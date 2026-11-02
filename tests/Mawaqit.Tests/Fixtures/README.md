# Fixtures

Trimmed `confData` captured from public mawaqit.net mosque pages on 2026-11-02 04:45 UTC
(23:45 on 1 November in Montreal). Only the keys the widget reads are kept.

| File | Why it is here |
|------|----------------|
| `montreal-assahaba.confdata.json` | Normal 6-column calendar. `times` shows **2 November** while it is still 1 November in Montreal: Mawaqit's "today" runs on its server clock. |
| `drammen-isha-wrap.confdata.json` | Nordic summer: Isha after midnight (e.g. 13 June Maghrib 22:45, Isha 00:00). |
| `oslo-imsak-column.confdata.json` | Mosque with imsak display on: rows have a leading 7th column. |

`aladhan-reference.json` holds api.aladhan.com timings (angle-based high-latitude rule) for 15 cities, their usual method and Asr school, on 5 dates: a normal day, the North American DST switch, midsummer (Oslo twilight all night), midwinter, and a Ramadan day (Umm al-Qura Isha +120 min).

Refresh them with `dotnet run --project tools/Mawaqit.Probe -- --save-fixture <slug>`.
