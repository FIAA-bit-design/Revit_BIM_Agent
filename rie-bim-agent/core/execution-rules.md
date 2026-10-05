# Kanonisk utføring

Denne BIM-kontrollagenten er den eneste arbeidsflyten for reglene. Utfør Revit-lesing og modellendringer med C# via Revit-host eller en vedlikeholdt C#-implementasjon.

Hvis en regel ikke har en tilsvarende C#-implementasjon, rapporter at regelen må implementeres i C# før den kan kjøres gjennom agenten. Ikke rapporter en regel som utført uten at den er gjennomført via den godkjente arbeidsflyten.

## Selvstendig feilsøking, retting og etterkontroll

Når brukeren har gitt et modellkontroll- eller retteoppdrag, kan entydige feil rettes innenfor bekreftet omfang og dokumenterte fagregler uten ny bekreftelse for hvert delsteg. Et oppdrag om kodeendring eller kompilering alene gir ikke tillatelse til generell modellretting.

1. Bekreft Revit-host og riktig dokument, og fastslå gjeldende entreprise-, leveransepakke-, kategori- og worksetavgrensning. Hold både mål og datakilder innenfor avtalte avgrensninger. Andre entrepriser i et K5B-avgrenset oppdrag ignoreres stille og endres ikke. Senterlinjer utelates.
2. Undersøk årsaken i faktiske modelldata, ikke bare antall avvik. Les instansparametere, connectorer og relevante plasseringer. Klassifiser hva som kan rettes entydig, hva som er en teknisk feil, og hva som trenger faglig avklaring. Bruk FIAA til en separat, lesende kvalitetssjekk ved ikke-trivielle kodeendringer eller modellrettinger. Hovedagenten er ansvarlig for å verifisere konklusjonene.
3. Rett entydige feil. Bevar gyldige eksisterende verdier med mindre en uttrykkelig fagregel krever korrigering. Ikke velg en verdi fordi den er vanligst når fagregelen krever et entydig treff. Fyll datakilder før avhengige elementer, for eksempel rettstrekk før bend, og les verdiene på nytt etter bekreftet commit.
4. Ved endret skriptlogikk eller en ny muterende C#-handling skal en midlertidig C#-byggkontroll bestå `dotnet build` med 0 feil før modellkjøring. En kompileringstest er ikke en modelltest. Kontroller host og dokument igjen før skriving, og lås handlingen til undersøkt dokument og element-/connectorutvalg.
5. Bruk transaksjoner og isolerte delforsøk. Bekreft commit og rollback med faktisk transaksjonsstatus; `Pending` er ikke en bekreftelse på noen av delene. Ved connectorretting skal en ytre `TransactionGroup` kunne tilbakeføre også avvik som oppdages etter hovedcommit. Ved `RegenerationFailedException` skal hele forsøket avbrytes og tilbakeføres; ikke fortsett dokumentlesing eller nye delforsøk etter denne fatale feilen.
6. Gjør en separat, lesende etterkontroll av resultatet i modellen etter hver retting eller avgrenset batch. Kontroller faktisk effekt og at eksisterende forbindelser, øvrige elementer og avtalte avgrensninger er bevart. Ved feil: undersøk rotårsaken, reparer samme avgrensede logikk og prøv igjen bare når gjeldende modelltilstand er verifisert og retry er trygg. Bruk maksimalt tre målrettede forsøk per konkret feil.
7. Oppdater avviksrapporten fra etterkontrollen, ikke fra gamle snapshots. Skill mellom rettet og verifisert, tilbakeført, teknisk blokkert og faglig uavklart. Ta med ElementId, leveransepakke, årsak og eventuelle kilde-ID-er/verdier. Se [rapporteringsreglene](reporting.md).
8. Avslutt med en kort oppsummering av det som faktisk er rettet, verifisert og fortsatt uavklart. Brukeren skal bare trenge å vurdere reelle faglige valg, tvetydige data eller tiltak utenfor fullmakten. Ikke lagre eller synkroniser modellen automatisk uten særskilt tillatelse. Rydd agentens egne midlertidige test-/eksportfiler.

## Worksharing for muterende C#-regler

Dette er et felles utføringskrav for muterende C#-regler 1–12, inkludert regel 3 og revisjonskontrollen i regel 12; det er ikke et eget kjøringstrinn.

Reglene skal håndtere den spesifikke Revit-TaskDialog-en som sier at mange worksets/elementer skal sjekkes ut. Bruk `UIApplication.DialogBoxShowing` og velg `Check Out Worksets` med `TaskDialogResult.CommandLink1` bare når meldingen inneholder teksten `trying to check out a large number of elements` etter normalisering. Normaliseringen er skiftleiefri og fjerner tegnsetting og mellomrom, slik at både `checkout` og `check out` gjenkjennes.

Logg et avgrenset utdrag av dialogmeldingen og om Revit godtok eller avviste valget. Hvis en TaskDialog nevner både checkout og workset, men ikke matcher den forventede storutcheckingsmeldingen, logg den som uavklart diagnostikk og ikke overstyr den. Ikke svar automatisk på andre Revit-dialoger.

Koble dialoghandleren til umiddelbart før den aktuelle transaksjonen, og koble den alltid fra i `finally`, også ved feil eller tidlig retur. Regelkjøringen skal rapportere om automatisk valg ble akseptert, avvist eller feilet.

Ved connectorretting skal elementer som eies av andre brukere ikke kobles. Følg også [connectorstandarden](../standards/connector-standards.md) og transaksjons-/rollbackkravene ovenfor.

## Kjøring av C#-implementasjoner

`run-csharp-script` mottar C#-kildekode, ikke en filbane. Når en vedlikeholdt `.cs`-regel skal kjøres, les og send hele kildefilen som `request`, inkludert `using`-direktiver, navnerom, `GeneratedAction` og forventet `Execute(UIApplication, Document?)`-inngangspunkt. Ikke send bare filbanen, en `#load`-direktiv eller en wrapper som utelater regelkoden.

Hvis hosten uttrykkelig avviser kildeformatet eller melder `invalid_source`/`CompileFailed` før `Execute` starter, klassifiser dette som en feil i agentens kallform eller kildeinnsending, ikke som en feil eller blokkering i BIM-regelen. Ingen regelkjøring eller modellendring har da skjedd. Rett kallformen ved å sende komplett kildekode, og prøv samme regel på nytt. Ikke stopp eller gå videre til neste regel før dette forsøket er gjort. Hvis komplett kildekode fortsatt ikke kompilerer, fang hele diagnostikken, inspiser og rett en entydig kildefeil før ny innsending.

Skill alltid mellom avvisning før kjøring, kompileringsfeil og runtime-feil etter at `Execute` har startet. Bare runtime-feil kan ha rukket å endre modellen; vurder transaksjonsresultat og gjeldende modelltilstand før retry.
