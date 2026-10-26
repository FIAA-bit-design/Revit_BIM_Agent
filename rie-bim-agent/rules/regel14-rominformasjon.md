Unntaksfanen støtter familieprefikser i formatet `Alle familier som begynner med <prefiks>`, typenavnprefikser i formatet `Alle typenavn som begynner med <prefiks>` og regelen `Alle typenavn for Conduit som inneholder 25mm eller har Size 25mm`. Familie- og typenavnprefikser matcher skiftleifritt mot starten av henholdsvis `Familienavn` og `Type`. Conduit-regelen gjelder bare `OST_Conduit` og matcher når typenavnet eller tekst-instansparameteren `Size` inneholder `25mm`; mellomrom ignoreres, slik at `25 mm` også matcher. Elementer som ellers har romavvik og matcher en regel tas ut av hovedfanen og vises i `Unntak` med regelteksten. Ukjent format, dupliserte regler, manglende arbeidsbok eller manglende ark blokkerer rapporten.
# Regel 14: Manglende rominformasjon

Regel 14 er et separat, lesende verktøy utenfor standard BIM-sekvens. Det lager en tidsstemplet Excel-liste over elementer med manglende eller ugyldig instansparameter `Romnavn` eller `dRofus_RoomfunctionNo`. Det skriver ikke parametere, starter ingen transaksjon, leser ikke Space-geometri og endrer ikke aktiv viewfase. Brukerens `logs/reports/Mangler_rominformasjon.xlsx` er kilde for unntakstekstene i arket `Unntak`; kildefilen overskrives ikke.

## Utvalg

Bare de eksakte BAS- og STA-modellene `U_F_BAS_FBU_RIE_XXX` og `U_F_STA_FBU_RIE_XXX` godtas. Utvalget omfatter Regel 3s elektrofaglige kategorier, eksakt `FOB_Entreprise = K5B` og entydige, skrivbare, blanke instansverdier for `FOB_Entreprise`. Andre entrepriser og manglende, dupliserte eller feiltypede entrepriseparametere utelates. Elementer med nøyaktig én heltallsparameter `MC Exclude From IFC View` med verdi ulik null utelates. I BAS utelates workset `RIE Temp Varelevering`. Senterlinjer og `OST_SpecialityEquipment` inngår ikke. Regel 14 leser, validerer eller filtrerer ikke på `PGF_RIE_Master`-lenken.

## Avvik

Et romfelt flagges når instansparameteren mangler, er duplisert, ikke er tekst, er blank eller inneholder `--` (trimmet). For trekkerør, trekkerørsfittings og elementer med `FOB_Eksistensstatus = Infoelement` er nøyaktig `--` gyldig; alle andre verdier flagges. Eksakt typenavn `Trekkerør 25 mm K5B` og tilhørende bend er unntatt fra dette plassholderkravet, slik at blanke verdier og `--` flagges som manglende rominformasjon. Regel 14 kontrollerer ikke om verdiene samsvarer med en Space.

Rapporten har én rad per berørt element og viser begge romfeltene, ElementId, kategori, familie, type, entreprise, leveransepakke, merkestreng og avviksgrunn. Gyldige verdier bevares som rapportkontekst. `dRofus_RoomID` og `dRofus_Rombetegnelse` er ikke målparametere.

Unntaksfanen bruker formatene beskrevet ovenfor. Elementer som ellers har romavvik og matcher familie-, type- eller Conduit-regelen tas ut av hovedfanen og vises i `Unntak` med regelteksten. Ukjent format, dupliserte regler, manglende arbeidsbok eller manglende ark blokkerer rapporten.

## Kjøring

Bygg `Rule14Runner/Rule14Runner.csproj`. Kjør runneren via Assistant mot aktiv BAS- eller STA-modell. Oppgi en absolutt `WorkspaceRoot` for å lagre rapporten under `logs/reports`; tom verdi bruker runnerens katalog. Hver kjøring oppretter en ny fil, og tidligere rapporter overskrives ikke. Implementasjonen gjør ingen modellendringer og inngår ikke i standard kjørerekkefølge.