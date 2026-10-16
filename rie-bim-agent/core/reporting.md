# Rapportering

Rapporter etterkontrollens faktiske modellresultat, ikke et gammelt snapshot. Skill tydelig mellom rettet og verifisert, tilbakeført, teknisk blokkert og faglig uavklart.

Ta med ElementId, leveransepakke, årsak og eventuelle kilde-ID-er/verdier. Lag tidsstemplet Excel-liste når manuell kontroll er nødvendig eller oppdraget bruker Excel som avviksrapport. Bevar tidligere rapporter og brukerens manuelle notater; historikklogger er append-only UTF-8. Kontroller at Excel-filen kan åpnes og at radantall, ID-er og verdier stemmer.

## Excel-kvalitetskontroll ved oppretting og oppdatering

- Excel skal kunne åpne listen uten reparasjon. At `openpyxl.load_workbook` lykkes er ikke alene tilstrekkelig validering.
- For Excel-tabeller skal tabellområdet og tabellens autofilter samsvare med de faktiske kolonnene. Antall `tableColumns` skal være lik områdets bredde, kolonne-ID-er skal være positive og unike, og kolonnenavn skal være unike og samsvare nøyaktig med overskriftscellene.
- Ved utvidelse av en eksisterende `openpyxl`-tabell: erstatt `tableColumns` med en ny liste av `TableColumn(id=1..N, name=overskrift)` og oppdater `ref` og `autoFilter.ref`. Ikke kall `_initialise_columns()` på en tabell som allerede har kolonner; metoden legger til nye definisjoner og kan gi ugyldige duplikater.
- Kjør [validate_excel.py](../../scripts/validate_excel.py) på hver ny eller oppdatert XLSX-fil, for eksempel `py -3.14 scripts\validate_excel.py "config\DrofusOccurrenceBaseline.xlsx"` fra prosjektroten. Verktøyet kontrollerer ZIP/XML og tabeller mot arkoverskrifter; det erstatter ikke full Excel-/Open XML-validering. Kontroller også data og radantall etter lagring.
- Ved feil i en eksisterende liste: bevar en tidsstemplet kopi før reparasjon. Endre bare ugyldig struktur; bevar data, Space-kolonner, manuelle notater og referansestatus. Hvis filen er låst i Excel, be brukeren lukke den; ikke avslutt brukerens Excel-prosess.

Hver delregel skal lage en tidsstemplet rapport fra sin egen kjøring; rapporten supplerer, men erstatter ikke, den append-only historikkloggen. Bevar eventuelle særskilte XLSX-/CSV-rapporter som regelen allerede lager. Avslutt oppdraget med en kort oppsummering av det som faktisk er rettet, verifisert og fortsatt uavklart. Rapporter feil, blokkeringer og fullstendige rapport-/loggbaner.
