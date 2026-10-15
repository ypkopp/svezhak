# BRAG plan — СВЕЖАК (umirhack.tech)

## What it is
СВЕЖАК — real-time expiry watch for restaurant kitchens. Batches arrive with a shelf-life in **hours**, the kitchen writes off by FEFO, dishes auto-drop to the stop-list when an ingredient runs short, and an AI advisor tells each shift what to sell, order, and write off.

## Audience / hook
Owners of restaurant chains (and the hackathon judges). The money line: *expired stock = burned money*.

## Angle
"Склад без просрочки" — the warehouse that never lets anything expire. Lead with the live dashboard (the actual product, not a mock), because the dashboard *is* the brand: dark glass, expiry LEDs, the freshness spiral.

## Tone
`polished` — serious, elegant, restrained. It's a real product with real metrics; let the UI breathe. Long holds, soft fades. ~20s, landscape 1920×1080, 30fps.

## Visual identity (from the real site/CSS)
- bg `#0f1115`, cream `#F3EEE4`, green `#48BB78`, orange `#ED8936`, red expiry, yellow warn
- font **Onest** (Google) — loaded via CDN, big condensed-feel headlines
- real product PNGs already on the site: dashboard, склад, stop-list, advisor + 3 theme variants
- mascots: Avo (all fresh), Syoma (alarm), Limo (advisor), Risik (empty) — chunky transparent PNGs

## Storyboard (≈20s @ 30fps = ~600 frames)

| # | t (s) | Scene | What moves |
|---|-------|-------|-----------|
| 1 | 0–2.5 | HOOK | Black → a single expiry LED blips to life, then the dashboard slides in around it. Tagline fades in: «Склад без просрочки». |
| 2 | 2.5–6 | DASHBOARD | Full dashboard hero drifts up + slow zoom. "СПИРАЛЬ СВЕЖЕСТИ" reads. Stat counters tick: −31% списаний · 0 просрочек · 24/7. |
| 3 | 6–10 | THE PROBLEM→FIX | Склад rows with expiry LEDs — one batch burns red (countdown), auto-drops to STOP-list. Green check: dish auto-stopped before the till. |
| 4 | 10–14 | ADVISOR | ИИ-советник card — Limo mascot slides in, one clean line: «что списать, что заказать». SignalR live pulse. |
| 5 | 14–17 | THEMES | The dashboard snaps through the 5 themes (night→paper→matcha→lilac) — quick flashes, "5 тем" chip. |
| 6 | 17–20 | OUTRO | Logo lockup + «СВЕЖАК — шеф и ИИ следят за складом вместе». umirhack.tech CTA. Fade to black with the Avo mascot blinking. |

## Music / SFX
Synthesized in-browser (WebAudio offline render → WAV): a low warm pad in A minor, a soft pulse-kick on the beat, a rising arpeggio for the theme snaps, one clean chime on the logo. All in-key, mixed under the pad. No samples.

## Build
- `render.html` — every scene a pure function of time `t`, using the real downloaded PNGs + Onest.
- Playwright headless screenshots → PNG frames → ffmpeg (playwright's bundled `ffmpeg-win64.exe`) → `brag.mp4` (h264, yuv420p).
- Audio rendered offline with the same WebAudio graph into `audio.wav`, muxed in ffmpeg.

## Poster / share
- `brag.jpg` = strongest settled frame (dashboard + tagline).
- `share-copy.txt` — 1–2 lines, RU, product-specific.

## Secrets check
Using only public site assets + fictional demo data already on the site. No keys, no real names/emails. Demo login code is a public demo credential (in README) — fine to show.
