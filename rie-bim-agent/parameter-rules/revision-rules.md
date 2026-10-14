# Utfyllingskontroll av revisjonsparametere

Kontrollen utføres av [Regel 3](../rules/regel03-parameterkontroll.md). Regel 12 er utgått. Kontroller de tre tekst-instansparameterne `FOB_Revisjonsdato`, `PGF_Revisjonsign` og `PGF_Revisjonsindeks` i Regel 3s eksisterende K5B-utvalg, etter eventuell parameterutfylling. Regel 3s kategori-, senterlinje-, IFC- og PGF_RIE_Master-filtre gjelder også her.

En gyldig verdi er utfylt og er ikke `--` etter trimming. Tom tekst, bare whitespace og `--` rapporteres som revisjonsavvik. Manglende, dupliserte eller feiltypede instansparametere rapporteres med ElementId og årsak. Ikke bruk typeparameterfallback. En utfylt, skrivebeskyttet tekstparameter er gyldig når verdien oppfyller utfyllingskravet.

Revisjonskontrollen er kun lesende: ikke fyll, kopier eller korriger revisjonsverdier, ikke bruk connector-kilder, og ikke slå opp eller sammenlign mot Excel-revisjonslister. Ingen bestemt dato, signatur, indeks eller format kreves utover utfyllingskravet. Kontrollen er uavhengig av `FOB_Leveransepakke`, også når pakken er tom, `--` eller ikke finnes i en tidligere revisjonsliste.

Revisjonsavvik inngår i Regel 3s telling av ufullstendige parameterverdier, tidsstemplede XLSX-rapport og append-only historikklogg. Ta med ElementId, parameter, faktisk verdi, leveransepakke som kontekst og årsak. Skill mellom at kontrollen er gjennomført og at alle revisjonsverdier er gyldige; gjenstående avvik må fremgå av oppsummeringen.

Tidligere revisjonslister, Regel 12-rapporter og historikklogger bevares, men er ikke lenger datakilder for kontrollen.
