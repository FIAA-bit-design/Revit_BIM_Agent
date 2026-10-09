# Regel 2: Fyll manglende FOB_Leveransepakke fra nærliggende elementer

Kjør denne regelen før den generelle parameterutfyllingen i regel 3. Kontroller alle ikke-type modellelementer med nøyaktig én tekst-instansparameter `FOB_Entreprise` lik `K5B`. Elementer i andre entrepriser kontrolleres ikke. `FOB_Leveransepakke` skal ikke være tom. En eksisterende tekst-instansparameter med blank verdi eller `--` regnes som manglende. Hvis `FOB_Leveransepakke` mangler eller har feil lagringstype på et K5B-element, logges ElementId og årsak som uavklart; parameteren skal ikke hentes fra typen eller opprettes av regelen. Revisjonslisten brukes kun til revisjonskontrollen i regel 3, ikke som hviteliste for pakkeutfyllingen. Denne tolkningen av `--` gjelder bare regel 2.

- C#-implementasjon: `../../CSharpRules/Rule02FillMissingPackage.cs`

Filtrer bort senterlinjer før parameterverdier leses. K5B-elementer der den eneste instansparameteren `MC Exclude From IFC View` har lagringstypen Integer og verdi ulik null (avkrysset), utelates fra regelen. De skal verken være mål eller kilde/kandidat, og skal ikke telles som manglende leveransepakke. Ikke bruk typeparameterfallback.

Finn andre K5B-elementer med utfylt `FOB_Leveransepakke` ved å bruke 3D-avstand mellom plasseringer. Elementtyper bruker typenavn/familienavn for å skille kandidater; familier for `FamilyInstance` bruker familienavnet. Bruk punktplassering, kurvens midtpunkt eller bounding-box-sentrum når punktplassering mangler. Søk først innen 1500 mm; bruk 3000 mm som fallback bare når primærsøket ikke gir kandidater. Vurder de fem nærmeste kandidatene, og ta med alle kandidater innen 1 mm fra femteplass. Fyll inn bare hvis én pakkenavnverdi har strengt flertall, altså mer enn halvparten av kandidatene. Hvis primærsøket har kandidater uten strengt flertall, skal fallback ikke brukes.

Hvis kandidatene ikke gir et strengt flertall, treff mangler innen 3000 mm, parameteren ikke kan skrives eller plassering mangler, ikke gjett og ikke skriv `--`. La verdien stå tom, logg ElementId, kandidattelling og årsak, og rapporter at leveransepakken må avklares. Eksisterende `FOB_Leveransepakke`-verdier skal ikke overskrives.

Hver kjøring lager en tidsstemplet rapport i `../../logs/reports/Regel 2 rapport <tid>.txt` med kjøreoppsummering og elementdetaljer. Append-only historikklogg bevares separat.
