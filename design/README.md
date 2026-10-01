# Kawaii Island — Design (Phase 0A)

Figma: https://www.figma.com/design/uT05fDcByXVM1C5zHpXULZ (page "Kawaii Island")

| Board | Frames |
|---|---|
| Mascot sheet | 2 skins (Bangs, Star clip) × idle / blink / happy / surprised / sleepy — components `Mascot/<skin>/<expression>` |
| Island states — Dark / Light | 01 collapsed · 02a–c compact (music, mail, notification) · 03a–e expanded (overview, music, mail, alerts, apps) |
| Desktop scenes — Dark / Light | 04 AppBar docked + "before" overlay bug · 05 unlocked drag + snap guides · 07 auto-hidden / revealed |
| Settings — Dark / Light | 06a right-click menu · 06b settings window |

**Current mockup: `mockup/index.html`** (interactive; open in a browser). The Figma file predates the mascot/interaction update and is kept for reference only.

Mascots: 5 skins (Kiko anime girl, Miso cat, Bun bunny, Bolt robot, Ribbit frog) × 8 expressions + box form, defined once in `mascot/mascot.js`; `node mascot/gen.mjs` exports `mascot/*.svg`.

Font: Figma lacks Segoe UI Variable, so the mockup uses Inter; the app uses Segoe UI Variable.

## Tokens

### Color
| Token | Dark | Light |
|---|---|---|
| island.bg | `#000000` | `#FFFFFF` (+1px `#000` 7% stroke) |
| surface | `#1B1B24` | `#F4EFF5` |
| text | `#FFFFFF` | `#1C1C28` |
| text.muted | `#9A9AB2` | `#6B6B85` |
| accent | `#FF8FB1` | `#E86F96` |
| accent.text | `#FF8FB1` | `#B8436F` (contrast on white) |
| track | `#33333F` | `#E6DFEA` |
| line | `#30303D` | `#ECE6EE` |
| mascot | skin `#FFE9DC` · eyes `#2B2B4A` · pink `#FF8FB1` / `#E86F96` · star `#FFD166` | same |

### Size & radius
| Token | Value |
|---|---|
| collapsed | 180 × 36, radius 18 (h/2) |
| compact | 280 × 40, radius 20 |
| expanded | 520 × 180, radius 28 |
| card radius | 14–16 · chips 12 · inputs 6 · window 12 |
| AppBar strip | collapsed height + 6 px gap + margin = 48 px (physical px × DPI scale) |
| mascot in pill | 24 px |

### Spacing
4 · 8 · 10 · 12 · 16 · 24 (island padding: h/4 + 2 collapsed, 14 expanded)

### Typography (Segoe UI Variable in app)
| Role | Size / weight |
|---|---|
| Clock / pill label | 13 Semibold |
| Title (music) | 17 Semibold |
| Body | 12 Medium/Regular |
| Caption | 10–11 Regular |

### Shadow
Dark: `0 8 24 rgba(0,0,0,.45)` · Light: `0 8 24 rgba(0,0,0,.14)` · Hover glow: accent 40%, blur 16.

### Motion
| Interaction | Duration | Easing |
|---|---|---|
| Expand / collapse (w, h, radius together) | 320 ms | BackEase(Amplitude 0.35) EaseOut |
| Module content fade/slide | 180 ms, 60 ms stagger | CubicEase Out, 8 px slide |
| Hover scale 1.00 → 1.04 + glow | 150 ms | CubicEase Out |
| Blink | 120 ms, every 4–7 s random | linear |
| Idle bob | 2.4 s loop, ±1 px | SineEase InOut |
| Notification auto-expand | hold 3 s | — |
| Auto-collapse | after 4 s no hover | — |
| Snap to edge | 200 ms | CubicEase Out |
| Auto-hide slide + fade | 250 ms; re-hide after 800 ms | CubicEase In/Out |
| Mascot peek (edge hover) | 340 ms, "hi" bubble +180 ms | BackEase Out |
| Mascot hover → wow | blink 140 ms, then eyes 1.3× | — |
| Mascot click → squish + annoyed | 380 ms squish, hold 1.2 s | BackEase Out |
| 3 clicks < 900 ms → dizzy | wobble ±14°, 450 ms loop, 3 s | SineEase InOut |
| File drop → box gulp | 450 ms, message 1.9 s | CubicEase Out |

All skipped when `SystemParameters.ClientAreaAnimation` is false.
