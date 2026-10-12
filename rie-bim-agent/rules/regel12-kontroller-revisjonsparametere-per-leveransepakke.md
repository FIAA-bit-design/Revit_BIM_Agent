# Regel 12: Kontroller revisjonsparametere per leveransepakke

Før Excel-verdien brukes for et manglende revisjonsfelt på et kabelbro- eller trekkerørselement, undersøk direkte fysisk tilkoblede føringselementer i samme system og leveransepakke. Bruk en entydig utfylt instansverdi fra disse kildene, også når pakken ikke har en fane i revisjonslisten. Hvis tilkoblede kilder har ulike verdier eller en kildeparameter er tvetydig, blokker feltet og rapporter kilde-ID-er; ikke velg Excel-verdien for å skjule konflikten. Hvis ingen tilkoblet kilde har en brukbar verdi, brukes godkjent Excel-verdi når pakken har en fane. Eksisterende utfylte modellverdier kontrolleres fortsatt mot Excel som før.

Kjør etter Regel 11 som siste delregel i fullkontrollen.

- C#-implementasjon: `../../CSharpRules/Rule12RevisionParameters.cs`
- Arbeidsbok velges fra aktiv modell: `U_F_STA_FBU_RIE_*.rvt` bruker `../../config/FBU STA revisjonsliste.xlsx`; `U_F_BAS_FBU_RIE_*.rvt` bruker `../../config/FBu BAS revisjonsliste.xlsx`. Ukjent modellnavn blokkerer regelen før revisjonsverdier endres.
- Append-only logg: `../../logs/history/Rule12_RevisionParameters.log`

Kontroller instansparameterne `FOB_Revisjonsdato`, `PGF_Revisjonsign` og `PGF_Revisjonsindeks` for de elektriske elementkategoriene som omfattes av Regel 3. Senterlinjer utelates. Velg arbeidsbokfane med nøyaktig samme navn som elementets `FOB_Leveransepakke`.

Finn hver godkjent verdi ved å slå opp parameternavnet i kolonnen `Parameternavn` og lese kolonnen `Verdi`; ikke baser oppslaget på faste radnumre. Hver brukt fane må ha nøyaktig én utfylt rad for hvert av de tre revisjonsfeltene. Hvis arbeidsboken eller en nødvendig godkjent verdi ikke kan leses, blokkeres regelen før modellverdier endres.

 Hvis `FOB_Leveransepakke` er tom eller `--`, hoppes elementet over uten rapportering. Hvis pakken mangler fane, fylles bare manglende revisjonsverdier på kabelbro- og trekkerørselementer når en entydig direkte tilkoblet kilde i samme pakke har verdien; konflikter eller tvetydige kilder blokkerer feltet, og uten brukbar kilde hoppes feltet over. For pakker med fane sammenlignes alle tre modellverdiene med godkjente verdier. Fyll manglende verdier og korriger avvik, også når modellverdien allerede er utfylt. Manglende eller dupliserte målparametere og skrivebeskyttede avvik rapporteres som blokkeringer.

Regelen endrer modellverdier i én transaksjon og håndterer den spesifikke checkout-dialogen etter worksharing-kravene i [utføringsreglene](../core/execution-rules.md). Rapporter antall verdier som samsvarte, ble oppdatert eller ble blokkert. Loggen er append-only; bevar eldre kjøringer.

Hver kjøring lager `../../logs/reports/Rule12_RevisionParameters <tid>.txt` med samme sammendrag og oppdaterte/utestående verdier som kjøreloggen. Append-only historikklogg bevares separat.
