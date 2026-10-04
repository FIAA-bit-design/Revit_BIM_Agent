# Regel 7: Kjør mengdeberegning for føringsveier

Ved relevant RIE-kontroll før leveranse skal den vedlikeholdte C#-implementasjonen brukes:

- C#-implementasjon: `../../CSharpRules/Rule07QuantityForingsways.cs`
Regelen gjelder kun elementinstanser med nøyaktig én instansparameter `FOB_Entreprise` av lagringstype String, der tekstverdien er eksakt `K5B` (ordinal sammenligning, uten trimming). Endre aldri `FOB_Mengde` for andre entrepriseverdier; ignorer dem stille uten telling eller loggføring. Manglende eller blank `FOB_Entreprise`, dupliserte parametere og feil lagringstype skal utelates, telles og logges som uavklart med ElementId og årsak.

Bekreft at aktivt Revit-dokument er riktig leveransemodell før kjøring. C#-regelen endrer `FOB_Mengde` bare for K5B og lager ikke Excel-rapport. Worksharing-dialogen håndteres etter [worksharing-reglene](../standards/worksharing-rules.md). Loggen skal inneholde antall oppdaterte, uendrede og uavklarte elementer, og én detaljrad per oppdatert element med `FOB_Leveransepakke`, `PGF_RIE_ElementId`, `FOB_Merkestreng`, `FOB_Mengdelistepost`, gammel og ny `FOB_Mengde`, Revit-ElementId og kategori. Rapporter resultatet, eventuelle feil og loggbanen.
