# Regel 12: Kontroller revisjonsparametere per leveransepakke

Kjør etter Regel 11 og før den skrivebeskyttede sluttrapporten i Regel 13.

- C#-implementasjon: `../../CSharpRules/Rule12RevisionParameters.cs`
- Arbeidsbok: `../../config/Revisjonsliste.xlsx`
- Append-only logg: `../../logs/history/Rule12_RevisionParameters.log`

Kontroller instansparameterne `FOB_Revisjonsdato`, `PGF_Revisjonsign` og `PGF_Revisjonsindeks` for de elektriske elementkategoriene som omfattes av Regel 3. Senterlinjer utelates. Velg arbeidsbokfane med nøyaktig samme navn som elementets `FOB_Leveransepakke`.

Finn hver godkjent verdi ved å slå opp parameternavnet i kolonnen `Parameternavn` og lese kolonnen `Verdi`; ikke baser oppslaget på faste radnumre. Hver brukt fane må ha nøyaktig én utfylt rad for hvert av de tre revisjonsfeltene. Hvis arbeidsboken eller en nødvendig godkjent verdi ikke kan leses, blokkeres regelen før modellverdier endres.

Hvis `FOB_Leveransepakke` er tom, `--` eller ikke finnes som fane, hoppes elementet over uten rapportering. For pakker som finnes i arbeidsboken, sammenlignes alle tre modellverdiene med godkjente verdier. Fyll manglende verdier og korriger avvik, også når modellverdien allerede er utfylt. Manglende eller dupliserte målparametere og skrivebeskyttede avvik rapporteres som blokkeringer.

Regelen endrer modellverdier i én transaksjon og håndterer den spesifikke checkout-dialogen etter worksharing-kravene i [utføringsreglene](../core/execution-rules.md). Rapporter antall verdier som samsvarte, ble oppdatert eller ble blokkert. Loggen er append-only; bevar eldre kjøringer.