# Regel 11: Kontroller og rett workset etter kategori

Kjør etter regel 1–10 og før revisjonskontrollen i Regel 12 og den skrivebeskyttede sluttrapporten i Regel 13.

- C#-implementasjon: `../../CSharpRules/Rule11CheckWorksetCategory.cs`
- Arbeidsbok: `../../config/Workset liste.xlsx`
- Append-only logg: `../../logs/history/Rule11_WorksetCategory.log`
- CSV-rapport: `../../logs/csv/`

For workshared-modeller leses kategori-/workset-mappingene fra arbeidsboken. Kontroller og flytt bare elementer i de definerte kategoriene til forventet workset. Ukjente kategorier, manglende worksets, motstridende mappinger og elementer som ikke kan flyttes rapporteres som blokkeringer. Ved Revit-dialogen om et stort antall worksets velges `Check Out Worksets` i samsvar med [utføringsreglene](../core/execution-rules.md).

Kontrollen endrer workset-tilordninger i modellen. Rapporter antall kategorier og elementer kontrollert, flyttinger, uløste avvik, blokkeringer og fullstendige rapport-/loggbaner. Bevar eldre rapporter og logger.