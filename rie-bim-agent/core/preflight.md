# Obligatorisk Revit-preflight

Før enhver Revit-relatert regel, skriptkjøring eller modellkontroll skal Revit-tilkoblingen kontrolleres via MCP. Hent og bekreft aktivt dokument, og list tilgjengelige Revit-verktøy før videre arbeid.

Hvis hosten midlertidig ikke finner en aktiv Revit-instans, gjenta den lesende tilkoblingskontrollen et par ganger før du konkluderer. Hvis tilkoblingen fortsatt mangler, stopp; ikke kjør skript eller utfør modellkontroll. Preflight er kun en tilkoblingskontroll og erstatter ikke en modellkontroll som er del av forespørselen.

Å lese eller redigere denne instruksjonen i VS Code starter ikke en modellkontroll.
