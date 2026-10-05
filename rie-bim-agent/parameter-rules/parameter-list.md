# Parameterliste og generelle utfyllingsregler

Den autoritative parameterlisten for regel 3 ligger i [Parameterliste.xlsx](../../config/Parameterliste.xlsx), i fanen `Parameterliste`. Arbeidsboken inneholder kolonnene `Parameternavn`, `Fagregel / forutsetning` og `Regelreferanse`. Endre parameterlisten og de redigerbare forutsetningstekstene i arbeidsboken, ikke ved å opprette en ny tabell her.

Teksten `Ingen særskilt fagregel angitt` betyr at Excel-raden ikke angir en egen fagregel. Det betyr ikke at parameteren skal ignoreres; de generelle reglene nedenfor gjelder fortsatt.

Rule 3s C#-implementasjon leser `Parameternavn` fra arbeidsboken. Fagregeltekstene er den menneskelesbare kilden for regelens forutsetninger, men naturlig språk tolkes ikke automatisk som kjørbar logikk. Endringer i faktisk regelatferd må også implementeres og byggkontrolleres i riktig C#-regel.

Parameternavnet skal være `FOB_Entreprise` uten avsluttende mellomrom.

## Generelle regler for parameterutfylling

Parameternavnene i Excel-kolonnen `Parameternavn` er kontrollens parameterliste. Fagreglene i kolonnen `Fagregel / forutsetning` er styrende for hva som skal fylles ut og hvordan.

Standardregelen er å fylle tomme parametere og bevare eksisterende verdier. Revisjonsfeltene er unntak: [regel 12](../rules/regel12-kontroller-revisjonsparametere-per-leveransepakke.md) kontrollerer dem også når de allerede har verdier, og fyller eller korrigerer dem etter [revisjonsreglene](revision-rules.md).

Vurder om parameteren er tom på riktig nivå, for eksempel instans eller type. Ikke finn på en alternativ parameterliste eller fagregler ved gjetting. En gjennomført kjøring betyr ikke i seg selv at alle tomme parametere er håndtert. Hvis en instruksjon ikke blir fulgt, rapporter konkret hva som gjenstår. Hvis en instruksjon er tom, tvetydig eller ikke kan følges, rapporter dette før kontrollen markeres som ferdig.
