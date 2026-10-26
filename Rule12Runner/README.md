# Rule12Runner

Separat viewverktøy for Revit 2026. Navnet er historisk; verktøyet er ikke Regel 12 i den ordinære BIM-regelrekken.

## Gjeldende fagregler

- Arbeidet er avgrenset til bekreftet BAS-modell `U_F_BAS_FBU_RIE_XXX.rvt`, viewet `KS Kablebro dRofus` og K5B-kabelbroer (`OST_CableTray`). Strømskinner utelates etter [dRofus-viewfilteret](../rie-bim-agent/standards/drofus-view-filter.md).
- C4 identifiseres ved typenavnet og har egne dRofus-artikler: **400 mm → 16289**, **600 mm → 16290**, **123 mm → 16291** for de tre bekreftede typene i [C4-artikkeltabellen](../rie-bim-agent/standards/drofus-view-filter.md#c4-har-egne-drofus-artikler). Ordinære typer beholder sine egne ID-er. Samme bredde er ikke nok til samme artikkel eller strekktilhørighet; ikke slå sammen ordinær utførelse og C4. Artikkelendring er en separat typeparameterhandling, ikke del av viewskjulingen.
- Det skal være **én synlig representant per dokumentert kabelbrostrekk**. Flere segmenter med samme gyldige merkestreng på samme dokumenterte strekk skal ikke synkroniseres som separate kabelbroer.
- Behold entydig eksisterende synkronisert representant. Når ingen eller flere er entydige kandidater, har brukeren godkjent lengste segment som representant, med laveste ElementId ved lik lengde. En allerede registrert representant skal likevel ikke byttes automatisk.
- Lik merkestreng, nærhet eller tilsynelatende horisontal retning er ikke alene bevis på samme strekk. Bruk dokumentert segmenttilhørighet eller et uttrykkelig brukerbekreftet elementutvalg. Parallelle broer, vertikale avgreninger og ulike dimensjoner slås ikke sammen uten avklaring.
- **Alle føringsveisegmenter innen omfanget skal ha gyldig `FOB_Merkestreng` og indeks/sekvensnummer (`FOB_Sekvensnummer`)**, også skjulte segmenter og segmenter som ikke synkroniseres separat. Alle dokumentert tilhørende segmenter på samme strekk skal følge representantens gyldige dRofus-merke og sekvensnummer. Tom tekst, `--` eller feilmelding er mangler, ikke godkjente verdier.
- Viewskjuling fritar ikke fra utfyllingskravet. Et strekk er ikke ferdig behandlet før merke og sekvensnummer er kontrollert på alle tilhørende segmenter. Mangler gyldig dRofus-kilde, beholdes eksisterende verdier og alle berørte segmenter rapporteres som uløste avvik; ikke dikt opp verdier. Tilhørighet skal fortsatt dokumenteres uavhengig av om feltene er utfylt.
- Skjul ekstrasegmentene **bare i avtalt view**. Ikke slett modellelementer, endre geometri, parametere eller occurrence-ID-er. Ikke lagre eller synkroniser modellen automatisk.
- Skjuling rydder ikke forekomster som allerede finnes i dRofus. Sletting eller nullstilling krever separat avklaring.
- dRofus er fasit for merkestreng og sekvensnummer. Parametersamordning er en separat handling etter [occurrence-standarden](../rie-bim-agent/standards/drofus-occurrence-reference.md), ikke en del av viewfiltreringen. Referansens `Pending`-status og Regel 3-vernet beholdes.
- Kontroller riktig modell, view og elementutvalg før endring. Forhåndsvis planlagt representant og skjulte ElementId-er, og utfør separat lesende etterkontroll av synlighet og uendrede modelldata etter commit.

## Implementasjonsstatus

[Rule12RunnerCommand](Rule12RunnerCommand.cs) bruker nå [kørutinen](../CSharpRules/DrofusRouteQueueRunner.cs) for registrering og samlet viewskjuling. Standardhandlingen er `RegisterSelection`, som ikke starter noen Revit-transaksjon. Nye markeringer krever ikke omskriving av C#-kilde eller egne engangsskript.

Den eldre [merkebaserte algoritmen](../CSharpRules/Rule12HideDuplicateCableTrays.cs) finnes fortsatt, men runneren tilbyr den bare som `LegacyPreview` (alltid lesende). Den er ikke en faglig godkjenning av strekktilhørighet og brukes ikke ved samlet utføring.

Avgrensede viewhandlinger utfører bare skjuling, ikke utfylling. Manglende merke og sekvensnummer er fortsatt uløste avvik, også på skjulte segmenter. At koblingene er lagret, betyr ikke at kompletthetskravet er oppfylt. Konkrete grupper, representanter og segment-ID-er dokumenteres i registeret og Excel-listen nedenfor, ikke som parallelle oppslag i denne README-en.

## Filoppsett og hva som er nødvendig

Rule12Runner er et valgfritt, separat viewverktøy; det er ikke en kjørbar Regel 12 i kontrollrekkefølgen. Behold denne oppdelingen:

| Fil(er) | Nødvendig for | Merknad |
| --- | --- | --- |
| [Rule12Runner.csproj](./Rule12Runner.csproj), [Rule12RunnerCommand.cs](./Rule12RunnerCommand.cs), [Rule12RunnerArgs.cs](./Rule12RunnerArgs.cs), [GlobalUsings.cs](./GlobalUsings.cs), [Configurations.targets](./Configurations.targets), [nuget.config](./nuget.config) | Bygging og Assistant/Revit-inngang | Prosjektet begrenser kjøring til BAS-modellen og `KS Kablebro dRofus`. |
| [DrofusRouteQueueRunner.cs](../CSharpRules/DrofusRouteQueueRunner.cs), [DrofusRouteQueueStore.cs](../CSharpRules/DrofusRouteQueueStore.cs), [DrofusRouteQueueReport.cs](../CSharpRules/DrofusRouteQueueReport.cs), [ExcelListReportWriter.cs](../CSharpRules/ExcelListReportWriter.cs) | Køregistrering, forhåndsvisning, viewskjuling og Excel-rapport | Kildekodene ligger under `CSharpRules/`, men er eksplisitt inkludert i runner-prosjektet. `ExcelListReportWriter.cs` er en delt rapporthjelper. |
| [DrofusConfirmedRoutes.tsv](../config/DrofusConfirmedRoutes.tsv) | `PreviewBatch` og `ApplyBatch`, og senere berikelse basert på brukerbekreftede grupper | Autoritativt medlemsregister. Bevar det. |
| [DrofusRouteQueue.json](../config/DrofusRouteQueue.json) | Fortsettelse av registrerte grupper mellom kjøringer | Vedvarende operativ kø; ikke slett eller regenerer for opprydding. Opprettes først ved registrering. `.lock` er en runtime-låsefil, ikke kødata. |
| [Rule12Runner.Tests](../Rule12Runner.Tests) | Tester av kølagring, representantvalg og Excel-rapport | Ligger gruppert med runneren i [Revit_BIM_Agent.sln](../Revit_BIM_Agent.sln); kjører uten Revit-modell. |
| [Rule12HideDuplicateCableTrays.cs](../CSharpRules/Rule12HideDuplicateCableTrays.cs) og `LegacyPreview`-argumentet | Bare den eldre, lesende forhåndsvisningen | Valgfritt for den nye køflyten, men beholdes så lenge `LegacyPreview` fortsatt eksponeres. Hvis den avvikles senere, fjernes både handlingen, dispatchen og prosjektinkluderingen samlet. |
| `logs/reports/BAS strekk ko …` | Historikk og revisjonsspor fra kjøringer | Rapporter overskrives eller slettes ikke som del av kildekodeopprydding. |

Prosjektet er gruppert i solution-folderen `Rule12Runner` sammen med [testprosjektet](../Rule12Runner.Tests/Rule12Runner.Tests.csproj). Kildekoden er fortsatt lenket fra `CSharpRules/`; fysisk flytting er ikke nødvendig for bygging og ville endret flere prosjektstier uten å gi ny funksjonalitet.

## Lagrede koblinger for berikelse etter dRofus-synk

[DrofusConfirmedRoutes.tsv](../config/DrofusConfirmedRoutes.tsv) er det varige registeret over brukerbekreftede grupper. Det skal leses fra disk når merkestreng og rominfo senere skal skrives, ikke rekonstrueres fra chat, dagens merke, fysisk nærhet eller utvalg i Revit. Den samlede [Excel-listen](../config/DrofusConfirmedRoutes.xlsx) gir filtrerbar oversikt over alle registrerte strekk og segmenter, også skjulte.

Registeret lagrer modellsti, view-UniqueId, strekkets interne nøkkel, `RepresentativeUniqueId`, ElementId, elementets `UniqueId`, forventet egen occurrence-ID og dimensjoner for alle registrerte elementer, inkludert representantene. Strekkets interne nøkkel er bare en registernøkkel, ikke en dRofus-merkestreng eller en kilde til romnummer. Merkestreng og rominfo lagres ikke som fasit i registeret; disse leses ferskt fra representanten etter synk.

Ved bestilt samordning brukes denne Excel-listen som strekkoversikt, kontrollert mot TSV-registerets medlemskap. Merkestreng, sekvensnummer og gyldig område hentes ferskt fra de registrerte representantene; gamle Excel-snapshotverdier er ikke skrivefasit. Alle segmenter i samme bekreftede strekk skal ha samme merke og sekvens. Manglende kilde eller merkekollisjon blokkerer berørt strekk og rapporteres; øvrige entydige strekk kan samordnes. Romutfylling og endring av occurrence-ID-er inngår ikke i en bestilling om merkesamordning.

Ved bestilling for **alle føringsveier** gjelder hele BAS K5B-kabelbroutvalget, også skjulte segmenter, ikke bare de sist registrerte gruppene. Bruk dokumenterte før-synk-koblinger i occurrence-referansen sammen med senere brukerbekreftede koblinger. Følg eksisterende representantkjeder til dagens representant; ikke utvid medlemskapet ved likt merke eller nærhet. Historiske occurrence-ID-er og feltverdier er ikke dagens fasit: les og bevar dagens ID-er, og hent skriveverdier ferskt fra representanten. Dette godkjenner ikke referansens `Pending`-status og starter ikke Regel 3. Rapporten skal dekke hele utvalget og skille samordningsavvik fra romavvik og manglende treff i siste dRofus-eksport.

### Fortløpende markeringer og samlet Excel-liste

#### Registrer først, utfør samlet etterpå

Aktiver `KS Kablebro dRofus` i BAS og marker ett strekk om gangen. Kjør den lokale Rule12Runner-prosjektversjonen med følgende argumenter:

| Action | PreviewOnly | Virkning |
| --- | --- | --- |
| `RegisterSelection` (standard) | `true` | Leser bare dagens markering, lagrer en ny gruppe og lager Excel. Ingen skjuling eller parameterendring. |
| `PreviewBatch` | `true` | Kontrollerer alle ventende/blokkerte grupper mot ferske modelldata. Endrer bare køstatus og rapportfiler. |
| `ApplyBatch` | **`false` uttrykkelig** | Kontrollerer og skjuler ekstrasegmentene gruppevis i samme kjøring. Krever ingen ny markering. |
| `LegacyPreview` | alltid lesende | Viser den gamle merkebaserte algoritmens forslag; utfører ikke køen. |

`ViewName` skal fortsatt være `KS Kablebro dRofus`. Argumentene finnes i [Rule12RunnerArgs](Rule12RunnerArgs.cs). Via Assistant sitt lokale prosjektverktøy brukes `Rule12Runner\Rule12Runner.csproj` med eksempelvis `{"Action":"RegisterSelection","PreviewOnly":true}`. Dette er prosjektkjøring, ikke publisering eller oppdatering av en tidligere installert/serverpublisert utgave.

Registreringene lagres varig i `config\DrofusRouteQueue.json` (opprettes ved første registrering), separat fra allerede utførte koblinger. Hver kjøring lager en tidsstemplet, samlet kørapport `logs\reports\BAS strekk ko <tid>.xlsx` og kjørelogg med samme prefiks. Excel inneholder status og én rad per segment på tvers av alle køgrupper. Excel-rapportene overskrives ikke; manuelle notater i en tidligere rapport bevares i den filen, men overføres ikke automatisk til en ny kørapport.

- `Pending`: registrert og venter på utføring.
- `Blocked`: modelldata, identitet, omfang eller synlighet avviker; årsaken står i rapporten. Ingen skjuling av denne gruppen. Andre gyldige grupper kan fortsatt utføres.
- `Applied`: gruppens viewskjuling er committed og kontrollert. Dette betyr **ikke** at merker/sekvens/rominfo er komplette.

Gjenta `RegisterSelection` for hvert nytt strekk. Ikke kjør `ApplyBatch` før registreringen er ferdig og du ønsker modellendringen. Samlet utføring bruker én tilbakeførbar transaksjonsgruppe per strekk; dette er ikke én alt-eller-ingenting-transaksjon for hele køen. Ved en teknisk feil stoppes kjøringen, og tidligere committede grupper beholdes og logges. Etter commit leses modelldata på nytt i handlingen og sammenlignes før gruppen godkjennes. Før rapportering til brukeren som ferdig etterkontrollert kreves også en separat lesende modellkontroll etter [utføringsreglene](../rie-bim-agent/core/execution-rules.md).

Oppføringer fra vellykket utføring føres til det eksisterende TSV-registeret for senere berikelse. Egen køstatus og sikkerhetskopier bevares. Gjentatt utføring beholder allerede utførte grupper uten å velge ny representant. Ved avbrudd mellom modellcommit og fillagring kan ny kjøring gjenkjenne allerede skjulte mål, men bare når alle registrerte modelldata fortsatt samsvarer. Ikke slett køen som feilsøkingsgrep.

Endres typer, parametere, occurrence-ID-er eller geometri etter registrering, blokkeres den berørte gruppen. Avklar endringen før en blokkert oppføring korrigeres i JSON-registeret; det finnes ikke en knapp for å overstyre kontrollene. Køfilen må ikke redigeres mens runneren kjører. En eksklusiv fillås hindrer samtidige køoppdateringer; atomisk erstatning bevarer forrige registerutgave i rapportmappen.

Excel viser **registreringstidspunktets** merke/sekvens/romnummer, ikke live-data. Å registrere eller skjule godkjenner ikke manglende verdier. Kørapportene har et eget navn og erstatter ikke [Excel-listen over utførte strekk](../config/DrofusConfirmedRoutes.xlsx).

#### Felles regler og rapport for utførte strekk

- Hver markering som brukeren sender inn for filtrering, er ett brukerbekreftet strekk. Agenten leser og forhåndskontrollerer det faktiske utvalget før endring; ikke overvåk eller behandle nye markeringer uten en ny bestilling.
- Behold registrert representant. For en ny gruppe beholdes den eneste kandidaten med positiv occurrence-ID og gyldig merke/sekvens dersom en slik finnes; ellers beholdes lengste segment, laveste ElementId ved lik lengde. Flere synkroniserte kandidater kan dermed samles etter brukerens godkjente valgregel. Valgt kilde og tidligere egne occurrence-ID-er dokumenteres.
- Ulike typer, dimensjoner eller C4/ordinær utførelse samt overlapp med en registrert gruppe krever avklaring før endring. Ikke utvid utvalget med nærliggende eller allerede skjulte segmenter på eget initiativ.
- Bruk kørutinen for nye grupper, ikke de tidligere utvalgslåste engangsskriptene. Bygg og kvalitetssjekk av rutinen utføres ved kodeendring, ikke som ny implementasjon for hvert registrerte strekk. Samlet utføring kontrollerer hver lagret identitet, type, parametere og geometri før skriving.
- Excel har fanene `Les meg`, `Strekk`, `Segmenter` og `Avvik`. Én rad per segment viser blant annet representant-ID/UniqueId, bredde, C4, egne occurrence-ID-er, synlighet, faktisk merke/sekvens/rominfo, representantens verdier og restavvik. Viewskjuling endrer ikke merkestrengene; forskjeller rapporteres frem til kontrollert etter-synk-berikelse.
- Forny listen over utførte strekk etter en samlet utføringsbatch fra et ferskt Revit-uttrekk av **alle** registrerte elementer, ikke bare synlige. Dette er fortsatt et separat agenttrinn; kørutinen lager kørapporten og oppdaterer TSV-koblingene, ikke denne arbeidsboken. [export_confirmed_routes.py](../scripts/export_confirmed_routes.py) leser registeret og snapshotet og oppdaterer Excel-listen. Uttrekket skal ha kolonnene `ElementId`, `UniqueId`, `TypeName`, `C4`, `OccurrenceId`, `ItemId`, `Hidden`, `Mark`, `Sequence`, `RoomId`, `RoomNumber`, `RoomName` og `LengthMm`. TSV-filer skal være UTF-8 med overskrift.
- Kjør fra prosjektroten: `py -3.14 scripts\export_confirmed_routes.py "logs\reports\<ferskt snapshot>.tsv"`. Excel-listen er en avledet rapport, ikke stedet for endring av medlemskap. Manuelle notater i `Segmenter`-kolonnen `Merknad` bevares etter UniqueId. Eksisterende arbeidsbok kopieres til en tidsstemplet rapport før oppdatering; ved fillås stoppes oppdateringen uten å lukke brukerens Excel.
- Valider hver oppdatert arbeidsbok med `scripts\validate_excel.py` og normal, skrivebeskyttet åpning via `scripts\Test-ExcelWorkbook.ps1` med varsler på. Oppgi at Excel-listen bare viser angitt snapshot, ikke live-modell eller automatisk dRofus-synk.

### Arbeidsrutine når brukeren ber om berikelse etter synk

1. Bekreft aktiv BAS-modell og avtalt omfang. Les registeret og hent elementene med **modellsti + UniqueId**; kontroller ElementId, view, dimensjoner, entreprise, synlighet og hver egen occurrence-ID. Stopp berørt gruppe ved manglende element eller endret identitet. Ikke slå opp ved å tolke UniqueId-suffikset som ElementId.
2. Bruk registerets representantkobling for akkurat de oppførte elementene. Den dokumenterer at tidligere separate representanter nå tilhører ett brukerbekreftet strekk, selv om de har hver sin positive occurrence-ID. Ikke omskriv den historiske Excel-referansen eller utvid gruppene automatisk.
3. Les representantens **aktuelle** `FOB_Merkestreng`, `FOB_Sekvensnummer` og `FOB_Omraade` fra Revit etter brukerens dRofus-synk. For romfelter leses dokumenterte medlemmer av samme strekk, og `RoomID`-valgregelen i occurrence-standarden avgjør kilden per mål. Bruk `Romnavn` som målparameter for romnavnet; ikke skriv `dRofus_Rombetegnelse` på segmentet. Revit-verdiene er kilde etter synk, ikke bevis på direkte kontroll mot dRofus-serveren.
4. Kontroller kilde, merkekollisjoner og entydig romtilhørighet etter [occurrence-standarden](../rie-bim-agent/standards/drofus-occurrence-reference.md). Mangler gyldig merke/sekvens, rapporter alle berørte segmenter som uløste avvik og bevar eksisterende verdier. Gruppen skal ikke rapporteres som ferdig beriket. Ikke finn på nummer, bruk et annet strekk eller gjenopprett et gammelt merke.
5. Lag en fersk, gjennomgåbar skriveplan med mål-ID, kilde-ID, parameter og før-/etterverdi. Samordne merke/sekvens og ikke-tomt område fra representanten. For romfelt betyr tom tekst og `--` manglende verdi; for RoomID betyr tom/`--`/`0` manglende verdi. Når målet allerede har positiv RoomID, velg romkilde blant dokumenterte medlemmer med samme RoomID, også hvis kilden er skjult eller ikke er dagens representant. Et annet RoomID på en annen del av strekket blokkerer ikke dette treffet; det skal heller ikke skrives over målets eksisterende RoomID. Krev entydig, gyldig romnummer og `Romnavn` for kildens RoomID. Når mål-RoomID mangler, bruk dagens representant bare dersom hele dokumenterte strekket har én positiv RoomID med entydig romnummer og romnavn. Bevar gyldige eksisterende romverdier; rapporter flere motstridende verdier innen samme RoomID som konflikt i stedet for å overskrive.
6. Bevar **hver egen occurrence-ID**, også positive ID-er på skjulte segmenter. Ikke kopier representantens occurrence-ID, nullstill ID-er, slett dRofus-forekomster, endre geometri eller gjøre skjulte segmenter synlige.
7. Før eventuell modellskriving: følg C#-bygg-, kvalitetssjekk-, transaksjons- og etterkontrollkravene i [utføringsreglene](../rie-bim-agent/core/execution-rules.md). Les verdiene separat tilbake etter commit og lag fersk avviksrapport for alle registrerte segmenter, også de skjulte. Behold Excel-referansens `Pending` og Regel 3-vernet.

### Romnavn og romnummer for kabelbrobend og kryss

`FillFittingRoomText` er en egen handling i Rule12Runner 0.0.6. Bruk `{"Action":"FillFittingRoomText","PreviewOnly":true}` for lesende forhåndsvisning og `PreviewOnly=false` for uttrykkelig bestilt utføring. Handlingen omfatter BAS K5B-bend/kryss og T-stykker, også skjulte, uavhengig av aktivt view og markering. `ViewName` beholder standardverdien; ingen view endres. Valgfri `TargetUniqueIds` avgrenser mål til en liste med UniqueId-er skilt med komma, semikolon eller linjeskift. Duplikater, angitt liste uten ID-er, manglende element eller feil kategori/entreprise/deltype stopper kjøringen før skriving. Uten avgrensning behandles alle støttede fittings; bruk et eksplisitt utvalg ved bestilling for konkrete elementer.

Strømskinner identifisert ved `Strømskinne` i familie- eller typenavn (skiftleiefritt) utelates før romtekstplanlegging og som kilde. De rapporteres separat på fanen `Unntak` med status `EXCLUDED`, ikke som blokkerte restavvik eller kontrollerte kabelbrobend/kryss. Dette gjelder også de åtte brukerbekreftede strømskinnebendene i LPE161: 16683440, 16683442, 16707932, 16707974, 16707976, 16708016, 16708019 og 16708020. Identifikasjonen er navnebasert, ikke en hardkodet ID-liste i kjøringen. Unntaket gjelder denne dRofus-romteksthandlingen, ikke generell fritakelse fra andre parameterkrav.

[DrofusConnectedFittingRoomTextFill.cs](../CSharpRules/DrofusConnectedFittingRoomTextFill.cs) fyller bare manglende `Romnavn` og `dRofus_RoomfunctionNo` fra direkte, gjensidig connector-tilkoblet rett K5B-kabelbro med samme eksisterende positive `dRofus_RoomID` som fittinget. Én komplett tilkoblet kilde er tilstrekkelig; en annen åpen ende blokkerer ikke. Flere kilder med samme RoomID må ha entydige ikke-tomme romverdier. Andre RoomID-er er ikke kilder for dette målet. Ingen komplett kilde, konflikt, skrivebeskyttelse eller annen eier gir blokkering, ikke gjetting. Tom tekst, bare mellomrom og `--` betyr manglende romtekst.

For T-stykker (`Tee`, `ChannelCableTrayTee`, `LadderCableTrayTee`) kan også manglende `dRofus_RoomID=0` fylles etter uttrykkelig bestilling. Krev nøyaktig én positiv RoomID blant alle direkte tilkoblede rette K5B-kabelbroer, én komplett romkilde og entydige tekstverdier for valgt RoomID. RoomID skrives før romtekst i samme tilbakeførbare transaksjon. Positiv eksisterende mål-RoomID bevares og velger kilde; negativ ID blokkeres. Ingen RoomID-utfylling for bend/Cross i denne handlingen. API-deltypen er avgjørende: en type kalt «Kabelbrokryss» kan være Tee. Skjøter/union og andre deltyper omfattes ikke.

Gyldig eksisterende romtekst, positive RoomID-er, alle occurrence-ID-er, øvrige parametere, geometri og forbindelser bevares. Bare Tee-unntaket ovenfor tillater utfylling av RoomID. Begge tekstfeltene på en kilde må være komplette; delkilder kombineres ikke til et nytt rom. Handlingen skriver kilde-/skriveplan før utføring og tidsstemplet XLSX-/TSV-rapport etterpå, med RoomID før/etter. Batchene ligger i én tilbakeførbar transaksjonsgruppe, med kontroll av mål, kilder, forbindelser og følgeendringer før gruppen bekreftes. Normal endring av utsjekkingsmetadata er ikke en fagdataendring. Feil etter bekreftet commit gir feilstatus med uttrykkelig melding om beholdt modellendring, ikke rollback; ikke gjenta blindt. Agenten utfører separat lesende etterkontroll og Excel-validering. Ingen lagring eller synkronisering foretas.

### Separat RoomID-handling

Den separate [DrofusConnectedFittingRoomIdFill.cs](../CSharpRules/DrofusConnectedFittingRoomIdFill.cs)-regelen behandler bare bend/kryss i `OST_CableTrayFitting` med `FOB_Entreprise=K5B`. Den leser direkte Revit-connectors og henter `dRofus_RoomID` fra rett, connector-tilkoblet `OST_CableTray` med K5B. Bare RoomID skrives; `dRofus_RoomfunctionNo`, `Romnavn`, occurrence-ID-er, geometri og view endres ikke. En entydig positiv kilde-ID kopieres, også når fittinget har en annen RoomID. Ved ulike positive kilde-ID-er bevares en eksisterende positiv ID på fittinget; mangler den, blokkeres og rapporteres konflikten. Mangler alle kilder positiv ID, skrives ingenting. Andre fittingtyper inngår ikke. Dette er en eksplisitt connector-regel for disse RoomID-verdiene, ikke generell dokumentasjon av strekkmedlemskap eller grunnlag for romnavn.

Dette er en **dokumentert agentarbeidsrutine med lagret koblingsgrunnlag**, ikke en hendelsesstyrt funksjon som starter når dRofus synkroniserer. Ingen ny berikelse er utført ved opprettelsen av registeret. Den tidligere engangsutfyllingen i [DrofusRouteFieldFill.cs](../CSharpRules/DrofusRouteFieldFill.cs) bruker en fast plan og krever occurrence-ID 0 på målsegmentene; den skal ikke kjøres uendret for nye grupper. Den validerer nå at en romkilde samsvarer med målets positive RoomID og tillater skjulte dokumenterte romkilder, men velger ikke kilden automatisk. Agenten må lage en ny plan med riktig RoomID-kilde og kontrollere den før en kontrollert handling.

## Vedlikehold og bygg

Ved nye brukeravklaringer om Rule12Runner eller dRofus-viewfilteret skal denne README-en og den relevante kanoniske standarden oppdateres i samme oppdrag, uten separat påminnelse. Skill alltid mellom vedtatte fagregler og faktisk implementasjon; dokumentasjonsoppdatering alene endrer ikke runner-koden.

Ved nye brukerbekreftede grupper: lagre representant og alle bekreftede medlemmers faktiske UniqueId/ElementId i registeret i samme oppdrag som viewendringen. Bevar tidligere bekreftede grupper, avvis overlappende medlemskap, og avklar ny representant før en eksisterende kobling endres. Ikke godkjenn nye grupper ved geometrisk gjetning.

Bygg fra denne mappen med `dotnet build Rule12Runner.csproj --configuration Debug`. Byggkontroll kjører ikke verktøyet mot modellen.

Fokuserte tester uten Revit-modell: `dotnet build Rule12Runner.Tests\Rule12Runner.Tests.csproj` fra prosjektroten, deretter `dotnet Rule12Runner.Tests\bin\Debug\net8.0\Rule12Runner.Tests.dll`. Testene dekker kølagring, overlapp, representantvalg, atomisk erstatning, backup og Excel-struktur. De erstatter ikke modelltest av `RegisterSelection`/`ApplyBatch`.

Excel-eksporten skriver ekte tomme celler, eksplisitte arkområder og arbeidsbokvisning med frosne overskrifter. Viderebehandling og Python-eksport bruker `save_excel` fra [validate_excel.py](../scripts/validate_excel.py); tabellfilter og arkfilter skal ikke overlappe. Følg [rapporteringsregelen](../rie-bim-agent/core/reporting.md) og kjør også `.\.venv\Scripts\python.exe -m unittest discover -s scripts -p test_validate_excel.py -v` ved endring av Excel-koden. Et valgfritt filargument til C#-testprogrammet beholder en kørapport og en `-format.xlsx`-testfil for kontroll av C#-/openpyxl-samspill og normal Excel-åpning; uten filargument slettes testfilene automatisk.