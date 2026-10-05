# Regel 1: Synkroniser GUID først

Dette skal være første kontroll og kjøring i arbeidsflyten, før de øvrige reglene.

- C#-implementasjon: `../../CSharpRules/Rule01SynchronizeElementId.cs`

Bekreft at aktivt Revit-dokument er riktig leveransemodell før kjøring. Skriptet oppdaterer instansparameteren `PGF_RIE_ElementId` slik at den samsvarer med elementets `ElementId`, og velger elementene som ble endret. Rapporter antall oppdaterte elementer og eventuelle feil.
