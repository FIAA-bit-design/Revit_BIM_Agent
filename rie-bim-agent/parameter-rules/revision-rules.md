# Revisjonskontroll per leveransepakke

Bruk [revisjonslisten](../../config/Revisjonsliste.xlsx). Hver fane er navngitt etter en leveransepakke. Behandle kun pakker som finnes som faner i arbeidsboken; ikke kontroller, endre eller rapporter andre pakker i modellen. Bruk verdien i `FOB_Leveransepakke` til å velge fanen med nøyaktig samme navn. Finn hver godkjent verdi ved å slå opp parameternavnet i kolonnen `Parameternavn`, og les verdien fra kolonnen `Verdi`; ikke baser oppslaget på faste radnumre.

For hver pakke i revisjonslisten skal `FOB_Revisjonsdato`, `PGF_Revisjonsign` og `PGF_Revisjonsindeks` være utfylt i modellen og samsvare med verdiene i fanen. Fyll inn manglende modellverdier og korriger avvik. Ikke marker revisjonskontrollen ferdig så lenge noen av feltene mangler eller avviker.

Hvis `FOB_Leveransepakke` er tom eller verdien ikke finnes som fane i revisjonslisten, hopp over pakken stille. Ikke kontroller, endre eller rapporter den. For pakker med en fane i listen må arbeidsboken ha godkjente, utfylte verdier for alle tre revisjonsfeltene. Hvis en godkjent verdi mangler eller er uklar i arbeidsboken, ikke gjett; rapporter dette som en blokkering og ikke marker kontrollen ferdig. En kontrollkjøring alene oppfyller ikke revisjonskontrollen.
