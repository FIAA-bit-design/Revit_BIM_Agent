# Connectorregler

## Eksisterende rørforbindelser

For eksisterende, gjensidig fysisk tilkoblede rette rør og bend med samsvarende rund profil og diameter skal vinkel mellom connectorenes retninger alene ikke gi feil, avvik eller faglig vurderingspunkt. Bekreft `IsConnectedTo` i begge retninger og sammenlign faktisk profil og diameter før denne regelen brukes. Regelen gjelder generelt i agentens tilkoblingskontroller og rapporter, også ved kontroll knyttet til `Logg av endringer`. Den innfører ingen ny vinkeltoleranse for oppretting av forbindelser. Parameterfeil, profil-/størrelsesavvik, brutte forbindelser og andre selvstendige feil kontrolleres fortsatt.

Brukeren har godkjent vinkelobservasjonene for `10331953/0 ↔ 10332036/2` (25 mm) og `15248526/0 ↔ 15248541/2` (50 mm) i dokumentet `U_F_BAS_FBU_RIE_XXX`. Begge par er verifisert gjensidig tilkoblet med samsvarende rund profil og null connectoravstand 2026-10-04. De skal ikke rapporteres som feil eller vurderingspunkter på grunn av vinkel alene. Dette er ikke et generelt fritak for element-ID-ene eller for tilsvarende ID-er i andre dokumenter.

Ved tilkoblingskontroll knyttet til `Logg av endringer` skal agenten lese `connector_control` i den tilhørende `Logg av endringer.settings.json` som kontrollpolicy. Feltet brukes av agenten, ikke som en innebygd geometrikontroll. `approved_angle_observations` dokumenterer de godkjente forbindelsene; verifiser gjeldende dokument, forbindelse, profil og diameter før godkjenningen anvendes. Policyen utvider ikke entreprise- eller leveransepakkeomfanget. Bevar gamle rapporter, og skriv en ny tidsstemplet korreksjonsrapport når tidligere vinkelobservasjoner godkjennes.

## Entydig connectorretting

En åpen bendende er en korrekt modelleringsforutsetning, ikke i seg selv en feil. Agenten skal ikke automatisk koble en slik ende eller rapportere den som et modelleringsavvik. Bruk den allerede tilkoblede enden som kilde for parameterutfylling. Connectorretting kan bare gjøres når en konkret manglende forbindelse er bekreftet som feil, eller brukeren særskilt ber om retting av den aktuelle forbindelsen; nærhet eller en åpen ende er ikke tilstrekkelig grunnlag.

Ved en slik bekreftet connectorretting brukes bare ledige fysiske `ConnectorType.End`-connectorer, med samme entreprise, utfylte leveransepakke, føringsveikategori, domain, profil og størrelse. Maksimal avstand er 1 mm, størrelsesavvik maksimalt 0,01 mm, og retningene skal være motsatte (BasisZ-skalarprodukt høyst -0,9998). Rektangulære profiler må også ha samsvarende akser (absolutt BasisX-skalarprodukt minst 0,9998). Treffet skal være gjensidig entydig; en connector som passer til flere ender, utelates.

Bruk `ConnectTo` med et isolert delforsøk, og bekreft fysisk tilkobling med `IsConnectedTo` i begge retninger etter commit. Bevar alle eksisterende forbindelser. Kontroller posisjoner, kurver, connectorgeometri, elementtyper og element-ID-utvalg før og etter; toleranse for uendret geometri er 0,000001 fot. Tilbakefør hvis Revit flytter/forlenger elementer, bytter typer, oppretter/sletter elementer eller bryter andre forbindelser. Ikke utvid avstandsterskelen, flytt geometri eller opprett nye fittings uten særskilt tillatelse. Nærhet alene er aldri bevis på riktig tilkobling. Koble ikke elementer som er eid av andre brukere.
