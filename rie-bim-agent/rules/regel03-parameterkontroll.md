# Regel 3: Parameterkontroll

Den autoritative beskrivelsen av formål, dokumenter og kjøringsrekkefølge står under [Regel 3 i hovedinstruksjonen](../rie-bim-kontroll.instructions.md#regel-3-parameterkontroll).

Parameternavnene og de redigerbare fagregeltekstene hentes fra den autoritative arbeidsboken `../../config/Parameterliste.xlsx`, fanen `Parameterliste`. C#-regelen finner kolonnene via overskriftene og leser navnene fra `Parameternavn`. Oppdater forutsetningene i arbeidsboken; faktisk kjørbar oppførsel må fortsatt implementeres i C#-regelen. Revisjonsfeltene `FOB_Revisjonsdato`, `PGF_Revisjonsign` og `PGF_Revisjonsindeks` kontrolleres separat av [regel 12](regel12-kontroller-revisjonsparametere-per-leveransepakke.md), som velger revisjonsliste etter aktiv modell.

Regel 3 kontrollerer bare elementer med eksakt `FOB_Entreprise = K5B` samt elementer med en entydig, skrivbar, blank instansparameter `FOB_Entreprise`, som fylles med `K5B` i samme kjøring. Elementer med andre entrepriseverdier eller manglende, duplisert eller feiltypet `FOB_Entreprise` utelates fra øvrige Regel 3-kontroller og rapportering. Eksisterende entrepriseverdier bevares. Regel 12 kontrollerer bare eksakt `FOB_Entreprise = K5B`.

Kontrollen gjelder RIE-elektrofaglige familiekategorier fra workset-mappingen. `OST_MEPSpaces` og `OST_SpecialityEquipment` skal filtreres bort før instans- og typeparametere leses. Dette utelater blant annet InfoNode-familiene fra Regel 3.

Elementer med nøyaktig én instansparameter `MC Exclude From IFC View`, med lagringstype Integer og verdi ulik null, utelates fra Regel 3 før entreprise- og parameterbehandling. De skal verken være mål eller kilde/kandidat. Ikke bruk typeparameterfallback; manglende, duplisert eller feiltypet parameter er ikke bevis på IFC-ekskludering. Antallet utelatte elementer logges separat.

Hele `PGF_RIE_Master.rvt`-lenken utelates fra Regel 3. Elektriske kategori/familie/type-signaturer leses fra lenken, og aktivelementer som matcher en slik signatur utelates før mål- og kildeutvalg, typeparameterkontroller og kabelbroforløper. Lenken må finnes nøyaktig én gang, være lastet og inneholde identifiserbare elektriske typer; ellers stoppes Regel 3 før elementbehandling.


Før generell parameterutfylling kontrollerer Regel 3 `OST_CableTray` og `OST_CableTrayFitting` for manglende `FOB_Sekvensnummer`, `FOB_Merkestreng` eller `FOB_Omraade`. Ved treff kjøres C#-motoren [CableTrayMarkingEngine.cs](../../CSharpRules/CableTrayMarkingEngine.cs) som en planlagt forløper i samme Regel 3-transaksjon. Den grupperer via connectors, bruker 1–250 mm nærhetsfallback, tildeler ett firesifret sekvensnummer per gruppe og bygger merkestreng fra område, system, funksjonskode og sekvens. Manglende område hentes fra inneholdende Space eller, for fittings, entydig fysisk tilkoblet rett kabelbro. Uavklarte kildeverdier forblir uendret og rapporteres; de fylles ikke med `--`.

Forløperen behandler bare kabelbroer og fittings i Regel 3s K5B-scope, inkludert elementer med blank, skrivbar `FOB_Entreprise` som Regel 3 fyller til K5B. Andre entrepriser brukes verken som mål eller connector-kilder. Strømskinner, typefunksjonskode `STS` og `FOB_Status = S5` utelates/beskyttes. Eksisterende sekvensgrupper kan omnummereres for å løse duplikater og lukke sekvensgap. Dette er en muterende del av Regel 3-kjøringen og skal synliggjøres i rapporten.

For `FOB_Mengde` og `FOB_Mengdeenhet` regnes både blank tekst og `--` som manglende. Regel 3 fyller `FOB_Mengdeenhet` etter kategori (`m` for rette føringsveier, ellers `stk`). For rette føringsveier finner ikke Regel 3 på en `FOB_Mengde`; Regel 7 beregner den for sitt K5B-omfang. Andre elementer får standardmengden `1`.

Når `FOB_Omraade` utenfor kabelbro-/trekkerørselementenes særregel eller `PGF_PNS` er blank eller `--`, bruker Regel 3 nærmeste gyldige instansverdi blant elektriske instanser med samme `FOB_Entreprise` og `FOB_Leveransepakke`. Søket følger 50–1500 mm primærradius og 1500–3000 mm fallback bare uten primærtreff. For K5B fortsetter søket uten øvre avstandsgrense hvis ingen kandidat finnes innen 3000 mm; samme familie utelates fortsatt. Ulike verdier fra kilder innen 1 mm, manglende entreprise/pakke eller ingen gyldig kilde noe sted gir ingen skriving og logges som uavklart. Eksisterende gyldige verdier bevares.

`PGF_RIE_Talevarslingsystemer` kontrolleres bare i den eksakte modellen `U_F_STA_FBU_RIE_XXX.rvt`. Parameteren utelates fra Regel 3-utvalget i alle andre modeller, siden den ikke finnes i BAS-modellen.

På `OST_FireAlarmDevices` synkroniseres `PGF_RIE_Alarmsystemer` av Regel 9 fra `FOB_FysiskMerke`. For andre kategorier følger parameteren Regel 3s generelle utfylling, slik at blanke verdier fylles med `--`.

For alle instanser i Regel 3-utvalget der `PGF_RIE_Bygg_VVSsystemer` finnes, skal verdien være nøyaktig `--`. Regel 3 overskriver blanke verdier og alle andre verdier; instanser uten parameteren inngår i den vanlige tellingen av manglende parametere.

`PGF_RIE_Fra` og `PGF_RIE_Til` gjelder bare trekkerør (`OST_Conduit`) og tilhørende trekkerørsfittings (`OST_ConduitFitting`). For disse kategoriene følger blanke verdier Regel 3s vanlige utfylling; uten særskilt parameterregel fylles `--`. Andre kategorier kontrolleres, fylles eller rapporteres ikke for disse parameterne.

For `FOB_Omraade` utenfor kabelbro-/trekkerørselementenes særregel og for `PGF_PNS` kopierer Regel 3 ved blank verdi eller `--` fra nærmeste elektriske instanselement med gyldig instansverdi, samme `FOB_Entreprise` og samme `FOB_Leveransepakke`. Søk etter nærmeste kilde innen 50–1500 mm; bruk 1500–3000 mm bare når primærsøket ikke gir treff. For K5B fortsetter søket uten øvre avstandsgrense hvis ingen kandidat finnes innen 3000 mm. Samme familie utelates. Ved motstridende verdier blant kilder innen 1 mm, manglende identitetsparametere eller ingen gyldig kilde noe sted står målet uendret og logges som uavklart. Eksisterende gyldige målverdier overskrives ikke.

For elementer med eksakt instansverdi `FOB_Entreprise = K5B` håndheves `FOB_Status = S4` også når eksisterende status er blank, `--` eller en annen verdi. `FOB_Status = S5` bevares som lås og skal ikke endres. Andre entrepriseverdier berøres ikke av denne statusregelen.

Hver kjøring lager `../../logs/reports/Rule03_ParameterFill <tid>.xlsx` som en filtrerbar liste med type, ElementId, familienavn, `FOB_Merkestreng`, parameter, verdi og detalj. Append-only historikklogg bevares separat.
