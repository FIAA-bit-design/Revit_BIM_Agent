# Regel 3: Parameterkontroll

Den autoritative beskrivelsen av formål, dokumenter og kjøringsrekkefølge står under [Regel 3 i hovedinstruksjonen](../rie-bim-kontroll.instructions.md#regel-3-parameterkontroll).

Parameternavnene og de redigerbare fagregeltekstene hentes fra den autoritative arbeidsboken `../../config/Parameterliste.xlsx`, fanen `Parameterliste`. C#-regelen finner kolonnene via overskriftene og leser navnene fra `Parameternavn`. Oppdater forutsetningene i arbeidsboken; faktisk kjørbar oppførsel må fortsatt implementeres i C#-regelen. Revisjonsfeltene `FOB_Revisjonsdato`, `PGF_Revisjonsign` og `PGF_Revisjonsindeks` kontrolleres separat av [regel 12](regel12-kontroller-revisjonsparametere-per-leveransepakke.md) mot `Revisjonsliste.xlsx`.

Kontrollen gjelder RIE-elektrofaglige familiekategorier fra workset-mappingen. `OST_MEPSpaces` og `OST_SpecialityEquipment` skal filtreres bort før instans- og typeparametere leses. Dette utelater blant annet InfoNode-familiene fra Regel 3.

Før generell parameterutfylling kontrollerer Regel 3 `OST_CableTray` og `OST_CableTrayFitting` for manglende `FOB_Sekvensnummer`, `FOB_Merkestreng` eller `FOB_Omraade`. Ved treff kjøres C#-motoren [CableTrayMarkingEngine.cs](../../CSharpRules/CableTrayMarkingEngine.cs) som en planlagt forløper i samme Regel 3-transaksjon. Den grupperer via connectors, bruker 1–250 mm nærhetsfallback, tildeler ett firesifret sekvensnummer per gruppe og bygger merkestreng fra område, system, funksjonskode og sekvens. Manglende område hentes fra inneholdende Space eller, for fittings, entydig fysisk tilkoblet rett kabelbro. Uavklarte kildeverdier forblir uendret og rapporteres; de fylles ikke med `--`.

Forløperen følger det historiske skriptets scope: den behandler kabelbroer og fittings i hele den aktive modellen, ikke bare K5B. Strømskinner, typefunksjonskode `STS` og `FOB_Status = S5` utelates/beskyttes. Eksisterende sekvensgrupper kan omnummereres for å løse duplikater og lukke sekvensgap. Dette er en muterende del av Regel 3-kjøringen og skal synliggjøres i rapporten.

For elementer med eksakt instansverdi `FOB_Entreprise = K5B` håndheves `FOB_Status = S4` også når eksisterende status er blank, `--` eller en annen verdi. `FOB_Status = S5` bevares som lås og skal ikke endres. Andre entrepriseverdier berøres ikke av denne statusregelen.

Hver kjøring lager `../../logs/reports/Rule03_ParameterFill <tid>.txt` med kjøringsoppsummering, oppdateringer og uavklarte parameterfunn. Append-only historikklogg bevares separat.
