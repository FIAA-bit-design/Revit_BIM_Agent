---
description: "Bruk ved BIM-kontroll av RIE-Revit-modeller før leveranse, særlig når parametere, GUID-er, parameterlister eller leveranseskript skal kontrolleres."
name: "RIE BIM-kontroll før leveranse"
---
# RIE BIM-kontroll før leveranse

Denne filen er inngangen til den modulære RIE BIM-agentinstruksjonen. Les alltid relevante filer i `core/`, `rules/`, `parameter-rules/` og `standards/` før du utfører oppgaven. Ved full kontroll før leveranse leses regelrekken 1–13 og alle tilhørende fagregler.

## Filhistorikk

GitHub-committhistorikken er eneste kilde for revisjonshistorikk av prosjektfiler. Ikke opprett eller vedlikehold parallelle lokale filhistorikker eller versjonsnotater. Eksisterende Revit-kjørelogger og kontrollrapporter følger fortsatt sine fagregler; ikke slett dem som følge av denne regelen.

## Obligatorisk arbeidsflyt

- Følg [preflight](core/preflight.md) før enhver Revit-relatert kontroll eller regel.
- Følg [utføringsreglene](core/execution-rules.md), [feilsøkingsreglene](core/troubleshooting.md), [rapporteringsreglene](core/reporting.md) og [oppryddingsreglene](core/cleanup.md).
- Kontroller [senterlinjefilteret](standards/centerline-filter.md) og [connectorstandarden](standards/connector-standards.md) når de gjelder. Følg worksharing-kravene i [utføringsreglene](core/execution-rules.md) for muterende regler.
- Parameterarbeid bruker [Parameterliste.xlsx](../config/Parameterliste.xlsx) som autoritativ parameterliste og følger [de generelle utfyllingsreglene](parameter-rules/parameter-list.md), [revisjonsreglene](parameter-rules/revision-rules.md), [mengdelistepostreglene](parameter-rules/quantity-post-rules.md) og [brukerfiltrene](parameter-rules/user-filters.md).
- Utfør relevante regler i nummerrekkefølge: [regel 1](rules/rule01-guid.md), [regel 2](rules/rule02-package.md), [regel 3](rules/rule03-parameters.md), [regel 4](rules/rule04-firealarm.md), [regel 5](rules/rule05-loadname.md), [regel 6](rules/rule06-mek.md), [regel 7](rules/rule07-quantity.md), [regel 8](rules/rule08-infonode.md), [regel 9](rules/rule09-firealarm-mark.md), [regel 10](rules/rule10-magicad-system.md), [regel 11](rules/rule11-workset-category.md), [regel 12: revisjonskontroll](rules/rule12-revision-parameters.md) og til slutt [regel 13: sluttrapport](rules/rule13-final-report.md). Følg de tverrgående utføringskravene der de gjelder.

Ikke anta at en regel er utført uten å følge C#-arbeidsflyten og rapportere blokkeringer som beskrevet i kjernereglene.
