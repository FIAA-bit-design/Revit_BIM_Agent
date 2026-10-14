---
description: "Bruk ved BIM-kontroll av RIE-Revit-modeller før leveranse, særlig når parametere, GUID-er, parameterlister eller leveranseskript skal kontrolleres."
name: "RIE BIM-kontroll før leveranse"
---
# RIE BIM-kontroll før leveranse

Denne filen er inngangen til den modulære RIE BIM-agentinstruksjonen. Ved full kontroll før leveranse leses alle filer i `core/`, `rules/`, `parameter-rules/` og `standards/`, inkludert delregelrekken 1–11 og alle tilhørende fagregler. Regel 12 er utgått; revisjonsparametere kontrolleres av Regel 3. For avgrensede oppgaver leses alle filer i `core/`, filene for reglene som velges etter arbeidsflyten nedenfor, og filene i `parameter-rules/` og `standards/` som disse reglene henviser til eller som kreves av arbeidsflyten nedenfor. Hvis en påkrevd fil mangler eller ikke kan leses, stopp kontrollen, rapporter hvilken fil som mangler, og ikke anta innholdet.

## Filhistorikk

GitHub-committhistorikken er eneste kilde for revisjonshistorikk av prosjektfiler. Ikke opprett eller vedlikehold parallelle lokale filhistorikker eller versjonsnotater. Eksisterende Revit-kjørelogger og kontrollrapporter følger fortsatt sine fagregler; ikke slett dem som følge av denne regelen.

## Obligatorisk arbeidsflyt

- Følg [preflight](core/preflight.md) før enhver Revit-relatert kontroll eller regel.
- Følg [utføringsreglene](core/execution-rules.md), [feilsøkingsreglene](core/troubleshooting.md), [rapporteringsreglene](core/reporting.md) og [oppryddingsreglene](core/cleanup.md).
- Kontroller [senterlinjefilteret](standards/centerline-filter.md) når modellen inneholder føringsveier, og [connectorstandarden](standards/connector-standards.md) når modellen inneholder MEK-koblingspunkter. Følg worksharing-kravene i [utføringsreglene](core/execution-rules.md) for muterende regler.
- Parameterarbeid bruker [Parameterliste.xlsx](../config/Parameterliste.xlsx) som autoritativ parameterliste og følger [de generelle utfyllingsreglene](parameter-rules/parameter-list.md), [revisjonsreglene](parameter-rules/revision-rules.md), [mengdelistepostreglene](parameter-rules/quantity-post-rules.md) og [brukerfiltrene](parameter-rules/user-filters.md).
- Regeloversikt: [regel 1](rules/regel01-synkroniser-guid.md), [regel 2](rules/regel02-fyll-manglende-leveransepakke.md), [regel 3](rules/regel03-parameterkontroll.md), [regel 4](rules/regel04-brannalarm-fob-merkestreng-kopiering.md), [regel 5](rules/regel05-lastenavn-til-datakurser.md), [regel 6](rules/regel06-vask-koblingspunkter-mek.md), [regel 7](rules/regel07-mengdeberegning-for-foringsveier.md), [regel 8](rules/regel08-kontroller-dupliserte-infonoder.md), [regel 9](rules/regel09-synkroniser-brannalarmens-fysiske-merke.md), [regel 10](rules/regel10-fyll-magicad-systemverdier-fra-tilkoblet-rett-foringsvei.md) og [regel 11](rules/regel11-kontroller-rett-workset-etter-kategori.md).
- Ved full kontroll før leveranse utfør reglene i denne avhengighetsrekkefølgen:
  1. Regel 1.
  2. Regel 3.
  3. Regel 2.
  4. Regel 4–11 i nummerrekkefølge.

  **Merknad:** Regel 3 rapporterer generell parameterkompletthet, inkludert en lesende kontroll av `FOB_Revisjonsdato`, `PGF_Revisjonsign` og `PGF_Revisjonsindeks`. Revisjonsverdier skal være utfylt; tom tekst, bare whitespace og `--` er ikke gyldig. Kontroll av revisjonsparametere bruker ikke Excel-revisjonslister eller connector-kilder og endrer ingen revisjonsverdier. Den gjelder hele Regel 3-utvalget uavhengig av `FOB_Leveransepakke`. Regel 2 kjøres etter Regel 3 slik at elementer som da får `FOB_Entreprise=K5B` også kan få `FOB_Leveransepakke`; nye pakker krever ikke en separat revisjonskontroll.

- For avgrensede oppgaver knytt brukerens spesifiserte parametere eller temaer til reglene ut fra titlene og de beskrevne parameterne og virkeområdene i de lenkede regelfilene. Velg reglene som uttrykkelig dekker oppgaven, og inkluder også alle forutsetningsregler som disse regelfilene krever, inkludert deres forutsetninger. Ved valg av Regel 2 inkluderes Regel 3 som beskrevet ovenfor. Kjør det valgte delsettet i samme relative rekkefølge som ved full kontroll; tidligere plassering i rekkefølgen betyr ikke alene at en regel er en forutsetning. Hvis brukerens spesifiserte parametere eller temaer ikke entydig kan knyttes til én eller flere regler, eller forutsetningene ikke kan forenes med denne rekkefølgen, be om avklaring før du kjører noen regel i stedet for å gjette. Følg de tverrgående utføringskravene der de gjelder.

Hver delregel lager en egen tidsstemplet rapport fra sin kjøring i tillegg til eventuell append-only historikklogg. Regel 3-rapporten inneholder parameterutfylling og generell parameterkompletthet, inkludert revisjonsavvik med ElementId, parameter, faktisk verdi, leveransepakke og årsak. Regel 3 lager en filtrerbar XLSX-liste; Regel 8, 9 og 11 beholder sine egne XLSX-/CSV-rapporter. Historiske Regel 12-rapporter og logger bevares. Det finnes ikke et separat sluttrapporttrinn.

Ikke anta at en regel er utført uten å følge C#-arbeidsflyten og rapportere blokkeringer som beskrevet i kjernereglene. Hvis en regel blokkeres, dokumenter blokkeringen, hopp ikke over nummerrekkefølgen uten å angi det, og angi eksplisitt om resten av kontrollen fortsatte eller ble avbrutt.
