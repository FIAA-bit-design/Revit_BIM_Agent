# Filter for føringsveiview før dRofus-synk

Strømskinner skal ikke inngå i filtrerte føringsveiview for dRofus eller i kontrollgrunnlaget før synkronisering av disse viewene. Dette gjelder blant annet BAS-viewet `KS Kablebro dRofus`.

- Identifiser strømskinner ved at familie- eller typenavnet inneholder `Strømskinne`, uten hensyn til store/små bokstaver. Ikke bruk hele kategorien `OST_CableTray` som utelatelsesfilter; ordinære kabelbroer og øvrige føringsveier skal fortsatt kontrolleres.
- Utelat strømskinner før kontroll av dimensjoner, `dRofus_ItemID`, merkestrenger, duplikater og dekning med én synlig representant per merkestreng.
- Strømskinner som ikke er synlige i viewet, er en tilsiktet avgrensning. Manglende dRofus-ID eller synlig representant for disse skal ikke rapporteres som avvik eller blokkere synkgrunnlaget.
- Hvis strømskinner likevel er synlige i målviewet, rapporter dette som et viewavgrensningsavvik med ElementId. Unntaket gir ikke i seg selv tillatelse til å endre viewet eller modellen.
- Oppgi utelatelsen og antall utelatte strømskinner separat i kontrollrapporten, uten å telle dem som kontrollerte elementer eller gjenstående avvik.

Unntaket gjelder bare føringsveiview for dRofus og tilhørende kontroll før synk. Andre BIM-kontroller og regler beholder sine egne omfang og filtre. Ikke endre eller slette strømskinner, parameterverdier eller geometri som følge av dette filteret.

## C4 har egne dRofus-artikler

C4 identifiseres etter typenavnet i Revit. C4-føringsveier skal ha egne artikler, ikke samme `dRofus_ItemID` som ordinære føringsveier med samme bredde. Artikkelvalget krever både riktig utførelse og faktisk instansbredde; ikke grupper ordinære og C4-segmenter som samme strekk bare fordi bredde eller merke er likt.

Brukerbekreftede C4-koblinger i BAS K5B:

| Revit-type | Faktisk bredde | dRofus_ItemID |
| --- | --- | --- |
| Kabelstige 400 mm x 60 mm C4 | 400 mm | 16289 |
| Kabelstige 600 mm x 60 mm C4 | 600 mm | 16290 |
| Veggkanal 73 mm x 123 mm C4 | 123 mm | 16291 |

123 mm er uttrykkelig bekreftet av brukeren. Koblingene gir ikke en generell breddebasert artikkeltildeling til andre typer eller utførelser. Ukjente C4-typer eller avvik mellom typenavn og faktisk bredde skal avklares.

`dRofus_ItemID` er en typeparameter: kontroller alle instanser av typen, også utenfor aktivt view og bekreftet entreprise, før endring. Ikke endre en delt type uten avklaring dersom andre entrepriser berøres. Ordinære typer beholder sine egne artikkel-ID-er. Et artikkelbytte gir ikke tillatelse til å nullstille occurrence-ID-er, slette dRofus-forekomster eller starte synk.

## En representant per bekreftet strekk

- Flere segmenter med samme gyldige merkestreng på samme dokumenterte strekk skal ha bare én synlig representant i dRofus-viewet. Behold en allerede registrert eller entydig eksisterende synkronisert representant. Ved nye brukerbekreftede grupper uten entydig kandidat har brukeren godkjent lengste segment, laveste ElementId ved lik lengde. Ikke bytt en registrert representant automatisk.
- Hver markering som brukeren sender inn for filtrering er ett bekreftet strekk, etter kontroll av faktisk utvalg, type, dimensjoner og C4/ordinær utførelse. Stopp ved blandet eller overlappende medlemskap. Lagre koblingene etter separat etterkontroll i [strekkregisteret](../../config/DrofusConfirmedRoutes.tsv), og forny den samlede [Excel-listen](../../config/DrofusConfirmedRoutes.xlsx) etter [arbeidsrutinen](../../Rule12Runner/README.md#fortløpende-markeringer-og-samlet-excel-liste). Skjuling gir ikke tillatelse til samtidig ommerking eller sletting av gamle dRofus-forekomster.
- Brukerens foretrukne arbeidsflyt er nå lesende innsamling først: Rule12Runner `RegisterSelection` lagrer hver markering i `config\DrofusRouteQueue.json` og lager en samlet, tidsstemplet Excel-kørapport uten modellendring. `PreviewBatch` kontrollerer køen; `ApplyBatch` med `PreviewOnly=false` utfører viewskjulingen samlet når brukeren bestiller det. Ikke utfør køen som del av registreringen. Se [kørutinen og statusreglene](../../Rule12Runner/README.md#registrer-først-utfør-samlet-etterpå). Identitets- eller modellendring siden registrering blokkerer berørt gruppe; utførte koblinger føres til TSV-registeret. Gamle registreringer skal ikke rekonstrueres fra dagens merkestreng.
- Lik merkestreng alene, nærhet eller tilsynelatende horisontal retning beviser ikke strekktilhørighet. Bruk dokumentert tilhørighet eller et uttrykkelig brukerbekreftet elementutvalg. Ikke slå sammen parallelle broer, vertikale avgreninger eller ulike dimensjoner uten avklaring.
- Alle føringsveisegmenter innen omfanget skal ha gyldig `FOB_Merkestreng` og indeks/sekvensnummer (`FOB_Sekvensnummer`), også skjulte og ikke separat synkroniserte segmenter. Samme dokumenterte strekk skal følge representantens gyldige dRofus-merke og sekvensnummer. Tom tekst, `--` og feilmeldinger er uløste avvik.
- Viewskjuling er ikke et unntak fra utfyllingskravet eller bevis på ferdig berikelse. Mangler gyldig dRofus-kilde, bevar eksisterende verdier og rapporter alle berørte segmenter som uløste avvik; ikke generer erstatningsmerker. Skjul bare dokumentert tilhørende ekstrasegmenter i avtalt view. Ikke tøm ID-er eller slett dRofus-forekomster; eventuell parameterutfylling følger den separate etter-synk-rutinen.
- Skjuling endrer fremtidig synkutvalg, ikke forekomster som allerede finnes i dRofus. Eventuell opprydding i disse krever separat avklaring av synkretning og sletting. Ikke godkjenn eller omskriv occurrence-referansen automatisk.
