# Regel 5: Kjør skript for lastenavn til datakurser

Ved kontroll før leveranse skal den vedlikeholdte C#-implementasjonen kjøres mot den aktive leveransemodellen:

- C#-implementasjon: `../../CSharpRules/Rule05LoadNameToDataCircuits.cs`
Bekreft aktivt Revit-dokument via obligatorisk preflight før kjøring. C#-regelen endrer data i modellen; rapporter kjøringen og eventuelle feil tydelig.

C#-regelen skal skrive logg append-only til `../../logs/history/Rule05_LoadNameToDataCircuits.log`. Ikke bruk, opprett, endre eller skriv til Python-standardhistorikken `D:\Revit\Python\Load name til datakurser for fordelinger\Load name til datakurser for fordelinger historikk.log` eller manuellhistorikken `D:\Revit\Python\Load name til datakurser for fordelinger\Load name til datakurser for fordelinger historikk manuell.log`. Begge Python-historikkfilene skal forbli urørt av C#-regelen.
