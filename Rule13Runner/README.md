# Rule13Runner v0.0.7

Separat, lesende Assistant-verktøy for Revit 2026. Verktøyet lager en fersk Excel-utstyrsliste for kabling og inngår ikke i standard BIM-sekvens. Ingen transaksjoner, modellendringer, lagring, synkronisering eller automatisk instansbytte utføres.

## Bruk

Bygg fra prosjektroten:

```powershell
dotnet build Rule13Runner/Rule13Runner.csproj
```

Ved en senere, uttrykkelig bestilt modellkjøring: bekreft først tilkoblet Revit-instans og aktiv modell etter kanonisk preflight. Kjør det lokale prosjektet gjennom Assistant med argumentet:

```json
{"WorkspaceRoot":"D:\\Revit\\Python\\Revit_BIM_Agent"}
```

`WorkspaceRoot` må være en absolutt katalogbane med [konfigurasjonen](../config/CablingEquipmentExceptions.json) under `config`. Tom verdi bruker katalogen til runnerens DLL, aldri prosessens arbeidskatalog. Prosjektet kopierer konfigurasjonen til utdata ved bygging. Ved tom verdi havner rapportene under DLL-katalogens `logs/reports`; eksplisitt prosjektrot anbefales. Ingen kildearbeidsbok må være tilgjengelig for eksporten.

Resultatet returnerer den fullstendige banen til `logs/reports/Utstyr_til_kabling_K5B_BAS_FBU.xlsx` eller `logs/reports/Utstyr_til_kabling_K5B_STA_FBU.xlsx`. Rapportene bruker faste filnavn uten tidsstempel eller unik ID; en ny kjøring erstatter den eksisterende filen for samme modellkode. Eksporttidspunktet står i `Omfang`-arket. Ingen extension er registrert eller publisert som del av implementasjonen.

## Omfang og data

- I `Utstyr K5B` skjules kolonnene `UniqueId`, `Revit-kategori`, `Mark` og `MC Exclude From IFC View`. Dataene og kolonnerekkefølgen bevares og kan vises igjen i Excel. Andre faner påvirkes ikke.
- Hovedlisten `Utstyr K5B` viser `ElementId`, `Familienavn`, `Type`, `FOB_Merkestreng`, `FOB_Leveransepakke`, `Nivå`, `BetjenerMerke` og `BetjenerNavn`, i denne rekkefølgen. K5A JRP/JOS-arkene bruker det separate detaljoppsettet fra den brukerjusterte listen.
- `BetjenerMerke` og `BetjenerNavn` leses fra instansparameterne `PGF_RIE_BetjenerMerke` og `PGF_RIE_BetjenerNavn` bare når trimmet familie- eller typenavn begynner med `Koblingspunkt`, skiftleiefritt. Ingen typefallback. Manglende verdier og begge kolonner for andre elementer forblir tomme. Dette endrer ikke utvalg eller øvrige filtre og gjelder BAS og STA.
- Bare de eksakte modellstiene `Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt` (BAS) og `Autodesk Docs://Fornebubanen/U_F_STA_FBU_RIE_XXX.rvt` (STA) godtas. Alle instanser i modellen vurderes, også skjulte; ingen lenkemodeller. Aktivt view og markering avgrenser ikke listen. Andre modeller avvises.
- STA bruker de samme åtte familieunntakene, kategori-, boks- og IFC-filtrene som BAS og samme JRP-avgrensning. Jordspyd inngår ikke i STA-uttrekket; JOS-oppslag utføres bare i BAS. JSON-feltet `modelPath` angir fortsatt BAS som kilde for den felles unntakslisten; faktisk målmodell angis separat i `Omfang` og rapportnavnet.
- Hovedutvalget beholder trimmet `FOB_Entreprise=K5B` på instansen, uten typefallback og med uendrede kategori-, boks-, familie- og IFC-filtre.
- Et separat K5A JRP/JOS-tillegg følger avgrensningen nedenfor. Listen har derfor blandet entrepriseinnhold, selv om hovedfanens navn beholdes for kompatibilitet.
- Kategorier: Electrical Equipment, Electrical Fixtures, Lighting Fixtures, Lighting Devices, Data Devices, Fire Alarm Devices, Communication Devices og Security Devices. Filtrering bruker API-kategorier, ikke lokaliserte kategorinavn.
- Familieunntak leses fra JSON med skjema 1 og `match=familyExactIgnoreCaseTrim`: trimmet, skiftleiefri likhet. Aldri prefiks eller contains. Listen har nå åtte unike navn: Fiber kveileramme, Ankerskinne, Armaturskinne Zumtobel, Jordskinne, Lokk kabelstige, Stag, Adgangskontroll værhus og Adgangskontroll dørbladmontert kortleser. Antallet kan vedlikeholdes; tomme og dupliserte navn avvises. JSON er vedlikeholdskilden; ugyldig modellsti, entreprise eller matchmodus stopper eksporten.
- Familie-/typenavn som inneholder `boks` eller `box`, utelates skiftleiefritt før familieunntakene, som i originaluttrekket.
- JSON-feltet `namePrefixes` utelater K5B-elementer når trimmet familienavn eller typenavn begynner med `Slokkegass` eller `Inertgass`, skiftleiefritt. Dette er prefiksmatching, ikke contains; eksisterende eksakte familieunntak beholdes. Regelen gjelder både BAS og STA, ikke andre BIM-regler. Fanen `Unntak` teller prefikstreff separat etter boks- og eksaktfiltrene, uten dobbelttelling. Kildearbeidsbøker og manuelle notater endres ikke; filtrene brukes ved neste uttrekk.
- Originaluttrekket hadde også et infoelementfilter etter status/navn. Den nøyaktige definisjonen er ikke kjent og er ikke implementert. Nye modellrapporter kan derfor inneholde slike elementer og er ikke identiske med originaluttrekket. Den filtrerte Excel-kopien bevarer originalens utvalg.
- Instansparameteren `MC Exclude From IFC View` med eksisterende heltallsverdi ulik null utelates etter global BIM-regel. Manglende, verdiløs eller annen lagringstype er ikke et heltallsunntak. Familieunntak telles først; IFC-tellingen omfatter bare resterende kandidater.
- Alle rapporterte parametere leses fra instansene. Familie og typenavn hentes fra `FamilyInstance.Symbol`; nivå hentes fra instansens `LevelId`. Manglende verdier blir tomme. Ingen rominformasjon leses eller brukes.
- `Load Name i kurs` hentes fra instansens tilknyttede `ElectricalSystem`-kurser via `GetElectricalSystems()`. Ikke-tomme navn trimmes, dedupliseres skiftleiefritt, sorteres og skilles med semikolon. Ingen typefallback, panelgjetting eller romkilder.
- `ElementId.Value` skrives som tekst og beholder 64-bit-ID-er.

Hovedlisten `Utstyr K5B` inneholder K5B-utvalget og entydige K5A JRP/JOS-rader uten duplikater. K5A-arkene viser bare feltene i det egne detaljoppsettet. `Unntak` teller fortsatt bare K5B-familieunntak. Arkfanene kommer i rekkefølgen `Omfang`, `Utstyr K5B`, `K5A JRP`, `Uavklart JRP`, `K5A JOS`, `Uavklart JOS`, `Unntak`. `Omfang` beskriver blandet entrepriseinnhold, avgrensning, kilder og separate faktiske JRP/JOS-radantall og uavklarttellinger.

### Avgrenset K5A JRP-tillegg

- Bare de låste BAS-/STA-modellstiene godtas. Viewnavnet er konstanten `GroundingDraftingView`: `Utskrift 450 Detaljtegning jordskinner`. Navnet må finnes eksakt og entydig, og viewet må ha `ViewType.DraftingView`; ellers stoppes eksporten før rapportskriving.
- Les alle `TextNote` gjennom `FilteredElementCollector(document, view.Id).OfClass(typeof(TextNote))`, avgrenset til noter eid av dette viewet. Aktivt view, nærhet og 3D-synlighet brukes ikke.
- Parseren tar hele alfanumeriske, bindestrekdelte merker med et eget indre `-JRP-`-segment, for eksempel `BYG200-450-JRP-055` og `2FD-450-JRP-007`. JOS, RJB og delstrengmatch brukes ikke. Like merker dedupliseres skiftleiefritt; alle kilde-notenes ElementId beholdes.
- Match kun trimmet instansparameter `FOB_Merkestreng`, eksakt og skiftleiefritt. Aldri fallback til `Mark`. Gyldige mål er `FamilyInstance` i `OST_ElectricalFixtures`, familienavn starter med `Jordbolt` skiftleiefritt, trimmet entreprise er eksakt `K5A`, og instansen er ikke IFC-ekskludert. JSON-feltet `groundingEnterprise` er låst til `K5A`; eksisterende `enterprise=K5B` gjelder hovedutvalget.
- Bare merker med nøyaktig én gyldig instans inkluderes. Ingen treff eller flere gyldige instanser havner i `Uavklart JRP`, med merke, TextNote-ID-er, view, årsak, antall gyldige treff og samtlige treff-ID-er. Ingen første-instans-utvelging eller duplikatrader.
- `K5A JRP` viser de samme utstyrskolonnene samt faktisk `FOB_Entreprise`, TextNote ElementId, drafting-view og grunnlag. Radbyggingen gjenbrukes via mål-ID-settet. K5A-tillegget bruker egne kriterier, ikke K5B-familie-/boksunntak; IFC-filteret gjelder begge.
- Ingen rom eller lenkemodeller leses. Nye `Kablet`-celler er tomme; eksisterende arbeidsbøker endres ikke.

### Avgrenset K5A JOS-tillegg (bare BAS)

**Alle seks jordspyd er brukerbekreftet; JOS er ikke lenger blokkert.** Tillatte helstrengsmerker er kun `BYG200-450-JOS-001`, `BYG200-450-JOS-002`, `BYG200-450-JOS-003`, `BYG200-450-JOS-004`, `BYG200-450-JOS-005` og `BYG200-450-JOS-006`. Ingen automatisk inkludering av JOS007, andre bygg, RJB eller utvidede merker.

- Kilden må være en faktisk `TextNote` eid av det samme entydige `GroundingDraftingView`. Hele merker gjenkjennes og dedupliseres skiftleiefritt; kilde-ID-ene bevares.
- Match kun trimmet, eksakt skiftleiefri instansparameter `FOB_Merkestreng`, aldri `Mark`. Målet må være en unik `FamilyInstance` i `OST_ElectricalFixtures`, familienavn starter med `Jordspyd` skiftleiefritt, trimmet instans `FOB_Entreprise` er eksakt `K5A`, og integer IFC-verdi ulik null utelates. JRP må fortsatt matche `Jordbolt`; klassene blandes ikke.
- `K5A JOS` har samme `Headers` og grunnlagskolonner som `K5A JRP`. Nulltreff og flertreff bevares i `Uavklart JOS` med samme avviksgrunnlag som JRP; ingen første instans velges.
- JOS gjenbruker `ReadGroundingTargets`, `MatchGroundingIds` og utstyrsradbyggingen. Hovedarket `Utstyr K5B` får de entydige JOS-radene uten duplikater. `K5A JRP`, `Uavklart JRP`, JRP-utvalget og rapportens navnestruktur beholdes. `Omfang` har separate JRP/JOS-tellinger.
- Runneren gjør ingen modellendringer. Fargeendring og Excel-sammenslåing utføres separat; ingen slike handlinger inngår her.

## Kablet og opprinnelig snapshot

**Nye rapporter erstatter aldri eksisterende Kablet-kilder.** `Kablet` er alltid tomt i en ny modellrapport. Verktøyet leser, importerer, kopierer eller erstatter ikke brukerens manuelle notater eller eksisterende arbeidsbøker.

Det opprinnelige Excel-snapshotet inkluderte elementer merket `MC Exclude From IFC View` ulik null. Separat filtrering av dette snapshotet etter de åtte familieunntakene bevarer snapshotets øvrige omfang og manuelle data. Dette er ikke samme datasett som den nye modellrapporten, som i tillegg anvender globalt IFC-filter. `sourceWorkbook` og `sourceSheet` i JSON er kun dokumentasjon av unntakenes kilde; ingen åpning eller endring av arbeidsboken skjer her.

## Kontroller og begrensninger

Fokuserte modellfrie tester for v0.0.5, uten Excel-skriving:

```powershell
dotnet run --project Rule13Runner/Tests/Rule13Runner.Tests.csproj -- --jrp
```

De fokuserte testene dekker de seks bekreftede JOS-merkene, avvisning av JOS007/andre bygg/utvidede merker, JOS/Jordspyd og JRP/Jordbolt, JOS nulltreff/flertreff og hele JRP-merker, alfanumerisk bygningsdel, ikke JOS/RJB/substring, trimming, skiftleie, duplikatnoter, eksakt målmatch, nulltreff, flere gyldige treff, unike 64-bit-ID-er, K5A-/familie-/kategoribeslutning, IFC-filter og uendrede kolonneoverskrifter. De tester ikke Revit-parameterlesing, drafting-view-oppslag, kurskoblinger eller faktiske modelldata.

Eksisterende modellfrie tester uten `--jrp` beholdes for konfigurasjon, familie-/boksfiltre, rotsti og den uendrede Excel-writeren. Et valgfritt filargument i den modusen beholder en syntetisk testarbeidsbok. Disse bredere Excel-testene er ikke nødvendige for denne avgrensede endringen.

Før en ny modellrapport brukes: valider den med `scripts/validate_excel.py` og test normal skrivebeskyttet åpning i separat Excel-instans med `scripts/Test-ExcelWorkbook.ps1`, etter [rapporteringsreglene](../rie-bim-agent/core/reporting.md). Bygg og modellfrie tester er ikke modellkontroll. Ingen modellrapport, logg/dialog eller faktiske modellantall er verifisert uten en særskilt bestilt modellkjøring.
