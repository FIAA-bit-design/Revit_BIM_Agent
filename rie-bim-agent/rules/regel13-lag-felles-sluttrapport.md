# Regel 13: Lag felles sluttrapport

Kjør denne regelen sist, etter at relevante regler 1–12 er ferdig kjørt og eventuelle modellendringer er utført. Worksharing-kravene i utføringsreglene følges under muterende regelkjøringer.

- C#-implementasjon: `../../CSharpRules/Rule13FinalReport.cs` (skrivebeskyttet mot Revit-modellen).

`Rule11CheckWorksetCategory.cs` er en muterende workset-kontroll som kjøres som Regel 11, men rapporten her forblir skrivebeskyttet.

Skriptet lager en skrivebeskyttet sluttsnapshot av aktiv modell. CSV-snapshotet inneholder én rad per instans der minst én av parameterne finnes, med kolonnene `FOB_Leveransepakke`, `PGF_Mengdetype`, `FOB_Merkestreng` og `ElementId`. Blanke felt og `--`-plassholdere telles som manglende bare når parameteren finnes på instansen. Hvis parameterobjektet ikke finnes, telles dette separat og regnes ikke som en manglende verdi. Radene viser den faktiske verdien fra modellen. Snapshoten får et tidsstemplet navn og lagres i `logs/csv/`.

I tillegg lager regelen en tidsstemplet K5B-avgrenset Excel-arbeidsbok i `logs/reports/` med arkene `Oversikt K5B`, `Manuell oppfølging` og `Regelversjoner`. En ElementId-rad tas bare med når aktiv modell har nøyaktig én instansparameter `FOB_Entreprise` av lagringstype String med eksakt verdi `K5B`; andre, blanke, manglende, dupliserte og ugyldige enterpriseverdier filtreres bort. Regel 7-funn hentes på nytt fra aktivt K5B-målsett. Kabelbro-/kabelstige-Union, T-stykker og kryss skal ikke vises som manglende lengde: Union utelates, mens T/kryss får samlet nettverkslengde beregnet og loggført uten at denne lengden skrives på fittingene. T/kryss-nettverkslengdene skal ikke vises som et eget Excel-ark. T/kryss-fittingene får `FOB_Mengde=1` og `FOB_Mengdeenhet=stk`, mens rette strekk beholder sin uavhengige lengdeberegning. Ikke-elementspesifikke funn som ikke kan avgrenses til K5B, tas ikke med i oppfølgingsarket. Ikke kopier alle modellradene fra CSV-snapshotet inn i Excel-arbeidsboken. CSV-snapshotet forblir et separat, fullstendig modelluttrekk.

Excel-arbeidsboken skal også ha arket `Regelversjoner` med kildeversjon (`ScriptVersion`), sist kjørt loggversjon, siste loggtid og status for Regel 1–12. Hvis kildeversjonen er nyere enn siste loggversjon, vis `Kilde oppdatert - ny kjøring mangler`; en målrettet delkjøring skal ikke presenteres som en full regelkjøring.

K5B-lengdeoversikten i rapporten følger Rule 7: strømskinne-bend med bend-deltype utelates, og instansene `17760381` og `17760457` bruker utfylt `Conduit Length` som lengdekilde.

Rapporter antall rader, manglende verdier per felt, antall instanser der parameteren ikke finnes, og de fullstendige CSV- og Excel-banene. Excel-rapporten oppsummerer bare kjøringslogger; den erstatter ikke utføringen eller resultatrapporteringen fra reglene 1–12. Aspirasjonselementer filtreres bort fra Regel 7s K5B-lengdeavvik etter familie-, type- eller instansnavn. CSV-snapshotet dokumenterer fortsatt modellens faktiske parameterverdier etter kontrollene.
