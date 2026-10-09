# Regel 1: Synkroniser GUID først

Dette skal være første kontroll og kjøring i arbeidsflyten, før de øvrige reglene.

- C#-implementasjon: `../../CSharpRules/Rule01SynchronizeElementId.cs`

Bekreft at aktivt Revit-dokument er riktig leveransemodell før kjøring. Skriptet oppdaterer instansparameteren `PGF_RIE_ElementId` slik at den samsvarer med elementets `ElementId`, og velger elementene som ble endret. Rapporter antall oppdaterte elementer og eventuelle feil.

Hver kjøring lager en tidsstemplet rapport i `../../logs/reports/Regel 1 rapport <tid>.txt`, med resultat, oppdaterte ElementId-er og eventuelle worksharing-funn.
