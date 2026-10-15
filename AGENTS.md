# Arbeidsinstruksjoner for agenter

Dette prosjektet inneholder Revit 2026-regler i C# og IronPython-skript for RIE BIM-kontroll.

## Kilder og struktur

- Før BIM-kontroll eller Revit-relatert arbeid: følg den [kanoniske BIM-instruksjonen](rie-bim-agent/rie-bim-kontroll.instructions.md) og de relevante modulene den lenker til. Rotfilen `rie-bim-kontroll.instructions.legacy.txt` er arkivert og ikke autoritativ.
- `CSharpRules/` inneholder regelimplementasjonene; runner-prosjektene kobler inn disse kildene for Assistant-integrasjon.
- `config/Parameterliste.xlsx` er autoritativ for Regel 3s parameternavn og redigerbare forutsetninger. Utførbar oppførsel må samsvare med den aktuelle C#-regelen og fagmodulen.
- `logs/` inneholder historikk og kjørerapporter. Bevar historikklogger append-only, og ikke slett rapporter som del av opprydding.

## Arbeidsflyt

- Før modellhandlinger, bekreft aktiv Revit-modell, regel og uttrykkelig angitt element-/entrepriseomfang. Ikke bytt modell, skript eller utvid omfang basert på antakelser; avklar tvetydighet først.
- Følg parameterens egen regel for tomverdier, faste verdier, filtrering og kildevalg. Ikke generaliser betydningen av `--`, `..` eller andre markører mellom parametere.
- Revit-Python-skript er laget for IronPython i Revit, ikke CPython. Behold UTF-8-kodingserklæringen, norske tegn og UTF-8-koding ved loggskriving. Bruk `ElementId.Value` for Revit 2026.
- Ved endring av skriptlogikk: bygg en midlertidig C#-emulering med `dotnet build` før eventuell modellkjøring. Byggkontroll er kun kompilering; modellkontroll og endringer følger separat den kanoniske Revit-arbeidsflyten.

## Bygg

Fra prosjektroten: `dotnet build Revit_BIM_Agent.sln`. Løsningen retter seg mot Revit 2026 og krever Revit API-referansene som angitt i prosjektfilene. Dette bygger koden, men kjører ikke regler mot en aktiv modell.