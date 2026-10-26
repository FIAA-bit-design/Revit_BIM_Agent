# Rule14Runner v0.0.2
# Rule14Runner v0.0.4
Familieunntak følger formatet `Alle familier som begynner med <prefiks>`. Typenavnprefikser følger formatet `Alle typenavn som begynner med <prefiks>`. Conduit-unntaket matcher 25 mm i typenavn eller instansparameteren `Size`, uten hensyn til mellomrom eller store/små bokstaver. Ukjent format, manglende ark eller dupliserte regler stopper rapporten før elementlesing.

Separat, lesende Assistant-verktøy for Revit 2026. Det lager en tidsstemplet Excel-liste over elementer med manglende eller ugyldig rominformasjon. Det inngår ikke i standard BIM-sekvens og utfører ingen transaksjoner eller modellendringer.

## Bygg og bruk

Bygg fra prosjektroten:

```powershell
dotnet build Rule14Runner/Rule14Runner.csproj
```

Kjør prosjektet via Assistant mot en aktiv, eksakt BAS- eller STA-modell. Oppgi en absolutt prosjektrot:

```json
{"WorkspaceRoot":"D:\\Revit\\Python\\Revit_BIM_Agent"}
```

Unntaksreglene leses fra `logs/reports/Mangler_rominformasjon.xlsx`, arket `Unntak`. Rapporten opprettes som `logs/reports/Mangler_rominformasjon <tid>.xlsx` med fanene `Mangler rominfo` og `Unntak`. Familieprefikstreff flyttes fra hovedlisten til unntaksfanen med regelteksten som forklaring. Den oppdaterte kildearbeidsboken overskrives ikke. En ny kjøring lager en ny tidsstemplet fil; tidligere rapporter bevares. Tom `WorkspaceRoot` bruker runnerens DLL-katalog.

## Utvalg og rapport

Se [Regel 14-beskrivelsen](../rie-bim-agent/rules/regel14-rominformasjon.md) for utvalg og avvikskriterier. Kort sagt vurderes elektrofaglige K5B-elementer og skrivbare blanke entrepriseverdier i de støttede modellene. IFC-ekskluderte elementer og BAS-workset `RIE Temp Varelevering` utelates. Rule14 leser ikke `PGF_RIE_Master`, Space-geometri eller lenkemodeller. Unntaksteksten må følge formatet `Alle familier som begynner med <prefiks>`; ukjent format, manglende ark eller dupliserte prefikser stopper rapporten før elementlesing.

Hovedfanen har én rad per ikke-unntatt element som har minst ett avvik i `Romnavn` eller `dRofus_RoomfunctionNo`. Unntaksfanen viser elementer som ellers ville vært avvik, sammen med den matchende regelen. Begge viser romverdier, elementkontekst og årsak. Ingen romverdier fylles eller korrigeres.