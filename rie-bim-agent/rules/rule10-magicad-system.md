# Regel 10: Fyll Magicad-systemverdier fra tilkoblet rett føringsvei

Kjør etter regel 1–9 og før Regel 11 workset-kontroll og den avsluttende Regel 12-rapporten.

- C#-implementasjon: `../../CSharpRules/Rule10MagicadSystemFromConnectedStraight.cs`
- Append-only logg: `../../logs/history/Rule10_MagicadSystemFromConnectedStraight.log`

Kontroller kabelbrofittings og trekkerørsfittings med nøyaktig én tekst-instansparameter `FOB_Entreprise` lik `K5B`. Bevar eksisterende ikke-blanke verdier i `MC System Code` og `MC System Name`; fyll bare manglende instansverdier.

Bruk bare direkte fysisk tilkoblede rette føringsveier av tilsvarende kategori: kabelbrofitting fra kabelbro, trekkerørsfitting fra trekkerør. Kildeelementet må ha `LocationCurve` med `Line` og nøyaktig `FOB_Entreprise=K5B`. Ikke bruk nærhet eller typeparameter som reserve.

For hver målparameter må alle brukbare direkte kilder ha samme ikke-blanke tekstverdi. Manglende parameter, ugyldig kilde, blank kildeverdi eller motstridende verdier rapporteres med mål-ID og årsak; ikke gjett. Logg kilde-ID-er og verdier for utfyllinger. Andre entrepriser utelates.