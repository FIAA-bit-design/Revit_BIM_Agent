# -*- coding: utf-8 -*-
from __future__ import print_function

import os
import io
import subprocess
import sys
import tempfile
import time


__version__ = "0.0.13"


def wait_for_marker(marker_path, timeout_seconds, timeout_message):
    deadline = time.time() + timeout_seconds
    while not os.path.isfile(marker_path):
        if time.time() >= deadline:
            raise IOError(timeout_message)
        time.sleep(2)


def main():
    workspace = os.path.dirname(os.path.abspath(__file__))
    instruction = os.path.join(
        workspace, "rie-bim-agent", "rie-bim-kontroll.instructions.md"
    )
    local_app_data = os.environ.get("LOCALAPPDATA")
    if not local_app_data:
        user_profile = os.environ.get("USERPROFILE")
        if not user_profile:
            raise IOError(u"Finner verken LOCALAPPDATA eller USERPROFILE.")
        local_app_data = os.path.join(
            user_profile, "AppData", "Local"
        )
    code = os.path.join(local_app_data, "Programs", "Microsoft VS Code", "Code.exe")
    code_cli = os.path.join(
        local_app_data, "Programs", "Microsoft VS Code", "bin", "code.cmd"
    )

    for required_file in (code, code_cli, instruction):
        if not os.path.isfile(required_file):
            raise IOError(u"Finner ikke filen: {}".format(required_file))

    subprocess.check_call(
        [code, "--reuse-window", workspace, instruction],
        cwd=workspace,
    )

    marker_descriptor, preflight_marker = tempfile.mkstemp(
        prefix="rie-bim-agent-preflight-", suffix=".txt"
    )
    os.close(marker_descriptor)
    os.remove(preflight_marker)
    completion_marker = os.path.join(
        tempfile.gettempdir(), "rie-bim-agent-complete.txt"
    )
    if os.path.isfile(completion_marker):
        os.remove(completion_marker)

    preflight_prompt = (
        u"Utfør kun en lesende MCP-preflight for Revit. Kontroller at Revit-hosten "
        u"svarer, hent aktivt dokument og list tilgjengelige Revit-verktøy. Hvis "
        u"hosten midlertidig ikke finner en aktiv instans, gjenta den lesende "
        u"tilkoblingssjekken et par ganger med kort pause. Ikke åpne eller les "
        u"RIE BIM-instruksjonen, og ikke utfør modellkontroll eller modellendringer. "
        u"Hvis host og aktivt dokument er bekreftet, skriv nøyaktig READY på første "
        u"linje og dokumenttittelen på andre linje i denne filen: {}. Hvis kontrollen "
        u"feiler etter eventuelle retry-forsøk, skriv BLOCKED på første linje og "
        u"kort årsak på andre linje. Opprett filen først når kontrollen er ferdig. "
        u"Ikke still spørsmål."
    ).format(preflight_marker)

    subprocess.check_call(
        [code, "--reuse-window", workspace],
        cwd=workspace,
    )
    subprocess.check_call(
        [
            os.environ.get("COMSPEC", "cmd.exe"),
            "/d", "/c", "call", code_cli,
            "chat", "--reuse-window", "--mode", "agent",
            preflight_prompt,
        ],
        cwd=workspace,
    )
    wait_for_marker(
        preflight_marker,
        5 * 60,
        u"Revit-preflight svarte ikke innen 5 minutter. BIM-instruksjonen ble ikke startet.",
    )
    with io.open(preflight_marker, "r", encoding="utf-8") as marker_file:
        preflight_result = marker_file.read().strip()
    os.remove(preflight_marker)
    preflight_status, separator, preflight_details = preflight_result.partition(u"\n")
    if preflight_status != u"READY" or not separator or not preflight_details.strip():
        raise IOError(
            u"Revit-preflight blokkerte oppstarten: {}".format(
                preflight_details or preflight_result or u"Ugyldig preflight-resultat."
            )
        )
    document_title = preflight_details.splitlines()[0].strip()
    print(u"Revit og aktiv modell bekreftet: {}".format(document_title))

    prompt = (
        u"Oppstartssteget har bekreftet Revit-host og aktiv modell via lesende "
        u"MCP-preflight: {}. Bruk denne overleveringen som obligatorisk preflight; "
        u"ikke gjenta en ren tilkoblingssjekk. "
        u"Les vedlagte RIE BIM-instruksjon som bindende oppgave og start "
        u"utførelsen umiddelbart; ikke bare oppsummer eller vent på en ny "
        u"brukermelding. Bruk det bekreftede aktive dokumentet som måldokument "
        u"uten å be om bekreftelse, og rapporter dokumenttittelen. Utfør alle relevante regler i angitt "
        u"rekkefølge med C# via Revit-host; ikke kjør separate Python-skript. "
        u"Ved kjøringsfeil, undersøk feilmeldingen og relevant "
        u"C#-regel, rett selv bare entydige feil, bygg en midlertidig "
        u"C#-emulering med dotnet build, og prøv den berørte regelen på "
        u"nytt bare hvis dette ikke gjentar en allerede utført modellendring. "
        u"Ved uavklart data, manglende implementasjon eller usikker retry, "
        u"logg blokkeringen og fortsett med uavhengige, trygge regler. "
        u"Hvis hosten mangler etter instruksjonens retry-forsøk, stopp og "
        u"rapporter blokkeringen. Ikke gjett eller omgå andre eksplisitte "
        u"blokkeringer i instruksjonen. "
        u"Skriv en ny tidsstemplet sluttrapport i arbeidsmappen med dokument, "
        u"regelresultater, feilrettinger, retries og uløste blokkeringer. "
        u"Ikke overskriv tidligere logger. Når alle trygge regler er forsøkt "
        u"og rapporten er skrevet, opprett en "
        u"tom fil her: {}. Ikke opprett markøren før oppgaven er avsluttet."
    ).format(document_title, completion_marker)

    subprocess.check_call(
        [
            os.environ.get("COMSPEC", "cmd.exe"),
            "/d", "/c", "call", code_cli,
            "chat", "--reuse-window", "--mode", "agent",
            "--add-file", instruction, prompt,
        ],
        cwd=workspace,
    )
    timeout_seconds = 5 * 60 * 60
    wait_for_marker(
        completion_marker,
        timeout_seconds,
        u"BIM-agenten fullførte ikke innen {} timer. Se agentchatten."
        .format(timeout_seconds // 3600),
    )
    print(u"BIM-kontrollen er fullført. Ferdigmarkøren er opprettet.")


if __name__ == "__main__":
    try:
        main()
    except (IOError, OSError, subprocess.CalledProcessError) as error:
        print(u"BIM-agenten feilet eller ble ikke fullført: {}".format(error), file=sys.stderr)
        sys.exit(1)