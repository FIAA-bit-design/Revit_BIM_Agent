# Prosjektstrukturvurdering

Dato: 2026-10-04
Sist oppdatert: 2026-10-05

## Sammendrag

Prosjektet har allerede en modulær instruksjonsstruktur, men den gamle, fullstendige instruksjonen ligger også i prosjektroten. Arbeidsbøkene ligger i roten, C#-historikk ligger under kildekoden, og genererte rapporter ligger både i roten og i `Rapporter/`. Flere kjørende regler bruker absolutte filbaner.

Anbefalingen er én kanonisk instruksjon i `rie-bim-agent/rie-bim-kontroll.instructions.md`, arbeidsbøker og parameterregister i `config/` og logger/rapporter i `logs/`. Historiske filer flyttes uten sletting. De C#- og Python-kodeendringene som er nødvendige, begrenses til stikonstanter og valg av parameterlistens fil; regelalgoritmene endres ikke.

## Nåværende struktur

```text
Revit_BIM_Agent/
├── .agent-autoload-selftest/       # testinstruksjon, markør og selvtest
├── .github/hooks/                  # VS Code-livssykluskroker
├── .vscode/settings.json
├── CSharpRules/                    # Revit-regler og 10 eksisterende .log-filer
├── Rapporter/                      # 5 historiske workset-CSV-er
├── rie-bim-agent/                  # modulære core/, rules/, parameter-rules/, standards/
│   ├── rules/regel12-kontroller-revisjonsparametere-per-leveransepakke.md
│   ├── rules/regel13-lag-felles-sluttrapport.md
│   └── standards/                  # senterlinje- og connectorstandarder
├── rie-bim-kontroll.instructions.md # eldre full instruksjon
├── Revisjonsliste.xlsx
├── Workset liste.xlsx
├── Felles BIM-kontroll logg.py
├── Felles BIM-kontroll logg historikk.log
├── Felles BIM-kontroll logg 20261003_144240_330.csv
├── RIE BIM-kontroll sluttrapport 20261003_144240_330.txt
└── Slette duplikater infonoder funn 20261003_144141_601.xlsx
```

## Foreslått struktur

```text
Revit_BIM_Agent/
├── .agent-autoload-selftest/       # beholdes urørt
├── .github/                        # beholdes
├── .vscode/                        # beholdes
├── CSharpRules/                    # beholdes; anbefalt teknisk modul, ikke flyttet
├── config/
│   ├── Revisjonsliste.xlsx
│   ├── Workset liste.xlsx
│   └── Parameterliste.xlsx          # autoritativ parameterliste for Regel 3
├── logs/
│   ├── history/                    # append-only .log-filer
│   ├── csv/                        # eksisterende og nye CSV-rapporter/snapshots
│   └── reports/                    # Excel- og tekstrapporter
├── rie-bim-agent/
│   ├── core/
│   ├── rules/                      # kjørbare regler 1–12, sluttrapport 13 sist
│   ├── parameter-rules/
│   ├── standards/                  # senterlinje- og connectorstandarder
│   └── rie-bim-kontroll.instructions.md  # eneste kanoniske instruksjon
├── rie-bim-kontroll.instructions.legacy.txt # bevart arkivkopi; ikke kanonisk
└── PROJECT_STRUCTURE_REVIEW.md
```

Den eksisterende `Rapporter/`-mappen beholdes tom etter flytting; den slettes ikke automatisk.

## Identifiserte duplikater

- `rie-bim-kontroll.instructions.md` i roten og `rie-bim-agent/rie-bim-kontroll.instructions.md` er to ulike versjoner: rotfilen inneholder den eldre samlede teksten, mens den modulære filen er inngangsporten til oppdelte dokumenter. Innholdet er fordelt på modulfilene. Rotfilen flyttes til et tydelig ikke-kanonisk `.legacy.md`-navn, ikke slettet.
- Brukerprofilens instruksjonskilde ligger utenfor prosjektet og er ikke endret. Den globale oppstartsregelen er avgrenset slik at arbeidsområder med den modulære filen ikke får en ekstra rotkopi; andre arbeidsområder beholder backup-rutinen.
- Ingen andre identiske datakilder ble identifisert. Tidsstemplede rapporter er historikk, ikke duplikater som skal fjernes.

## Risikoer og konsekvenser

- `Rule03ParameterFill.cs` leser nå parameternavn fra `config/Parameterliste.xlsx`. Arbeidsboken må beholde fanen `Parameterliste` og kolonnene `Parameternavn`, `Fagregel / forutsetning` og `Regelreferanse`; manglende eller dupliserte parameternavn blokkerer regelen før elementlesing.
- `Rule03ParameterFill.cs` og `Rule10CheckWorksetCategory.cs` har absolutte baner til arbeidsbøkene. Når arbeidsbøkene flyttes, oppdateres bare disse banekonstantene og dokumentreferansene. Feil sti vil blokkere revisjons- eller worksetkontrollen.
- C#-reglenes absolutte loggbaner og rapportbaner oppdateres før tilhørende historikk-/rapportfiler flyttes. Logginnhold, filnavnsmønstre og append-only-egenskap beholdes. Nye rapporter går til riktig `logs/`-undermappe.
- Python-generert felleslogg får en endret output-katalog, men logikk og filnavnsmønster beholdes. Skriptet kjøres ikke som del av denne oppryddingen.
- `RIE BIM-kontroll sluttrapport ...txt` inneholder absolutte referanser til snapshot og Excel-rapport. Referansene oppdateres etter flytting, slik at historikkrapporten fortsatt peker til filene.
- En brukerprofilinstruksjon ved oppstart kopierte tidligere en ekstra fil til prosjektroten. Den er nå betinget: dette prosjektets modulære instruksjon hindrer rotkopi, mens andre arbeidsområder fortsatt får backup.
- Stier med `Revit_BIM_Agnet` i Markdown er feilstavet sammenlignet med faktisk `Revit_BIM_Agent`-mappe. De oppdateres til fungerende relative prosjektstier.

## Filer som flyttes

- `Revisjonsliste.xlsx` og `Workset liste.xlsx` til `config/`.
- `Parameterliste.xlsx` opprettes i `config/` som autoritativ parameterliste for Regel 3.
- Eksisterende `.log`-filer i roten og `CSharpRules/` til `logs/history/`.
- Den eksisterende rot-snapshoten og de fem CSV-filene i `Rapporter/` til `logs/csv/`.
- Den eksisterende infonode-Excel-rapporten og sluttrapporten `.txt` til `logs/reports/`.
- Den eldre rotinstruksjonen til `rie-bim-kontroll.instructions.legacy.txt`; ingen fil slettes.

## Filer og mapper som beholdes

- `CSharpRules/*.cs`, `.github/`, `.vscode/`, `.agent-autoload-selftest/`, Python-skript og alle historiske datafiler beholdes.
- `rie-bim-agent/core/`, `rules/`, `parameter-rules/` og `standards/` beholdes uendret i struktur.
- `CSharpRules/` flyttes ikke i denne endringen.
- `Rapporter/` beholdes som tom mappe; ingen sletting gjøres.

## Referanser og status

Før flytting var følgende aktive referanser identifisert: `Rule03ParameterFill.cs` til instruksjonstabellen og revisjonsarbeidsboken; `Rule10CheckWorksetCategory.cs` til worksetarbeidsboken og `Rapporter/`; `Rule08CheckInfoNodes.cs` til `Rapporter/`; øvrige C#-regler til egne `.log`-filer under `CSharpRules/`. Startskriptet pekte til brukerprofilens instruksjon, og Regel 3-dokumentet hadde en lenke til rotinstruksjonen.

Brutte eller foreldede referanser som ble funnet og rettet:

- C#-dokumentreferanser brukte den feilskrevne prosjektbanen `Revit_BIM_Agnet`; aktive regelreferanser bruker nå relative stier.
- Regel 3s C#-leser ble først flyttet fra rotinstruksjonen til `parameter-rules/parameter-list.md`; fra 2026-10-05 leser den parameternavn fra `config/Parameterliste.xlsx`.
- C#-baner til arbeidsbøkene, loggene og rapportmappene pekte til gamle plasseringer; de peker nå til `config/` og `logs/`.
- Den arkiverte sluttrapporten pekte til gamle plasseringer for snapshot og Excel-rapport; de to pekerne er oppdatert.
- Startskriptet og Regel 3-dokumentet peker nå til den modulære kanoniske instruksjonen.

Etter oppryddingen finnes ingen brutte aktive Markdown-lenker eller manglende flyttede filer. Arkivkopien beholdes for historikk; eventuelle gamle filbaner inne i denne uttrykkelig ikke-kanoniske kopien er ikke kjørende referanser. Ingen Revit-modelloperasjon inngår.

## Worksharing-reglenes plassering

**Oppdatert 2026-10-05:** Worksharing-kravene ligger i `core/execution-rules.md` fordi de styrer gjennomføringen av muterende regler og ikke er en selvstendig kontroll. Revisjonskontrollen er en egen Regel 12 fordi den slår opp mot `Revisjonsliste.xlsx`; sluttrapporten er Regel 13. `standards/` beholdes for senterlinje- og connectorstandardene.

Den tidligere anbefalingen om å beholde `standards/` gjaldt katalogen og de normerende tekniske standardene. Den endres ikke for de gjenværende filene.

## Parameterliste i Excel

**Oppdatert 2026-10-05:** `config/Parameterliste.xlsx` er autoritativ for parameternavn og fagregeltekster. Den inneholder 39 parametere med filter, låst topptekst, tekstbryting og egen kolonne for utdypende regelreferanser. `parameter-rules/parameter-list.md` beskriver generelle utfyllingsregler, men dupliserer ikke lenger tabellen.

`Rule03ParameterFill.cs` leser kolonnen `Parameternavn` fra fanen `Parameterliste` og validerer overskrifter, tomme rader med innhold og duplikater. De naturlige språkbeskrivelsene i Excel dokumenterer forutsetningene; de endrer ikke automatisk C#-regelens utførbare logikk.

De tre revisjonsfeltene kontrolleres separat av `Rule12RevisionParameters.cs` mot fanene og verdiene i `config/Revisjonsliste.xlsx`. Regel 3 skriver ikke lenger disse feltene.

## C#-modul

**Anbefaling: vurder `engine/CSharpRules/` som framtidig plassering, men behold `CSharpRules/` nå.** Koden kjøres som Revit-host-regler og fungerer som utføringsmotor; `engine/` beskriver rollen bedre enn `src/` så lenge det ikke finnes en vanlig byggbar applikasjons-/bibliotekstruktur. Flytting krever banendringer i instruksjoner, loggbaner, arbeidsflyt og eventuelle verktøy; den gjøres ikke uten egen godkjenning.

## Rekkefølge for opprydding

1. Opprett målmapper og registrer filbanekonsekvenser i denne rapporten. **Fullført.**
2. Flytt arbeidsbøker til `config/`, og oppdater de to C#-konstantene og dokumentreferansene. **Fullført.**
3. Pek Regel 3-leseren direkte på parameterlisten i Markdown-modulen, med samme navneuttrekk. **Fullført i forrige migrering; senere erstattet av Excel-kilden nedenfor.**
4. Oppdater logg-/rapportbanekonstanter og produsentbanen for fellesloggen. **Fullført; kun stier og skriptversjon ble endret.**
5. Flytt historiske logger og rapporter; oppdater sluttrapportens filreferanser. **Fullført; alle kildedata og historiske filer er bevart.**
6. Arkiver rotinstruksjonen uten sletting, oppdater prosjektinterne lenker og bruk den modulære filen som kanonisk. **Fullført.**
7. Valider referanser og katalogmål, bygg C#-emulering og sjekk Python-syntaks. **Fullført. Ingen regel ble kjørt mot modellen.**

## Status etter gjennomføring

- Tre arbeidsbøker/registre ligger i `config/`, inkludert den nye `Parameterliste.xlsx`.
- 11 historikkfiler ligger i `logs/history/`, 6 CSV-filer i `logs/csv/` og 2 rapportfiler i `logs/reports/`.
- Ingen eksisterende prosjektfiler ble slettet. Rotinstruksjonen er bevart som `rie-bim-kontroll.instructions.legacy.txt`; den kanoniske instruksjonen ligger kun i `rie-bim-agent/`.
- `Rapporter/` står igjen som en tom mappe; den er ikke slettet.
- Den fellesloggens Python-skriptversjon er økt fra `0.0.3` til `0.0.4` for stioppdateringen; skriptet ble ikke kjørt.
- Alle dokumentlenker og flyttede kilde-/outputbaner ble validert. Python-syntakskontrollen fant ingen feil. C#-emuleringens `dotnet build` fullførte med 0 feil og 0 advarsler.
