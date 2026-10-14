# Regel 12: Revisjonskontroll

Kjør Regel 12 etter Regel 3. Regel 3 fyller blank instansverdi i `FOB_Entreprise` med `K5B`; Regel 12 fyller ikke entreprise. Regel 12 kontrollerer bare elementer med eksakt `FOB_Entreprise = K5B` og revisjonsfeltene `FOB_Revisjonsdato`, `PGF_Revisjonsign` og `PGF_Revisjonsindeks`. Alle andre parametere og generell parameterkompletthet rapporteres av [Regel 3](regel03-parameterkontroll.md). Elementer med bekreftet IFC-ekskludering (`MC Exclude From IFC View` som én Integer-instansparameter med verdi ulik null) utelates fra revisjonskontrollen. Manglende, duplisert eller feiltypet IFC-parameter er ikke bevis på ekskludering.

Regel 2 kjøres etter Regel 12 og kan fylle leveransepakker senere i samme fullkontroll. Slike pakker og tilhørende revisjonsverdier blir først omfattet av revisjonskontrollen ved en senere Regel 12-kjøring.

- C#-implementasjon: `../../CSharpRules/Rule12RevisionParameters.cs`
- Arbeidsbok velges fra aktiv modell: `U_F_STA_FBU_RIE_*.rvt` bruker `../../config/FBU STA revisjonsliste.xlsx`; `U_F_BAS_FBU_RIE_*.rvt` bruker `../../config/FBu BAS revisjonsliste.xlsx`. Ukjent modellnavn blokkerer regelen før revisjonsverdier endres.
- Append-only logg: `../../logs/history/Rule12_RevisionParameters.log`

Kontroller instansparameterne `FOB_Revisjonsdato`, `PGF_Revisjonsign` og `PGF_Revisjonsindeks` for de elektriske elementkategoriene som omfattes av Regel 3. Senterlinjer utelates. Velg arbeidsbokfane med nøyaktig samme navn som elementets `FOB_Leveransepakke`.

Finn hver godkjent verdi ved å slå opp parameternavnet i kolonnen `Parameternavn` og lese kolonnen `Verdi`; ikke baser oppslaget på faste radnumre. Hver brukt fane må ha nøyaktig én utfylt rad for hvert av de tre revisjonsfeltene. Hvis arbeidsboken eller en nødvendig godkjent verdi ikke kan leses, blokkeres regelen før modellverdier endres.

 Hvis `FOB_Leveransepakke` er tom eller `--`, hoppes elementet over uten rapportering. Hvis pakken mangler fane, fylles bare manglende revisjonsverdier på kabelbro- og trekkerørselementer når en entydig direkte tilkoblet kilde i samme pakke har verdien; konflikter eller tvetydige kilder blokkerer feltet, og uten brukbar kilde hoppes feltet over. For pakker med fane sammenlignes alle tre modellverdiene med godkjente verdier. Fyll manglende verdier og korriger avvik, også når modellverdien allerede er utfylt. Manglende eller dupliserte målparametere og skrivebeskyttede avvik rapporteres som blokkeringer.

Regelen endrer modellverdier i én transaksjon og håndterer den spesifikke checkout-dialogen etter worksharing-kravene i [utføringsreglene](../core/execution-rules.md). Rapporter antall verdier som samsvarte, ble oppdatert eller ble blokkert. Loggen er append-only; bevar eldre kjøringer.

Hver kjøring lager `../../logs/reports/Rule12_RevisionParameters <tid>.xlsx` som en filtrerbar liste med type, ElementId, parameter, verdi og detalj. Rapporten inneholder bare revisjonsparametere og revisjonsrelaterte resultater. IFC-ekskluderte elementer brukes heller ikke som tilkoblede kilder for manglende revisjonsverdier. Append-only historikklogg bevares separat.
