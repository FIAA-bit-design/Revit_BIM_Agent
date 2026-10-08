---
description: "Bruk ved BIM-kontroll av RIE-Revit-modeller før leveranse, særlig når parametere, GUID-er, parameterlister eller leveranseskript skal kontrolleres."
name: "RIE BIM-kontroll før leveranse"
---
# RIE BIM-kontroll før leveranse

Denne filen er inngangen til den modulære RIE BIM-agentinstruksjonen. Les alltid relevante filer i `core/`, `rules/`, `parameter-rules/` og `standards/` før du utfører oppgaven. Ved full kontroll før leveranse leses regelrekken 1–13 og alle tilhørende fagregler. Hvis en påkrevd fil mangler eller ikke kan leses, stopp kontrollen, rapporter hvilken fil som mangler, og ikke anta innholdet.

## Filhistorikk

GitHub-committhistorikken er eneste kilde for revisjonshistorikk av prosjektfiler. Ikke opprett eller vedlikehold parallelle lokale filhistorikker eller versjonsnotater. Eksisterende Revit-kjørelogger og kontrollrapporter følger fortsatt sine fagregler; ikke slett dem som følge av denne regelen.

## Obligatorisk arbeidsflyt

- Følg [preflight](core/preflight.md) før enhver Revit-relatert kontroll eller regel.
- Følg [utføringsreglene](core/execution-rules.md), [feilsøkingsreglene](core/troubleshooting.md), [rapporteringsreglene](core/reporting.md) og [oppryddingsreglene](core/cleanup.md).
- Kontroller [senterlinjefilteret](standards/centerline-filter.md) når modellen inneholder føringsveier, og [connectorstandarden](standards/connector-standards.md) når modellen inneholder MEK-koblingspunkter. Følg worksharing-kravene i [utføringsreglene](core/execution-rules.md) for muterende regler.
- Parameterarbeid bruker [Parameterliste.xlsx](../config/Parameterliste.xlsx) som autoritativ parameterliste og følger [de generelle utfyllingsreglene](parameter-rules/parameter-list.md), [revisjonsreglene](parameter-rules/revision-rules.md), [mengdelistepostreglene](parameter-rules/quantity-post-rules.md) og [brukerfiltrene](parameter-rules/user-filters.md).
- Ved full kontroll før leveranse utfør alle reglene 1–13 i nummerrekkefølge. For avgrensede oppgaver utfør kun reglene som dekker parameterne eller temaene brukeren spesifiserer: [regel 1](rules/regel01-synkroniser-guid.md), [regel 2](rules/regel02-fyll-manglende-leveransepakke.md), [regel 3](rules/regel03-parameterkontroll.md), [regel 4](rules/regel04-koble-brannalarm-underfamilie-til-overordnet-familie.md), [regel 5](rules/regel05-lastenavn-til-datakurser.md), [regel 6](rules/regel06-vask-koblingspunkter-mek.md), [regel 7](rules/regel07-mengdeberegning-for-foringsveier.md), [regel 8](rules/regel08-kontroller-dupliserte-infonoder.md), [regel 9](rules/regel09-synkroniser-brannalarmens-fysiske-merke.md), [regel 10](rules/regel10-fyll-magicad-systemverdier-fra-tilkoblet-rett-foringsvei.md), [regel 11](rules/regel11-kontroller-rett-workset-etter-kategori.md), [regel 12: revisjonskontroll](rules/regel12-kontroller-revisjonsparametere-per-leveransepakke.md) og til slutt [regel 13: sluttrapport](rules/regel13-lag-felles-sluttrapport.md). Følg de tverrgående utføringskravene der de gjelder.

Ikke anta at en regel er utført uten å følge C#-arbeidsflyten og rapportere blokkeringer som beskrevet i kjernereglene. Hvis en regel blokkeres, dokumenter blokkeringen, hopp ikke over nummerrekkefølgen uten å angi det, og angi eksplisitt om resten av kontrollen fortsatte eller ble avbrutt.
