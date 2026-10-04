# Regel 4: Koble brannalarmens child-familie til parent-familien kule

Familien `Brannalarm tilkoblingspunkt magnethold glideskinne` skal brukes som child. Bruk den lastede Revit-lenken med filnavnet `U_F_BAS_FBU_RIS_XXX.rvt` som kildemodell. Match eksakt filnavn fra lenkenavnet eller den lenkede dokumenttittelen, uten hensyn til store/små bokstaver; `.rvt`-endelsen og et Revit-instanssuffiks med ett mellomrom på hver side av kolon og et tall (for eksempel `: <tall>`) kan normaliseres bort. Ikke søk etter den generiske delstrengen `RIS_BAS`. Hvis lenken ikke finnes/er lastet, eller flere lenkeinstanser matcher, stopp uten modellendringer og rapporter blokkeringen.

- C#-implementasjon: `../../CSharpRules/Rule04CopyAlarmParentData.cs`
Bruk malens etablerte parameterkobling:

- Bruk `FOB_ID` på child som treffnøkkel mot parent-parameteren `FOB_Merkestreng`.
- Kopier `FOB_Merkestreng` fra parent til `PGF_RIE_BetjenerMerke` på child.
- Kopier `PGF_Mengdetype` fra parent til `PGF_RIE_BetjenerNavn` på child.

`FOB_ID` er treffnøkkel, ikke en av verdiene som kopieres. Håndter manglende treff og avstandsmatching etter den etablerte logikken. Ikke overskriv eksisterende måldata uten at oppgaven uttrykkelig ber om det. Bruk instansparametere; ikke skriv `FOB_ID` eller bruk typeparameter-fallback.
