# Filter for føringsveiview før dRofus-synk

Strømskinner skal ikke inngå i filtrerte føringsveiview for dRofus eller i kontrollgrunnlaget før synkronisering av disse viewene. Dette gjelder blant annet BAS-viewet `KS Kablebro dRofus`.

- Identifiser strømskinner ved at familie- eller typenavnet inneholder `Strømskinne`, uten hensyn til store/små bokstaver. Ikke bruk hele kategorien `OST_CableTray` som utelatelsesfilter; ordinære kabelbroer og øvrige føringsveier skal fortsatt kontrolleres.
- Utelat strømskinner før kontroll av dimensjoner, `dRofus_ItemID`, merkestrenger, duplikater og dekning med én synlig representant per merkestreng.
- Strømskinner som ikke er synlige i viewet, er en tilsiktet avgrensning. Manglende dRofus-ID eller synlig representant for disse skal ikke rapporteres som avvik eller blokkere synkgrunnlaget.
- Hvis strømskinner likevel er synlige i målviewet, rapporter dette som et viewavgrensningsavvik med ElementId. Unntaket gir ikke i seg selv tillatelse til å endre viewet eller modellen.
- Oppgi utelatelsen og antall utelatte strømskinner separat i kontrollrapporten, uten å telle dem som kontrollerte elementer eller gjenstående avvik.

Unntaket gjelder bare føringsveiview for dRofus og tilhørende kontroll før synk. Andre BIM-kontroller og regler beholder sine egne omfang og filtre. Ikke endre eller slette strømskinner, parameterverdier eller geometri som følge av dette filteret.
