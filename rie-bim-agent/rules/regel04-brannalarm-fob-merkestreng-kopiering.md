# Regel 4: Brannalarm FOB_Merkestreng kopiering

Bruk familien `Brannalarm tilkoblingspunkt magnethold glideskinne` som child og finn parent-familier av typen kule i aktiv modell eller tilgjengelige lenkemodeller.

- C#-implementasjon: `../../CSharpRules/Rule04CopyAlarmParentData.cs`

Bruk malens etablerte parameterkobling:

- Bruk `FOB_ID` på child som treffnøkkel mot parent-parameteren `FOB_Merkestreng`.
- Kopier `FOB_Merkestreng` fra parent til `PGF_RIE_BetjenerMerke` på child.
- Kopier `PGF_Mengdetype` fra parent til `PGF_RIE_BetjenerNavn` på child.

`FOB_ID` er treffnøkkel, ikke en av verdiene som kopieres. Håndter manglende treff og avstandsmatching etter den etablerte logikken. Ikke overskriv eksisterende måldata uten at oppgaven uttrykkelig ber om det. Bruk instansparametere; ikke skriv `FOB_ID` eller bruk typeparameter-fallback.

Hver kjøring lager `../../logs/reports/Rule04_CopyAlarmParentData <tid>.txt` med sammendrag, treff, oppdateringer og blokkeringer. Append-only historikklogg bevares separat.