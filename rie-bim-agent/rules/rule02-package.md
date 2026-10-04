# Regel 2: Fyll manglende FOB_Leveransepakke fra nærliggende elementer

Kjør denne regelen før den generelle parameterutfyllingen i regel 3. `FOB_Leveransepakke` skal ikke være tom. For denne regelen regnes en eksisterende instansparameter med blank verdi eller `--` som manglende; parameterobjekt som ikke finnes, utelates. Revisjonslisten brukes kun til revisjonskontrollen i regel 3, ikke som hviteliste for pakkeutfyllingen. Denne tolkningen av `--` gjelder bare regel 2.

- C#-implementasjon: `../../CSharpRules/Rule02FillMissingPackage.cs`

Finn elementer i andre familier med utfylt `FOB_Leveransepakke` ved å bruke 3D-avstand mellom plasseringer. Bruk punktplassering, kurvens midtpunkt eller bounding-box-sentrum når punktplassering mangler. Søk først innen 1500 mm; bruk 3000 mm som fallback bare når primærsøket ikke gir kandidater. Vurder de fem nærmeste kandidatene, og ta med alle kandidater innen 1 mm fra femteplass. Fyll inn bare hvis én pakkenavnverdi har strengt flertall, altså mer enn halvparten av kandidatene. Hvis primærsøket har kandidater uten strengt flertall, skal fallback ikke brukes.

Hvis kandidatene ikke gir et strengt flertall, treff mangler innen 3000 mm, parameteren ikke kan skrives eller plassering mangler, ikke gjett og ikke skriv `--`. La verdien stå tom, logg ElementId, kandidattelling og årsak, og rapporter at leveransepakken må avklares. Eksisterende `FOB_Leveransepakke`-verdier skal ikke overskrives.
