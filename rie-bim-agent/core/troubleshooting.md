# Feilsøking og blokkeringer

- Ved manglende Revit-host: følg retry-regelen i [preflight](preflight.md). Ikke start kjøring før aktivt dokument og tilgjengelige verktøy er bekreftet.
- Hvis en regel mangler vedlikeholdt C#-implementasjon, rapporter hvilken regel som må implementeres før den kan kjøres.
- Ved `invalid_source` eller `CompileFailed` før `Execute`: rett innsendingen til full C#-kildekode og prøv samme regel på nytt. Dette er ikke en BIM-regelblokkering og innebærer ikke modellendring.
- Ved runtime-feil etter `Execute`-start: kontroller transaksjonsstatus og modelltilstand før retry. `Pending` bekrefter verken commit eller rollback.
- Ved `RegenerationFailedException`: avbryt hele forsøket, rull tilbake og ikke fortsett dokumentlesing eller delforsøk.
- Ikke gjett ved tvetydige data, manglende godkjente revisjonsverdier eller flere motstridende kilder. Rapporter konkret blokkering og berørte ID-er.
- Begrens retry til maksimalt tre målrettede forsøk per konkret feil. Før hvert forsøk må gjeldende modelltilstand og retry-sikkerhet verifiseres.

Se [utføringsreglene](execution-rules.md), [revisjonsreglene](../parameter-rules/revision-rules.md) og [connectorreglene](../standards/connector-rules.md) for de fullstendige betingelsene.
