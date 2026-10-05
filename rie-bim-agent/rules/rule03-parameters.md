# Regel 3: Parameterkontroll

Den autoritative beskrivelsen av formål, dokumenter og kjøringsrekkefølge står under [Regel 3 i hovedinstruksjonen](../rie-bim-kontroll.instructions.md#regel-3-parameterkontroll).

Kontrollen gjelder RIE-elektrofaglige familiekategorier fra workset-mappingen. `OST_MEPSpaces` og `OST_SpecialityEquipment` skal filtreres bort før instans- og typeparametere leses. Dette utelater blant annet InfoNode-familiene fra Regel 3.
