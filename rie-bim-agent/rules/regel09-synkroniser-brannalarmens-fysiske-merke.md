# Regel 9: Synkroniser brannalarmens fysiske merke

Kontroller alle instanser i familiekategorien `Fire Alarm Devices` (`OST_FireAlarmDevices`). Begge parameterne er instansparametere; ikke slå opp eller bruk typeparametere.

- C#-implementasjon: `../../CSharpRules/Rule09SyncAlarmSystemMark.cs`

Sammenlign `FOB_FysiskMerke` og `PGF_RIE_Alarmsystemer` som tekstverdier. Hvis verdiene er ulike, overskriv `PGF_RIE_Alarmsystemer` med verdien fra `FOB_FysiskMerke`. Sammenlign eksakt, uten trimming eller endring av store/små bokstaver. En tom kildeverdi synkroniseres som tom tekst. Ikke endre `FOB_FysiskMerke`. Rapporter elementer der en parameter mangler, har duplikatnavn, feil lagringstype eller målparameteren er skrivebeskyttet.
