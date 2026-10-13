# Obligatorisk Revit-preflight

Før enhver Revit-relatert regel, skriptkjøring eller modellkontroll skal Revit-tilkoblingen kontrolleres via MCP. Kall `operation://integrations/list-instances` med `integration=Revit` for å hente kjørende instanser, modellnavn og gjeldende tilkobling. List tilgjengelige Revit-verktøy før videre arbeid.

Når brukeren angir en målmodell, velg Revit-instansen med et entydig samsvarende modellnavn. Hvis den ikke allerede er tilkoblet, bruk `operation://integrations/connect` med `instanceId` fra instanslisten. Bekreft deretter aktivt dokument på nytt via Revit-verktøy før noen regler kjøres. Hvis ingen eller flere instanser matcher, stopp og oppgi modellnavnene som ble funnet; ikke åpne en annen modell eller gjett.

Når ingen målmodell er angitt, behold gjeldende Revit-tilkobling. Hvis ingen instans er tilkoblet, koble til automatisk bare når nøyaktig én Revit-instans kjører. Ved flere mulige instanser må riktig modell avklares før videre arbeid.

Hvis hosten midlertidig ikke finner en aktiv Revit-instans, gjenta den lesende tilkoblingskontrollen et par ganger før du konkluderer. Hvis tilkoblingen fortsatt mangler, stopp; ikke kjør skript eller utfør modellkontroll. Preflight er kun en tilkoblingskontroll og erstatter ikke en modellkontroll som er del av forespørselen.

Å lese eller redigere denne instruksjonen i VS Code starter ikke en modellkontroll.
