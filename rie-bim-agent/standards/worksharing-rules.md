# Worksharing-regler

Alle muterende C#-regler (1–11) skal håndtere den spesifikke Revit-TaskDialog-en som sier at mange worksets/elementer skal sjekkes ut. Bruk `UIApplication.DialogBoxShowing` og velg `Check Out Worksets` med `TaskDialogResult.CommandLink1` bare når en TaskDialog-melding inneholder teksten `trying to check out a large number of elements` etter normalisering. Normaliseringen er skiftleiefri og fjerner tegnsetting og mellomrom, slik at både `checkout` og `check out` gjenkjennes.

Logg et avgrenset utdrag av dialogmeldingen og om Revit godtok eller avviste valget. Hvis en TaskDialog nevner både checkout og workset, men ikke matcher den forventede storutcheckingsmeldingen, logg den som uavklart diagnostikk og ikke overstyr den. Ikke svar automatisk på andre Revit-dialoger.

Dialoghandleren skal kobles til umiddelbart før den aktuelle transaksjonen og alltid kobles fra i `finally`, også ved feil eller tidlig retur. Regelkjøringen skal rapportere om automatisk valg ble akseptert, avvist eller feilet.

Ved connectorretting skal elementer som er eid av andre brukere ikke kobles. Bruk transaksjons- og rollbackkravene i [utføringsreglene](../core/execution-rules.md).
