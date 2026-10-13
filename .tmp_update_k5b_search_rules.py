import os
import posixpath
import tempfile
import zipfile
import xml.etree.ElementTree as ET

WORKBOOK = os.path.join("config", "Parameterliste.xlsx")
MAIN_NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
REL_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
PKG_REL_NS = "http://schemas.openxmlformats.org/package/2006/relationships"
XML_NS = "http://www.w3.org/XML/1998/namespace"
ET.register_namespace("", MAIN_NS)
ET.register_namespace("r", REL_NS)

RULE_TEXT = {
    "FOB_Omraade": (
        "For OST_CableTray og OST_CableTrayFitting beholdes Space-/connectorregelen. "
        "For andre K5B-elementer i Regel 3: Ved blank FOB_Omraade eller -- kopieres "
        "nærmeste gyldige instansverdi med samme FOB_Entreprise=K5B og "
        "FOB_Leveransepakke, men annen familie. Primærsøk 50–1500 mm; fallback "
        "1500–3000 mm bare uten primærtreff. Hvis ingen kandidat finnes innen "
        "3000 mm, fortsetter K5B-søket uten øvre avstandsgrense til nærmeste gyldige "
        "kandidat. Motstridende kilder innen 1 mm, manglende identitet eller ingen "
        "kandidat noe sted gir ingen skriving og logges uavklart. Bevar gyldige verdier."
    ),
    "PGF_PNS": (
        "Ved blank PGF_PNS eller -- kopieres nærmeste gyldige instansverdi med samme "
        "FOB_Entreprise=K5B og FOB_Leveransepakke, men annen familie. Primærsøk "
        "50–1500 mm; fallback 1500–3000 mm bare uten primærtreff. Hvis ingen kandidat "
        "finnes innen 3000 mm, fortsetter K5B-søket uten øvre avstandsgrense til "
        "nærmeste gyldige kandidat. Motstridende kilder innen 1 mm, manglende identitet "
        "eller ingen kandidat noe sted gir ingen skriving og logges uavklart. Bevar gyldige verdier."
    ),
}


def cell_text(cell, shared_strings):
    cell_type = cell.attrib.get("t")
    if cell_type == "inlineStr":
        node = cell.find(f"{{{MAIN_NS}}}is")
        return "" if node is None else "".join(t.text or "" for t in node.iter(f"{{{MAIN_NS}}}t"))
    value = cell.find(f"{{{MAIN_NS}}}v")
    if value is None or value.text is None:
        return ""
    if cell_type == "s":
        return shared_strings[int(value.text)]
    return value.text


def main():
    workbook_path = os.path.abspath(WORKBOOK)
    temporary_path = None
    with zipfile.ZipFile(workbook_path, "r") as source:
        workbook = ET.fromstring(source.read("xl/workbook.xml"))
        relationships = ET.fromstring(source.read("xl/_rels/workbook.xml.rels"))
        relationship_map = {
            relation.attrib["Id"]: relation.attrib["Target"]
            for relation in relationships.findall(f"{{{PKG_REL_NS}}}Relationship")
        }
        parameter_sheet = next(
            sheet for sheet in workbook.iter(f"{{{MAIN_NS}}}sheet")
            if sheet.attrib.get("name") == "Parameterliste"
        )
        relation_id = parameter_sheet.attrib[f"{{{REL_NS}}}id"]
        target = relationship_map[relation_id]
        sheet_path = target.lstrip("/") if target.startswith("/") else posixpath.normpath(posixpath.join("xl", target))
        shared_strings = []
        if "xl/sharedStrings.xml" in source.namelist():
            shared_xml = ET.fromstring(source.read("xl/sharedStrings.xml"))
            shared_strings = [
                "".join(text.text or "" for text in item.iter(f"{{{MAIN_NS}}}t"))
                for item in shared_xml.findall(f"{{{MAIN_NS}}}si")
            ]
        sheet = ET.fromstring(source.read(sheet_path))
        found = set()
        for row in sheet.iter(f"{{{MAIN_NS}}}row"):
            cells = {cell.attrib.get("r"): cell for cell in row.findall(f"{{{MAIN_NS}}}c")}
            row_number = row.attrib["r"]
            parameter_name = cell_text(cells.get(f"A{row_number}"), shared_strings)
            if parameter_name not in RULE_TEXT:
                continue
            rule_cell = cells.get(f"B{row_number}")
            if rule_cell is None:
                raise ValueError(f"Missing cell B{row_number}")
            rule_cell.attrib["t"] = "inlineStr"
            for child in list(rule_cell):
                if child.tag in (f"{{{MAIN_NS}}}v", f"{{{MAIN_NS}}}is"):
                    rule_cell.remove(child)
            inline = ET.SubElement(rule_cell, f"{{{MAIN_NS}}}is")
            text = ET.SubElement(inline, f"{{{MAIN_NS}}}t")
            text.set(f"{{{XML_NS}}}space", "preserve")
            text.text = RULE_TEXT[parameter_name]
            found.add(parameter_name)
        if found != set(RULE_TEXT):
            raise ValueError(f"Expected both parameter rows, found {sorted(found)}")
        sheet_bytes = ET.tostring(sheet, encoding="utf-8", xml_declaration=True)
        fd, temporary_path = tempfile.mkstemp(prefix="Parameterliste.", suffix=".xlsx", dir=os.path.dirname(workbook_path))
        os.close(fd)
        try:
            with zipfile.ZipFile(temporary_path, "w") as destination:
                for info in source.infolist():
                    data = sheet_bytes if info.filename == sheet_path else source.read(info.filename)
                    destination.writestr(info, data)
        except Exception:
            if temporary_path and os.path.exists(temporary_path):
                os.remove(temporary_path)
            raise
    try:
        os.replace(temporary_path, workbook_path)
        temporary_path = None
    finally:
        if temporary_path and os.path.exists(temporary_path):
            os.remove(temporary_path)
    with zipfile.ZipFile(workbook_path, "r") as updated:
        updated_workbook = ET.fromstring(updated.read("xl/workbook.xml"))
        updated_rels = ET.fromstring(updated.read("xl/_rels/workbook.xml.rels"))
        updated_map = {
            relation.attrib["Id"]: relation.attrib["Target"]
            for relation in updated_rels.findall(f"{{{PKG_REL_NS}}}Relationship")
        }
        updated_sheet = next(
            sheet for sheet in updated_workbook.iter(f"{{{MAIN_NS}}}sheet")
            if sheet.attrib.get("name") == "Parameterliste"
        )
        updated_target = updated_map[updated_sheet.attrib[f"{{{REL_NS}}}id"]]
        updated_path = updated_target.lstrip("/") if updated_target.startswith("/") else posixpath.normpath(posixpath.join("xl", updated_target))
        updated_xml = ET.fromstring(updated.read(updated_path))
        updated_shared = []
        if "xl/sharedStrings.xml" in updated.namelist():
            strings_xml = ET.fromstring(updated.read("xl/sharedStrings.xml"))
            updated_shared = [
                "".join(text.text or "" for text in item.iter(f"{{{MAIN_NS}}}t"))
                for item in strings_xml.findall(f"{{{MAIN_NS}}}si")
            ]
        checked = {}
        for row in updated_xml.iter(f"{{{MAIN_NS}}}row"):
            cells = {cell.attrib.get("r"): cell for cell in row.findall(f"{{{MAIN_NS}}}c")}
            row_number = row.attrib["r"]
            name = cell_text(cells.get(f"A{row_number}"), updated_shared)
            if name in RULE_TEXT:
                checked[name] = cell_text(cells.get(f"B{row_number}"), updated_shared)
        if checked != RULE_TEXT:
            raise RuntimeError("Workbook validation failed for FOB_Omraade/PGF_PNS")
    for name, text in checked.items():
        print(f"{name}: {text}")


if __name__ == "__main__":
    main()
