# -*- coding: utf-8 -*-
# pyright: reportMissingImports=false, reportUndefinedVariable=false
# IronPython-skript for Autodesk Revit 2026.
# Formål: Skrive en samlet, skrivebeskyttet sluttlogg etter BIM-kontrollen.
# Bruk ElementId.Value (int64), ikke ElementId.IntegerValue.

import clr
import datetime
import io
import os
import traceback

try:
    from pyrevit import forms as pyrevit_forms
except Exception:
    pyrevit_forms = None

clr.AddReference("RevitAPI")
clr.AddReference("RevitAPIUI")

from Autodesk.Revit.DB import FilteredElementCollector
from Autodesk.Revit.UI import TaskDialog

SCRIPT_TITLE = "Felles BIM-kontroll logg"
SCRIPT_VERSION = "0.0.4"
REPORT_DIRECTORY = r"D:\Revit\Python\Revit_BIM_Agent\logs\history"
PARAMETER_NAMES = (
    "FOB_Leveransepakke",
    "PGF_Mengdetype",
    "FOB_Merkestreng",
)


def resolve_context():
    try:
        revit_app = globals().get("__revit__")
        ui_doc = revit_app.ActiveUIDocument if revit_app else None
        doc = ui_doc.Document if ui_doc else None
        if doc:
            return ui_doc, doc
    except Exception:
        pass

    try:
        doc = globals().get("doc")
        if doc:
            return None, doc
    except Exception:
        pass

    return None, None


def to_unicode(value):
    if value is None:
        return u""
    try:
        return unicode(value)
    except Exception:
        try:
            return unicode(str(value), "utf-8", "replace")
        except Exception:
            return u""


def clean_cell(value):
    text = to_unicode(value)
    return text.replace(u"\t", u" ").replace(u"\r", u" ").replace(u"\n", u" ").strip()


def is_missing_value(value):
    return not value or value == u"--"


def get_parameter_value(element, parameter_name):
    parameter = element.LookupParameter(parameter_name)
    if parameter is None or not parameter.HasValue:
        return None, u""

    if parameter.StorageType.ToString() == "String":
        value = parameter.AsString()
    else:
        value = parameter.AsValueString()

    return parameter, clean_cell(value)


def get_output_path():
    if not os.path.isdir(REPORT_DIRECTORY):
        raise Exception(u"Fant ikke mappen for felleslogg: {}".format(REPORT_DIRECTORY))

    timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S_%f")
    base_name = u"Felles BIM-kontroll logg {}".format(timestamp)
    path = os.path.join(REPORT_DIRECTORY, base_name + ".log")
    suffix = 1
    while os.path.exists(path):
        path = os.path.join(REPORT_DIRECTORY, "{}_{:03d}.log".format(base_name, suffix))
        suffix += 1
    return path


def collect_snapshot(doc):
    rows = []
    missing_counts = dict((name, 0) for name in PARAMETER_NAMES)
    absent_parameter_counts = dict((name, 0) for name in PARAMETER_NAMES)
    total_instances = 0

    elements = (
        FilteredElementCollector(doc)
        .WhereElementIsNotElementType()
        .ToElements()
    )

    for element in elements:
        total_instances += 1
        parameters = []
        values = []
        for parameter_name in PARAMETER_NAMES:
            parameter, value = get_parameter_value(element, parameter_name)
            parameters.append(parameter)
            values.append(value)

        if not any(parameter is not None for parameter in parameters):
            continue

        for index, parameter in enumerate(parameters):
            parameter_name = PARAMETER_NAMES[index]
            if parameter is None:
                absent_parameter_counts[parameter_name] += 1
            elif is_missing_value(values[index]):
                missing_counts[parameter_name] += 1

        rows.append((values[0], values[1], values[2], unicode(element.Id.Value)))

    rows.sort(key=lambda row: int(row[3]))
    return total_instances, rows, missing_counts, absent_parameter_counts


def write_snapshot(doc, total_instances, rows, missing_counts, absent_parameter_counts):
    generated_at = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        u"Rapport: {} v{}".format(SCRIPT_TITLE, SCRIPT_VERSION),
        u"Dokument: {}".format(clean_cell(doc.Title)),
        u"Opprettet: {}".format(generated_at),
        u"Instanser undersøkt: {}".format(total_instances),
        u"Elementer i rapporten: {}".format(len(rows)),
        u"Manglende verdier FOB_Leveransepakke (blank/--): {}".format(missing_counts["FOB_Leveransepakke"]),
        u"Parameter ikke til stede FOB_Leveransepakke: {}".format(absent_parameter_counts["FOB_Leveransepakke"]),
        u"Manglende verdier PGF_Mengdetype (blank/--): {}".format(missing_counts["PGF_Mengdetype"]),
        u"Parameter ikke til stede PGF_Mengdetype: {}".format(absent_parameter_counts["PGF_Mengdetype"]),
        u"Manglende verdier FOB_Merkestreng (blank/--): {}".format(missing_counts["FOB_Merkestreng"]),
        u"Parameter ikke til stede FOB_Merkestreng: {}".format(absent_parameter_counts["FOB_Merkestreng"]),
        u"",
        u"FOB_Leveransepakke\tPGF_Mengdetype\tFOB_Merkestreng\tElementId",
    ]

    for package_value, quantity_type, mark_string, element_id in rows:
        lines.append(u"\t".join((package_value, quantity_type, mark_string, element_id)))

    output_path = get_output_path()
    with io.open(output_path, "w", encoding="utf-8", newline="") as handle:
        handle.write(u"\r\n".join(lines) + u"\r\n")
    return output_path


def run():
    ui_doc, doc = resolve_context()
    if doc is None:
        raise Exception(u"Fant ikke et aktivt Revit-dokument.")

    total_instances, rows, missing_counts, absent_parameter_counts = collect_snapshot(doc)
    output_path = write_snapshot(doc, total_instances, rows, missing_counts, absent_parameter_counts)
    summary = (
        u"Felleslogg opprettet. {} elementrader. Manglende verdier (blank/--): "
        u"FOB_Leveransepakke {}, PGF_Mengdetype {}, FOB_Merkestreng {}. "
        u"FOB_Leveransepakke-parameter ikke til stede: {}. Logg: {}"
    ).format(
        len(rows),
        missing_counts["FOB_Leveransepakke"],
        missing_counts["PGF_Mengdetype"],
        missing_counts["FOB_Merkestreng"],
        absent_parameter_counts["FOB_Leveransepakke"],
        output_path,
    )
    print(summary)
    TaskDialog.Show(SCRIPT_TITLE, summary)


if __name__ == "__main__":
    try:
        run()
    except Exception as error:
        TaskDialog.Show(SCRIPT_TITLE + u" - feil", to_unicode(error))
        raise
