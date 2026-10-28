# -*- coding: utf-8 -*-
# IronPython script for COWI Assistant's Run Python Script for Revit task.

import clr
import glob
import os
import traceback

clr.AddReference("RevitAPI")
clr.AddReference("RevitAPIUI")

from Autodesk.Revit.UI import TaskDialog


SCRIPT_TITLE = "Regel 14"
FALLBACK_WORKSPACE_ROOT = r"D:\Revit\Python\Revit_BIM_Agent"


def get_workspace_root():
    script_path = globals().get("__file__")
    if script_path:
        script_directory = os.path.dirname(os.path.abspath(script_path))
        if os.path.basename(script_directory).lower() == "rule14runner":
            return os.path.dirname(script_directory)
    if os.path.isdir(FALLBACK_WORKSPACE_ROOT):
        return FALLBACK_WORKSPACE_ROOT
    raise IOError("Fant ikke arbeidsområderoten for Regel 14.")


def find_runner_assembly(workspace_root):
    pattern = os.path.join(
        workspace_root,
        "Rule14Runner",
        "bin",
        "*",
        "net8.0-windows",
        "Rule14Runner.2026.*.dll",
    )
    assemblies = glob.glob(pattern)
    if not assemblies:
        raise IOError(
            "Fant ikke bygget Rule14Runner. Bygg Rule14Runner/Rule14Runner.csproj først."
        )
    return max(assemblies, key=os.path.getmtime)


def get_active_document():
    try:
        from rpw import revit

        document = revit.doc
        if document:
            return document
    except Exception:
        pass

    revit_application = globals().get("__revit__")
    ui_document = revit_application.ActiveUIDocument if revit_application else None
    if ui_document:
        return ui_document.Document

    document = globals().get("doc")
    if document:
        return document
    raise RuntimeError("Fant ikke et aktivt Revit-dokument.")


def run():
    workspace_root = get_workspace_root()
    document = get_active_document()
    assembly_path = find_runner_assembly(workspace_root)
    clr.AddReferenceToFileAndPath(assembly_path)

    from CW.Assistant.Generated import Rule14PythonActionBridge

    result = Rule14PythonActionBridge.Execute(document, workspace_root)
    print(result)
    TaskDialog.Show(SCRIPT_TITLE, result)


try:
    run()
except Exception as error:
    details = traceback.format_exc()
    print(details)
    TaskDialog.Show(SCRIPT_TITLE + " - feil", str(error))
    raise