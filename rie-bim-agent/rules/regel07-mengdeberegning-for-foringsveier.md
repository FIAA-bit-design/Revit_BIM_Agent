# Regel 7: Kjør mengdeberegning for føringsveier

Ved relevant RIE-kontroll før leveranse skal den vedlikeholdte C#-implementasjonen brukes:

- C#-implementasjon: `../../CSharpRules/Rule07QuantityForingsways.cs`

Elementer der familie-, type- eller instansnavnet inneholder `Aspirasjon` eller `Aspiration` (uten hensyn til store/små bokstaver) skal utelates helt fra Regel 7: ikke mål, lengdeberegning, nettverkskilder, avvik eller oppdatering av `FOB_Mengde`. Filteret brukes før entreprise- og lengdeklassifisering.

Cable Tray Fittings der familie- eller typenavnet inneholder `Strømskinne` og `FAMILY_CONTENT_PART_TYPE` er en bendtype, utelates fra mengdeberegning og avvik. Bevar eksisterende `FOB_Mengde`; unntaket gjelder ikke strømskinne-elementer med andre deltyper.

For K5B Conduit Fittings med ElementId `17760381` eller `17760457` brukes den ene instansparameteren `Conduit Length` som lengdekilde når den er en utfylt Double. Disse spesifikke elementene skal dermed inngå i lengdeberegningen selv om `LocationCurve` mangler.
Regelen gjelder kun elementinstanser med nøyaktig én instansparameter `FOB_Entreprise` av lagringstype String, der tekstverdien er eksakt `K5B` (ordinal sammenligning, uten trimming). Endre aldri `FOB_Mengde` for andre entrepriseverdier; ignorer dem stille uten telling eller loggføring. Manglende eller blank `FOB_Entreprise`, dupliserte parametere og feil lagringstype skal utelates, telles og logges som uavklart med ElementId og årsak.

Kabelbro-/kabelstigefittings med `FAMILY_CONTENT_PART_TYPE` lik `Union`, `ChannelCableTrayUnion` eller `LadderCableTrayUnion` trenger ingen lengdeberegning. Utelat dem fra lengdeoppdatering og manglende-lengde-avvik, bevar eksisterende `FOB_Mengde`, og logg dem separat som utelatt. Ikke utvid unntaket til trekkerørsfittings.

Kabelbro-/kabelstige-T-stykker og kryss identifiseres med `FAMILY_CONTENT_PART_TYPE` lik `Tee`, `Cross`, `ChannelCableTrayTee`, `ChannelCableTrayCross`, `LadderCableTrayTee` eller `LadderCableTrayCross`. Ikke skriv den beregnede nettverkslengden på fittingene. Beregn og logg samlet lengde ved å traversere fysisk gjensidig tilkoblede `ConnectorType.End`-forbindelser gjennom K5B-kabelbrofittings og summere `LocationCurve.Length` for unike tilkoblede kabelbroelementer. Sett i stedet instansparameterne `FOB_Mengde` til `1` og `FOB_Mengdeenhet` til `stk`. Denne regelen påvirker ikke lengdeberegningen eller mengdeskrivingen for rette strekk. Ved manglende connectorer eller kabelbrokilder logges en egen uavklart nettverkslengde; ikke gjett eller endre modellen.

Bekreft at aktivt Revit-dokument er riktig leveransemodell før kjøring. C#-regelen endrer `FOB_Mengde` bare for K5B og lager ikke Excel-rapport. Hver kjøring lager en tidsstemplet tekstrapport i `../../logs/reports/Rule07_QuantityForingsways <tid>.txt` med samme sammendrag og elementdetaljer som loggen. Worksharing-dialogen håndteres etter [utføringsreglene](../core/execution-rules.md). Loggen skal inneholde antall oppdaterte, uendrede og uavklarte elementer, og én detaljrad per oppdatert element med `FOB_Leveransepakke`, `PGF_RIE_ElementId`, `FOB_Merkestreng`, `FOB_Mengdelistepost`, gammel og ny `FOB_Mengde`, Revit-ElementId og kategori. Rapporter resultatet, eventuelle feil og rapport-/loggbanene.
