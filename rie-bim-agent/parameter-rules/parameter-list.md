# Parameterliste

Dette er gjeldende parameterliste for regel 3. `*(tom celle)*` betyr at ingen særskilt fagregel er angitt for parameteren.

| Parameter | Fagregel / instruksjon |
| --- | --- |
| `FOB_ENTstatus` | *(tom celle)* |
| `FOB_Eksistensstatus` | Settes alltid til Ny |
| `FOB_Entreprise` | Settes alltid til K5B |
| `FOB_FysiskMerke` | *(tom celle)* |
| `FOB_ID` | *(tom celle)* |
| `FOB_Leveransepakke` | Fylles etter regel 2 når én pakke har strengt flertall blant de nærmeste naboene; skal ikke være tom. |
| `FOB_Mengde` | For allt unntatt kabelbro og trekkerør strekk settes verdi 1 |
| `FOB_Mengdeenhet` | For føringsveier rette strekk brukes m For alt annet stk også fittjngs |
| `FOB_Mengdelistepost` | Skal ikke være blank eller `--`. Følg [reglene for mengdelistepost](quantity-post-rules.md). |
| `FOB_Merkestreng` | *(tom celle)* |
| `FOB_Merknad` | *(tom celle)* |
| `FOB_Omraade` | *(tom celle)* |
| `FOB_Produksjonsenhet` | *(tom celle)* |
| `FOB_Revisjonsdato` | Følg [revisjonsreglene](revision-rules.md). |
| `FOB_Sekevensnummer` | *(tom celle)* |
| `FOB_Status` | Settes alltid til S4 |
| `FOB_TilhorerObjekt` | *(tom celle)* |
| `FOB_Funksjonskode` | Dette er en type parameter sjekk at verdi verdi KAF er brukt for cable tray og cable tray fittings. For alle conduits og conduits fittings brukes TRR |
| `FOB_Merkesystem` | Dette er en type parameter sjekk at verdi verdi SPV er brukt for cable tray og cable tray fittings og for alle conduits og conduits fittings |
| `FOB_System` | Dette er en type parameter sjekk at verdi verdi 460 er brukt for cable tray og cable tray fittings og for alle conduits og conduits fittings |
| `PGF_RIE_Alarmsystemer` | For Fire Alarm Devices synkroniseres instansparameteren fra `FOB_FysiskMerke` etter regel 9 når verdiene er ulike. |
| `PGF_RIE_BetjenerMerke` | *(tom celle)* |
| `PGF_RIE_BetjenerNavn` | *(tom celle)* |
| `PGF_RIE_Bygg_VVSsystemer` | *(tom celle)* |
| `PGF_RIE_ElementId` | *(tom celle)* |
| `PGF_RIE_Elkraftsystemer` | *(tom celle)* |
| `PGF_RIE_Fra` | *(tom celle)* |
| `PGF_RIE_Føringsvei` | *(tom celle)* |
| `PGF_RIE_IKTsystemer` | *(tom celle)* |
| `PGF_RIE_Kommentar` | *(tom celle)* |
| `PGF_RIE_Sekvensnummer` | *(tom celle)* |
| `PGF_RIE_Til` | *(tom celle)* |
| `PGF_RIE_Tilhører` | *(tom celle)* |
| `PGF_Mengdetype` | For rette føringsveier brukes typeparameteren `Description` sammen med parameteren `Size`. Den sammensatte teksten kopieres også til tilhørende fittings. For elementer som ikke er føringsveier eller tilhørende fittings, brukes kun typeparameteren `Description`. |
| `PGF_PNS` | *(tom celle)* |
| `PGF_Revisjonsign` | Følg [revisjonsreglene](revision-rules.md). |
| `PGF_Revisjonsindeks` | Følg [revisjonsreglene](revision-rules.md). |
| `PGF_Spesialbeskrivelse` | *(tom celle)* |
| `PGF_Statussign` | Settes til dagens dato med format som eksempel 2026.01.10 |

Parameternavnet skal være `FOB_Entreprise` uten avsluttende mellomrom.

## Generelle regler for parameterutfylling

Parameternavnene i første kolonne er kontrollens parameterliste. Fagreglene i den andre kolonnen er styrende for hva som skal fylles ut og hvordan.

Standardregelen er å fylle tomme parametere og bevare eksisterende verdier. Revisjonsfeltene er unntak: kontroller dem også når de allerede har verdier, og fyll eller korriger dem etter [revisjonsreglene](revision-rules.md).

Vurder om parameteren er tom på riktig nivå, for eksempel instans eller type. Ikke finn på en alternativ parameterliste eller fagregler ved gjetting. En gjennomført kjøring betyr ikke i seg selv at alle tomme parametere er håndtert. Hvis en instruksjon ikke blir fulgt, rapporter konkret hva som gjenstår. Hvis en instruksjon er tom, tvetydig eller ikke kan følges, rapporter dette før kontrollen markeres som ferdig.
