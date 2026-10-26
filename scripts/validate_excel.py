"""Safely save XLSX files and validate views, inline strings and table definitions."""

import argparse
import os
import posixpath
import tempfile
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

from openpyxl.utils.cell import range_boundaries
from openpyxl.workbook.views import BookView

MAIN = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
REL = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}"
PACKAGE = "{http://schemas.openxmlformats.org/package/2006/relationships}"


def column_number(reference: str) -> int:
    result = 0
    for char in reference:
        if char.isdigit():
            break
        result = result * 26 + ord(char) - ord("A") + 1
    return result


def ranges_overlap(first: str, second: str) -> bool:
    first_left, first_top, first_right, first_bottom = range_boundaries(first)
    second_left, second_top, second_right, second_bottom = range_boundaries(second)
    return (first_left <= second_right and second_left <= first_right
            and first_top <= second_bottom and second_top <= first_bottom)


def save_excel(workbook, path: Path) -> None:
    path = Path(path)
    if not workbook.views:
        workbook.views = [BookView()]
    for worksheet in workbook:
        for row in worksheet:
            for cell in row:
                if cell.value == "" or (cell.value is None and cell.data_type != "n"):
                    cell.value = None
                    cell.data_type = "n"
        for table in worksheet.tables.values():
            sheet_filter = worksheet.auto_filter.ref
            if sheet_filter and ranges_overlap(sheet_filter, table.ref):
                if range_boundaries(sheet_filter) != range_boundaries(table.ref):
                    raise ValueError(f"{worksheet.title}: worksheet autoFilter partially overlaps table range")
                if worksheet.auto_filter.filterColumn or worksheet.auto_filter.sortState is not None:
                    raise ValueError(f"{worksheet.title}: active worksheet autoFilter must be preserved or explicitly migrated")
                worksheet.auto_filter.ref = None
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix=f".{path.stem}.", suffix=".xlsx", delete=False) as stream:
        temporary = Path(stream.name)
    try:
        workbook.save(temporary)
        validate_excel(temporary)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def validate_excel(path: Path) -> int:
    with ZipFile(path) as archive:
        bad_entry = archive.testzip()
        if bad_entry:
            raise ValueError(f"Invalid ZIP entry: {bad_entry}")
        documents = {
            name: ET.fromstring(archive.read(name))
            for name in archive.namelist()
            if name.endswith((".xml", ".rels"))
        }
        shared = [
            "".join(node.itertext())
            for node in documents.get("xl/sharedStrings.xml", ET.Element("empty")).findall(f"{MAIN}si")
        ]
        workbook = documents.get("xl/workbook.xml")
        views = workbook.findall(f"{MAIN}bookViews/{MAIN}workbookView") if workbook is not None else []
        table_count = 0
        table_names: set[str] = set()
        for name, worksheet in documents.items():
            if not name.startswith("xl/worksheets/") or not name.endswith(".xml"):
                continue
            for view in worksheet.findall(f"{MAIN}sheetViews/{MAIN}sheetView"):
                if not 0 <= int(view.attrib.get("workbookViewId", "-1")) < len(views):
                    raise ValueError(f"{path.name}: {name}: sheetView references missing workbookView")
            cells = {
                cell.attrib["r"]: cell
                for cell in worksheet.findall(f"{MAIN}sheetData/{MAIN}row/{MAIN}c")
            }
            for reference, cell in cells.items():
                if cell.attrib.get("t") == "inlineStr" and cell.find(f"{MAIN}is") is None:
                    raise ValueError(f"{path.name}: {name}!{reference}: inlineStr cell missing is content")
            parts = worksheet.findall(f"{MAIN}tableParts/{MAIN}tablePart")
            if not parts:
                continue
            rel_name = posixpath.join(posixpath.dirname(name), "_rels", posixpath.basename(name) + ".rels")
            relations = {
                node.attrib["Id"]: node.attrib["Target"]
                for node in documents[rel_name].findall(f"{PACKAGE}Relationship")
            }
            for part in parts:
                target = relations[part.attrib[f"{REL}id"]]
                table_path = target.lstrip("/") if target.startswith("/") else posixpath.normpath(
                    posixpath.join(posixpath.dirname(name), target)
                )
                table = documents[table_path]
                label = f"{path.name}: {table_path}"
                sheet_filter = worksheet.find(f"{MAIN}autoFilter")
                if sheet_filter is not None and ranges_overlap(sheet_filter.attrib["ref"], table.attrib["ref"]):
                    raise ValueError(f"{label}: worksheet autoFilter overlaps table range")
                table_name = table.attrib["displayName"].casefold()
                if table_name in table_names:
                    raise ValueError(f"{label}: duplicate table name")
                table_names.add(table_name)
                start, end = table.attrib["ref"].split(":")
                width = column_number(end) - column_number(start) + 1
                columns_node = table.find(f"{MAIN}tableColumns")
                if columns_node is None:
                    raise ValueError(f"{label}: missing tableColumns")
                columns = list(columns_node)
                if len(columns) != width or int(columns_node.attrib["count"]) != width:
                    raise ValueError(f"{label}: {width} worksheet columns but {len(columns)} table columns")
                ids = [int(col.attrib["id"]) for col in columns]
                names = [col.attrib["name"] for col in columns]
                if len(set(ids)) != width or any(value <= 0 for value in ids):
                    raise ValueError(f"{label}: duplicate or invalid column IDs")
                if len({value.casefold() for value in names}) != width or any(not value.strip() for value in names):
                    raise ValueError(f"{label}: duplicate or empty column names")
                header_row = "".join(char for char in start if char.isdigit())
                headers = {}
                for ref, cell in cells.items():
                    if "".join(char for char in ref if char.isdigit()) != header_row:
                        continue
                    text = cell.findtext(f"{MAIN}v", "")
                    if cell.attrib.get("t") == "s":
                        text = shared[int(text)]
                    elif cell.attrib.get("t") == "inlineStr":
                        text = "".join(node.text or "" for node in cell.findall(f"{MAIN}is//{MAIN}t"))
                    headers[column_number(ref)] = text
                actual = [headers.get(index, "") for index in range(column_number(start), column_number(end) + 1)]
                if actual != names:
                    raise ValueError(f"{label}: table names differ from worksheet headers")
                auto_filter = table.find(f"{MAIN}autoFilter")
                if auto_filter is not None:
                    left, top, right, bottom = range_boundaries(table.attrib["ref"])
                    total_rows = int(table.attrib.get("totalsRowCount", "0"))
                    if total_rows not in (0, 1) or range_boundaries(auto_filter.attrib["ref"]) != (
                        left, top, right, bottom - total_rows
                    ):
                        raise ValueError(f"{label}: autoFilter differs from table range")
                table_count += 1
        return table_count


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="+", type=Path)
    args = parser.parse_args()
    for workbook_path in args.paths:
        count = validate_excel(workbook_path)
        print(f"OK: {workbook_path} ({count} tables)")
