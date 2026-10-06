# Regel 9: Synkroniser brannalarmens merke- og sekvensparametere

Kontroller bare instanser i familiekategorien `Fire Alarm Devices` (`OST_FireAlarmDevices`). Alle parameterne er instansparametere; ikke slå opp eller bruk typeparametere.

- C#-implementasjon: `../../CSharpRules/Rule09SyncAlarmSystemMark.cs`

Synkroniser disse parameterparene uavhengig av hverandre:

- `FOB_FysiskMerke` → `PGF_RIE_Alarmsystemer`
- `PGF_RIE_Sekvensnummer` → `FOB_Sekvensnummer`

Sammenlign som tekstverdier eksakt, uten trimming eller endring av store/små bokstaver. Hvis verdiene er ulike, overskriv målparameteren med kildeverdien. For `FOB_FysiskMerke` synkroniseres også en tom kildeverdi som tom tekst. `PGF_RIE_Sekvensnummer` skal derimot ha en verdi: tom tekst, bare blanktegn eller `--` rapporteres som `FEIL` med element-ID, og `FOB_Sekvensnummer` skal ikke endres for dette elementet. Ikke endre kildeparameterne. Feil eller manglende parametere for ett par skal ikke hindre kontroll av det andre. Rapporter elementer der en parameter mangler, har duplikatnavn, feil lagringstype eller målparameteren er skrivebeskyttet.
