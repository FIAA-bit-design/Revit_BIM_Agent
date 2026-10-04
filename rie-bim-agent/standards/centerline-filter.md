# Standardfilter for senterlinjer

`Center line`-elementer (senterlinjer) skal ikke sjekkes. Utelat kategorier der `BuiltInCategory`-navnet slutter på `CenterLine`, blant annet `OST_ConduitCenterLine` og `OST_ConduitFittingCenterLine`. Bruk kategori-ID (`ElementId.Value`) for filtrering, ikke det språkavhengige visningsnavnet.

Dette gjelder alle parameterkontroller, utfylling, kildesøk, avvikstelling og sluttrapporter i agentarbeidsflyten. Filtrer før parameterverdier leses, slik at senterlinjer heller ikke rapporteres som manglende eller feil. Ikke endre eller slett selve senterlinjeelementene. Andre elementkategorier og øvrige filtre skal ikke endres av denne regelen.

Nye Revit-skript fra standardmalen skal bruke `collect_control_elements(doc)` eller samme kategorifilter ved innsamling til kontroller.
