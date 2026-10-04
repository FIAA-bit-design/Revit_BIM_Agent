# Regel 8: Kontroller dupliserte infonoder

Ved relevant RIE-kontroll før leveranse skal du kjøre denne C#-implementasjonen:

- C#-implementasjon: `../../CSharpRules/Rule08CheckInfoNodes.cs`
- Excel-rapport: `../../logs/reports/`
Bekreft at aktivt Revit-dokument er riktig leveransemodell før kjøring. Regelen rapporterer manglende eller dupliserte `InfoNode_hostID`-verdier og konflikter i `InfoNode_subs`, og lager en Excel-rapport. Regelen sletter ikke dupliserte infonoder; den kan flytte Klimaport under Brannport med 50 mm klaring. C#-implementasjonen bruker forutsetningen om at negativ Y-retning er «ned» for klaringsberegningen.

Worksharing-dialogen håndteres etter [worksharing-reglene](../standards/worksharing-rules.md). Rapporter funn, eventuelle flyttinger, feil og hvor rapporten ble lagret.
