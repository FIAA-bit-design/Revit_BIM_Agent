# Brukerfiltre for avgrensede kontroller

## Mengdelistepostkontrollen

Følgende utelatelser fra brukerens kommentarer i kildekolonnen gjelder bare `FOB_Mengdelistepost`-kontrollen knyttet til `Logg av endringer`, inkludert mål, generiske kilder, connector-kilder og tilhørende avvikstelling/rapporter. De endrer ikke den generelle endringssammenligningen eller andre parameterregler. Bruk familienavn uten hensyn til store/små bokstaver:

- Familienavn som inneholder `port` eller `veggboks`, utelates.
- Familien `Personheis` utelates ved eksakt familienavn. Excel-teksten `=K2:K1107` er ikke et filter for hele celleområdet.
- Familienavn som inneholder `teknisk`, utelates bare i kategorien `OST_ElectricalEquipment`; bruk kategori-ID, ikke lokalisert visningsnavn.
- Familienavn som inneholder `adgangskontroll`, utelates med unntak for navn som også inneholder `grensesnittboks`. Unntaket gjelder adgangskontrollregelen; andre uavhengige filterregler gjelder fortsatt.
- Instanser med nøyaktig én tekst-instansparameter `MC System Code`, der verdien etter trimming er `733.1`, utelates. Ikke bruk `MC Default System Code`, typeparameter-fallback, prefiksmatching eller hele familien som reserve. Manglende, duplisert eller feil lagringstype er ikke bevis på kode `733.1`.

Eksisterende senterlinje-, entreprise- og detaljpilfilter beholdes. Utelatte elementer skal ikke brukes som kilder eller telles som gjenstående avvik. Dokumenter utelatelsesårsak og ElementId separat, uten å endre eller slette elementer/parameterverdier. Bevar brukerens opprinnelige hovedrapport og kommentarer; skriv en ny tidsstemplet filtrert rapport.

## Unntak i lesende romrapporter

Gul markering i familie-/typecellen bekrefter et romrapportunntak etter valgt navn/prefiks. Rød markering er ikke et unntak: undersøk rominformasjon fra direkte, gjensidig fysisk tilkoblet føringsvei for den markerte instansen, identifisert med modellsti og UniqueId. Ikke utvid modellskriving til andre instanser av samme familie/type uten bestilling. Bruk bare entydige gyldige kildeverdier innen avtalt omfang, bevar eksisterende gyldige målverdier og occurrence-ID-er, og følg bygg-/transaksjons-/etterkontrollkravene før eventuell retting. Manglende rominfo på tilkoblede kilder gir dokumentert blokkering uten skriving; det er ikke en instruks om å bruke nærhet, koble om eller gjette rom.

[RoomInformationExceptions.json](../../config/RoomInformationExceptions.json) lagrer brukerbekreftede prefiksfiltre fra gule familie-/typeceller i BAS K5B-romrapporten. Bruk skiftleiefri startmatching på akkurat feltet i regelen: `Familienavn` eller `Type`. Ikke gjør typen `Aspirasjonsrør` til et generelt unntak for familien `Conduit with Fittings`. Filteret `3D Detaljpil nummer` med etterfølgende mellomrom gjelder nummererte typer. Unntakene gjelder bare lesende rapportering av manglende `Romnavn` og `dRofus_RoomfunctionNo`, ikke andre BIM-parametere, modellretting eller koblingsgrunnlag.

Bruk [export_missing_room_report.py](../../scripts/export_missing_room_report.py) med kilderapport og en ny utfil, for eksempel `.\.venv\Scripts\python.exe scripts\export_missing_room_report.py "logs\reports\<kilde>.xlsx" "logs\reports\<ny filtrert rapport>.xlsx"`. Verktøyet kontrollerer modellsti og K5B-avgrensning, viderefører tidligere unntak og bevarer originalfilen med gule markeringer/notater. Fanen `Unntak` viser hver faktisk familie-/typekombinasjon bare én gang, med antall elementer, årsak og ElementId-/UniqueId-lister. Flere ulike typer i samme familie er egne kombinasjoner. Fanen `Filterregler` viser de gjenbrukbare prefiksene. Tell utelatte elementer separat fra restavvik. Ny rapport merkes som filtrering av eksisterende snapshot, ikke som en ny modellkontroll, og lagres/valideres etter [rapporteringsregelen](../core/reporting.md). Test med `.\.venv\Scripts\python.exe -m unittest discover -s scripts -p test_export_missing_room_report.py -v`.
