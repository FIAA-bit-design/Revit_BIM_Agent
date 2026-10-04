# Brukerfilter for mengdelistepostkontrollen

Følgende utelatelser fra brukerens kommentarer i kildekolonnen gjelder bare `FOB_Mengdelistepost`-kontrollen knyttet til `Logg av endringer`, inkludert mål, generiske kilder, connector-kilder og tilhørende avvikstelling/rapporter. De endrer ikke den generelle endringssammenligningen eller andre parameterregler. Bruk familienavn uten hensyn til store/små bokstaver:

- Familienavn som inneholder `port` eller `veggboks`, utelates.
- Familien `Personheis` utelates ved eksakt familienavn. Excel-teksten `=K2:K1107` er ikke et filter for hele celleområdet.
- Familienavn som inneholder `teknisk`, utelates bare i kategorien `OST_ElectricalEquipment`; bruk kategori-ID, ikke lokalisert visningsnavn.
- Familienavn som inneholder `adgangskontroll`, utelates med unntak for navn som også inneholder `grensesnittboks`. Unntaket gjelder adgangskontrollregelen; andre uavhengige filterregler gjelder fortsatt.
- Instanser med nøyaktig én tekst-instansparameter `MC System Code`, der verdien etter trimming er `733.1`, utelates. Ikke bruk `MC Default System Code`, typeparameter-fallback, prefiksmatching eller hele familien som reserve. Manglende, duplisert eller feil lagringstype er ikke bevis på kode `733.1`.

Eksisterende senterlinje-, entreprise- og detaljpilfilter beholdes. Utelatte elementer skal ikke brukes som kilder eller telles som gjenstående avvik. Dokumenter utelatelsesårsak og ElementId separat, uten å endre eller slette elementer/parameterverdier. Bevar brukerens opprinnelige hovedrapport og kommentarer; skriv en ny tidsstemplet filtrert rapport.
