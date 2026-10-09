# Regel 6: Vask koblingspunkter for MEK

Ved relevant RIE-kontroll før leveranse skal du kjøre dette skriptet:

- C#-implementasjon: `../../CSharpRules/Rule06CleanMekConnectionPoints.cs`
Bekreft at aktivt Revit-dokument er riktig leveransemodell før kjøring. C#-regelen skriver parameterverdier i modellen; rapporter utfallet og eventuelle feil etter kjøring. Bevar standard historikklogg `D:\Revit\Python\Vask koblingspunkter MEK\Vask koblingspunkter MEK historikk.log`; ikke slett eller overskriv historikk manuelt.

Hver kjøring lager `../../logs/reports/Rule06_CleanMekConnectionPoints <tid>.txt` med oppsummering, oppdateringer og uavklarte parametere. Append-only agentlogg bevares separat.
