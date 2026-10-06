# Regel 9: Synkroniser brannalarmens merke- og sekvensparametere

Kontroller bare instanser i familiekategorien `Fire Alarm Devices` (`OST_FireAlarmDevices`). Alle parameterne er instansparametere; ikke slå opp eller bruk typeparametere.

- C#-implementasjon: `../../CSharpRules/Rule09SyncAlarmSystemMark.cs`

Synkroniser disse parameterparene uavhengig av hverandre:

- `FOB_FysiskMerke` → `PGF_RIE_Alarmsystemer`
- `PGF_RIE_Sekvensnummer` → `FOB_Sekvensnummer`

Sammenlign som tekstverdier eksakt, uten trimming eller endring av store/små bokstaver. Hvis verdiene er ulike, overskriv målparameteren med kildeverdien. For `FOB_FysiskMerke` synkroniseres også en tom kildeverdi som tom tekst. For `Fire Alarm Devices` der familienavnet begynner med `Slokkeanlegg` eller `Inertgass` (uten hensyn til store/små bokstaver), er `--` en gyldig og påkrevd verdi i `FOB_Merkestreng`, `PGF_RIE_Sekvensnummer` og `FOB_Sekvensnummer`; overskriv andre verdier i disse tre parameterne med `--`. For øvrige familienavn rapporteres tom tekst, bare blanktegn eller `--` i `PGF_RIE_Sekvensnummer` som feil, og `FOB_Sekvensnummer` endres ikke for dette elementet. Feil eller manglende parametere for ett par skal ikke hindre kontroll av det andre.

Opprett en tidsstemplet Excel-avviksrapport i `logs/reports/` med én rad per element som har en uløst feil. Kolonnene er `ElementId`, `PGF_Mengdetype`, `FOB_Merkestreng` og `Feil`. Ikke ta med elementer der synkroniseringen ble utført uten feil. Den append-only historikkloggen skal bare inneholde sammendrag, transaksjons-/rapportfeil og rapportbane, ikke en rad per vellykket synkronisering.
