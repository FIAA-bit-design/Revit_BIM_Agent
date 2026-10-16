"""Validate XLSX table definitions against their worksheet headers."""

import argparse
import posixpath
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

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
        table_count = 0
        table_names: set[str] = set()
        for name, worksheet in documents.items():
            if not name.startswith("xl/worksheets/") or not name.endswith(".xml"):
                continue
            parts = worksheet.findall(f"{MAIN}tableParts/{MAIN}tablePart")
            if not parts:
                continue
            rel_name = posixpath.join(posixpath.dirname(name), "_rels", posixpath.basename(name) + ".rels")
            relations = {
                node.attrib["Id"]: node.attrib["Target"]
                for node in documents[rel_name].findall(f"{PACKAGE}Relationship")
            }
            cells = {
                cell.attrib["r"]: cell
                for cell in worksheet.findall(f"{MAIN}sheetData/{MAIN}row/{MAIN}c")
            }
            for part in parts:
                target = relations[part.attrib[f"{REL}id"]]
                table_path = target.lstrip("/") if target.startswith("/") else posixpath.normpath(
                    posixpath.join(posixpath.dirname(name), target)
                )
                table = documents[table_path]
                label = f"{path.name}: {table_path}"
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
                if auto_filter is not None and auto_filter.attrib.get("ref") != table.attrib["ref"]:
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
