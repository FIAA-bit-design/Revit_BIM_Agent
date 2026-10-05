# Regel 3: Parameterkontroll

Den autoritative beskrivelsen av formål, dokumenter og kjøringsrekkefølge står under [Regel 3 i hovedinstruksjonen](../rie-bim-kontroll.instructions.md#regel-3-parameterkontroll).

Parameternavnene og de redigerbare fagregeltekstene hentes fra den autoritative arbeidsboken `../../config/Parameterliste.xlsx`, fanen `Parameterliste`. C#-regelen finner kolonnene via overskriftene og leser navnene fra `Parameternavn`. Oppdater forutsetningene i arbeidsboken; faktisk kjørbar oppførsel må fortsatt implementeres i C#-regelen. Revisjonsfeltene `FOB_Revisjonsdato`, `PGF_Revisjonsign` og `PGF_Revisjonsindeks` kontrolleres separat av [regel 12](regel12-kontroller-revisjonsparametere-per-leveransepakke.md) mot `Revisjonsliste.xlsx`.

Kontrollen gjelder RIE-elektrofaglige familiekategorier fra workset-mappingen. `OST_MEPSpaces` og `OST_SpecialityEquipment` skal filtreres bort før instans- og typeparametere leses. Dette utelater blant annet InfoNode-familiene fra Regel 3.
