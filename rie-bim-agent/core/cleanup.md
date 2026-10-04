# Opprydding av arbeidsmappen

Når du oppretter eller oppdaterer filer i arbeidsmappen, og når brukeren ber om opprydding, kontroller nærliggende skript og genererte filer for tydelige, foreldede kopier. Søk etter referanser i instruksjoner og kode før sletting.

- `Start BIM-agent` er kanonisk startskript. Slett automatisk UUID-navngitte sesjonskopier når de ikke er referert, og den kanoniske starteren er samme eller nyere versjon. Slett også eksakte duplikater av andre startkopier når minst én kopi beholdes.
- Fjern midlertidige bygg- og testfiler som agenten selv har opprettet, etter at den tilhørende kontrollen er ferdig.
- Fjern tomme, urefererte arbeidskataloger og regenererbare cache-kataloger som `__pycache__`. Bevar kataloger med innhold og tomme målmapper som er eksplisitt referert i instruksjoner eller kode, for eksempel `logs/` og `.agent-autoload-selftest`. Hvis formålet med en tom katalog er uklart, vis den som kandidat før sletting.
- Bevar tidsstemplede rapporter, CSV-snapshots, historikk- og regel-logger, revisjonslisten, `.agent-autoload-selftest/Start-Agent-SelfTest` og kanoniske eller unike skript. Ikke slett en unik, ureferert fil automatisk hvis den kan ha manuell eller ekstern bruk; rapporter den som kandidat og be om avklaring.
- Oppsummer slettede filer og viktige kandidater som ble beholdt.
