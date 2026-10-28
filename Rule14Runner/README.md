# Rule14Runner v0.0.9
Familieunntak følger formatet `Alle familier som begynner med <prefiks>`. Typenavnprefikser følger formatet `Alle typenavn som begynner med <prefiks>`. Aspirasjonrør filtreres bare via typenavn. Trekkerørsbend har en avgrenset 25 mm-regel. `3D Detaljpil nummer` bruker en påkrevd mellomromsgrense etter `nummer`, slik at bare nummererte typer unntas. Conduit-unntaket matcher 25 mm i typenavn eller instansparameteren `Size`, uten hensyn til mellomrom eller store/små bokstaver. Ukjent format, manglende ark eller dupliserte regler stopper rapporten før elementlesing.
	return results
Separat, lesende Assistant-verktøy for Revit 2026. Det lager en tidsstemplet Excel-liste over elementer med manglende eller ugyldig rominformasjon. Det inngår ikke i standard BIM-sekvens og utfører ingen transaksjoner eller modellendringer.

## Bygg og bruk

Bygg fra prosjektroten:

```powershell
dotnet build Rule14Runner/Rule14Runner.csproj
```

For kjøring fra COWI Assistant velger du `Start Regel 14 i COWI Assistant.py` i oppgaven `Run Python Script for Revit`. Bygg prosjektet først. Skriptet finner arbeidsområderoten fra sin egen filplassering, bruker aktivt Revit-dokument og kaller `Rule14PythonActionBridge` i den bygde runneren. Det krever ingen `WorkspaceRoot`-variabel i Assistant.

Kjør prosjektet via Assistant mot en aktiv, eksakt BAS- eller STA-modell. Oppgi en absolutt prosjektrot:

```json
{"WorkspaceRoot":"D:\\Revit\\Python\\Revit_BIM_Agent"}
```

Unntaksreglene leses fra `config/Mangler_rominformasjon.xlsx`, arket `Unntak`. Rapporten opprettes som `logs/reports/Mangler_rominformasjon <tid>.xlsx` med fanene `Mangler rominfo` og `Unntak`. Familieprefikstreff flyttes fra hovedlisten til unntaksfanen med regelteksten som forklaring. Kildearbeidsboken overskrives ikke. En ny kjøring lager en ny tidsstemplet fil; tidligere rapporter bevares. Tom `WorkspaceRoot` bruker runnerens DLL-katalog.

## Utvalg og rapport

Se [Regel 14-beskrivelsen](../rie-bim-agent/rules/regel14-rominformasjon.md) for utvalg og avvikskriterier. Kort sagt vurderes elektrofaglige K5B-elementer og skrivbare blanke entrepriseverdier i de støttede modellene. IFC-ekskluderte elementer og BAS-workset `RIE Temp Varelevering` utelates. Rule14 leser ikke `PGF_RIE_Master`, Space-geometri eller lenkemodeller. Unntaksteksten må følge formatet `Alle familier som begynner med <prefiks>`; ukjent format, manglende ark eller dupliserte prefikser stopper rapporten før elementlesing.

For `OST_Conduit` med eksakt instansverdi `FOB_Entreprise=K5B` og tekst-instansparameteren `Size` på minst 50 mm kreves gyldig romtekst i både `Romnavn` og `dRofus_RoomfunctionNo`; blanke verdier og `--` flagges. Størrelsen leses fra instansparameteren uten typefallback. Andre trekkerør og trekkerørsfittings beholder plassholderregelen, med det særskilte 25 mm-unntaket beskrevet i regelinstruksen.

Hovedfanen har én rad per ikke-unntatt element som har minst ett avvik i `Romnavn` eller `dRofus_RoomfunctionNo`. Unntaksfanen viser elementer som ellers ville vært avvik, sammen med den matchende regelen. Begge viser romverdier, elementkontekst og årsak. Ingen romverdier fylles eller korrigeres.