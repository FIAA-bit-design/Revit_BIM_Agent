# Mengdelistepost og connector-baserte fittings

## Generell regel

`FOB_Mengdelistepost` skal ikke være blank eller `--`. For øvrige elementer kopieres bare én entydig gyldig instansverdi fra samme `PGF_Mengdetype` og `PGF_Spesialbeskrivelse`; blank og `--` spesialbeskrivelse matcher hverandre. Bevar utfylte målverdier.

## K5B-kabelbrogrupper i Regel 7

For `OST_CableTray` og `OST_CableTrayFitting` kan Regel 7 fylle blanke eller `--`-verdier fra samme fysiske, connector-tilkoblede K5B-komponent. Krev minst to eksisterende gyldige instansverdier i komponenten, og krev at alle gyldige verdier er identiske. Bruk bare gjensidig tilkoblede `ConnectorType.End`-forbindelser; ikke bruk nærhet eller familie-/typeverdier. Bevar gyldige målverdier. Ved færre enn to gyldige kilder, motstridende verdier, manglende connectorer eller ugyldig målparameter skal verdien stå uendret og avviket logges med ElementId og kilde-ID-er. Denne komponentregelen gjelder ikke trekkerør.

## Connector-baserte fittings

Alle typer bend i kabelbro/kabelstige og trekkerør kan være korrekt modellert med en åpen ende. Det er ikke krav om at begge ender er tilkoblet. Identifiser bend med fittingkategori og familiens innebygde `FAMILY_CONTENT_PART_TYPE`: `Elbow`, `ChannelCableTrayElbow`, `ChannelCableTrayVerticalElbow`, `LadderCableTrayElbow` og `LadderCableTrayVerticalElbow`. Dette er deltypeklassifisering, ikke typeparameter-fallback for prosjektets parameterverdier.

Den samme connector-regelen gjelder alle T-kryss/T-stykker, kryss og muffer/skjøter i kabelbro og kabelstige, avgrenset til `OST_CableTrayFitting`. Bruk deltypene `Tee`, `Cross`, `Union`, `ChannelCableTrayTee`, `ChannelCableTrayCross`, `ChannelCableTrayUnion`, `LadderCableTrayTee`, `LadderCableTrayCross` og `LadderCableTrayUnion`. Ikke klassifiser ut fra familienavn alene. Utvidelsen gjelder ikke T-kryss, kryss eller muffer i trekkerør, og heller ikke overganger, forskyvninger eller multiport-fittings.

Når instansparameteren `FOB_Mengdelistepost` er blank eller `--`, kopier fra direkte connector-tilkoblet rett føringsvei av samme kategori (kabelbro til kabelbrofitting, trekkerør til trekkerørbend). Les fittingens `MEPModel.ConnectorManager`, bruk `ConnectorType.End` og bekreft forbindelsen med `IsConnectedTo` i begge retninger. Kildens plassering må være `LocationCurve` med `Line`; bruk bare instansparameteren på rettstrekket. Ikke bruk avstand, familienavn, lik mengdetype eller generisk match som reserve for disse fittingene.

Finn eksisterende fysisk tilkoblede ender og bruk de tilhørende rettstrekkene som kilde. Minst én tilkoblet kilde med én entydig gyldig `FOB_Mengdelistepost` er tilstrekkelig; andre åpne ender skal ikke hindre utfylling. Hvis flere ender har tilkoblede rettstrekk, skal de gyldige kildeverdiene være entydige før kopiering. Flertall er ikke tilstrekkelig, heller ikke for T-kryss og kryss. Manglende eller ugyldig målparameter, ingen tilkoblet rettkilde, manglende kildeverdi, ulike kildeverdier eller connector-/parameterfeil rapporteres som konkrete datablokkeringer uten gjetting. Ingen tilkoblet kilde skal ikke automatisk tolkes som feil modellering. Bevar eksisterende gyldige målverdier, og ikke opprett nye forbindelser bare for å skaffe en kilde.

Logg kilde-ID-er og verdier, konflikter og manglende brukbare kildeverdier i egen append-only UTF-8 kontrollogg. Åpne fittingender er ikke avvik og skal ikke telles som feil i logg eller Excel-rapport. Rapporter fortsatt manglende `PGF_Mengdetype`, men la ikke manglende PGF-søkefelt hindre et ellers entydig connector-oppslag for disse fittingene. Senterlinjefilteret gjelder fortsatt. Ved kjøring knyttet til `Logg av endringer` utelates andre entrepriser enn K5B både som mål og kilder, slik kontrollens etablerte filter angir.

Se også [connectorstandardene](../standards/connector-standards.md) for krav til eventuell connectorretting. Åpne ender gir ikke i seg selv fullmakt til å koble.
