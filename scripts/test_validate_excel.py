import tempfile
import unittest
from pathlib import Path
from zipfile import ZipFile

from validate_excel import validate_excel


class ExcelTableValidationTests(unittest.TestCase):
    def make_workbook(self, path, *, count=2, ids=(1, 2), names=("Id", "SpaceNavn"), filter_ref="A1:B2"):
        main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
        with ZipFile(path, "w") as archive:
            archive.writestr(
                "xl/worksheets/sheet1.xml",
                f'<worksheet xmlns="{main}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
                '<sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Id</t></is></c>'
                '<c r="B1" t="inlineStr"><is><t>SpaceNavn</t></is></c></row></sheetData>'
                '<tableParts count="1"><tablePart r:id="rId1"/></tableParts></worksheet>',
            )
            archive.writestr(
                "xl/worksheets/_rels/sheet1.xml.rels",
                '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                '<Relationship Id="rId1" Target="../tables/table1.xml"/></Relationships>',
            )
            columns = "".join(f'<tableColumn id="{i}" name="{n}"/>' for i, n in zip(ids, names))
            archive.writestr(
                "xl/tables/table1.xml",
                f'<table xmlns="{main}" displayName="TestTable" ref="A1:B2">'
                f'<autoFilter ref="{filter_ref}"/><tableColumns count="{count}">{columns}</tableColumns></table>',
            )

    def test_valid_table(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "test.xlsx"
            self.make_workbook(path)
            self.assertEqual(validate_excel(path), 1)

    def test_invalid_table_metadata(self):
        cases = [
            {"count": 3},
            {"ids": (1, 1)},
            {"ids": (0, 2)},
            {"names": ("Id", "Id")},
            {"names": ("Id", "WrongHeader")},
            {"filter_ref": "A1:A2"},
            {"count": 4, "ids": (1, 2, 1, 2), "names": ("Id", "SpaceNavn", "Column1", "Column2")},
        ]
        with tempfile.TemporaryDirectory() as directory:
            for case in cases:
                with self.subTest(case=case):
                    path = Path(directory) / "test.xlsx"
                    self.make_workbook(path, **case)
                    with self.assertRaises(ValueError):
                        validate_excel(path)


if __name__ == "__main__":
    unittest.main()
