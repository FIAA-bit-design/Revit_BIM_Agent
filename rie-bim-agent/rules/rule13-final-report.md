# Regel 13: Lag felles sluttrapport

Kjør denne regelen sist, etter at relevante regler 1–12 er ferdig kjørt og eventuelle modellendringer er utført. Worksharing-kravene i utføringsreglene følges under muterende regelkjøringer.

- C#-implementasjon: `../../CSharpRules/Rule13FinalReport.cs` (skrivebeskyttet).

`Rule11CheckWorksetCategory.cs` er en muterende workset-kontroll som kjøres som Regel 11, men rapporten her forblir skrivebeskyttet.

Skriptet lager en skrivebeskyttet sluttsnapshot av aktiv modell. Loggen inneholder én rad per instans der minst én av parameterne finnes, med kolonnene `FOB_Leveransepakke`, `PGF_Mengdetype`, `FOB_Merkestreng` og `ElementId`. Blanke felt og `--`-plassholdere telles som manglende bare når parameteren finnes på instansen. Hvis parameterobjektet ikke finnes, telles dette separat og regnes ikke som en manglende verdi. Radene viser den faktiske verdien fra modellen. Snapshoten får et tidsstemplet navn og lagres i `logs/csv/`.

Rapporter antall rader, manglende verdier per felt, antall instanser der parameteren ikke finnes, og den fullstendige loggbanen. Sluttrapporten dokumenterer modellens tilstand etter kontrollene, men erstatter ikke utføringen eller resultatrapporteringen fra reglene 1–12.
