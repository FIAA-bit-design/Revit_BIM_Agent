# Worksharing-regler

Ved regel 3 skal C#-implementasjonen automatisk velge `Check Out Worksets` via `UIApplication.DialogBoxShowing` dersom workshared modell viser Revit-TaskDialog-en med teksten om at et stort antall elementer forsøkes sjekket ut. Overstyr bare TaskDialog-er med den spesifikke meldingsteksten; ikke svar automatisk på andre Revit-dialoger.

Samme avgrensede dialoghåndtering gjelder regel 7 og regel 8 når Revit viser den spesifikke meldingen om stort antall elementer som forsøkes sjekket ut. Ikke overstyr andre dialoger.

Ved connectorretting skal elementer som er eid av andre brukere ikke kobles. Bruk transaksjons- og rollbackkravene i [utføringsreglene](../core/execution-rules.md).
