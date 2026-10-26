# Regel 11: Kontroller og rett workset etter kategori

Kjør etter regel 1–10.

- C#-implementasjon: `../../CSharpRules/Rule11CheckWorksetCategory.cs`
- Arbeidsbok: `../../config/Workset liste.xlsx`
- Append-only logg: `../../logs/history/Rule11_WorksetCategory.log`
- CSV-rapport: `../../logs/csv/`

For workshared-modeller leses kategori-/workset-mappingene fra arbeidsboken. Kontroller og flytt bare elementer i de definerte kategoriene til forventet workset. Ukjente kategorier, manglende worksets, motstridende mappinger og elementer som ikke kan flyttes rapporteres som blokkeringer. Ved Revit-dialogen om et stort antall worksets velges `Check Out Worksets` i samsvar med [utføringsreglene](../core/execution-rules.md).

Faner for modellspesifikke unntak i arbeidsboken bruker kolonnene `Modell`, `Kategori`, `Familie` og `Workset`. `Familieunntak` brukes for STA, og `BAS` for BAS. Modellnavnet må samsvare med aktiv Revit-modell, og familien matches eksakt uten hensyn til store/små bokstaver. Unntaket gjelder bare treff for angitt modell, kategori og familie; øvrige elementer bruker kategorimappingen. Motstridende unntak blokkerer regelen.

I BAS-modellen `U_F_BAS_FBU_RIE_XXX` er elementer som allerede ligger på workset `RIE Temp Varelevering` et brukerbekreftet unntak. De skal beholdes der uansett kategori- eller familiemapping, ikke flyttes og ikke telles som kontrollerte elementer eller uløste workset-avvik. Unntatte elementer i regelens kategorier vises separat som `Workset-unntak` i CSV-rapporten og summeres i historikkloggen. Modell- og worksetnavn matches eksakt uten hensyn til store/små bokstaver. Unntaket gjelder bare Regel 11 i BAS, ikke andre modeller eller regler.

Kontrollen endrer workset-tilordninger i modellen. Rapporter antall kategorier og elementer kontrollert, flyttinger, uløste avvik, blokkeringer og fullstendige rapport-/loggbaner. Bevar eldre rapporter og logger.

Den tidsstemplede CSV-rapporten i `../../logs/csv/` er Regel 11s rapport per kjøring; append-only historikklogg ligger separat.
