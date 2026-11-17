# My Garage design tokens (direction B, "instrument cluster")

Source of truth: [tokens.css](../src/MyGarage.Web/wwwroot/css/tokens.css) (fonts in [fonts.css](../src/MyGarage.Web/wwwroot/css/fonts.css)). Preview with live contrast checks: [design-tokens-preview.html](design-tokens-preview.html). Screens built on these tokens: [mockups/](mockups/).

Status: approved direction. Nothing in `src/` uses these yet. This replaces the palette and shell described in [ui-standard.md](ui-standard.md) once ported.

## Rules

1. One design, two palettes. Dark mode changes colour values only. Layout, type, spacing, radius and motion are identical.
2. Theme is `data-bs-theme="light|dark"` on `<html>`, as `theme-init.js` already sets it (the mockups use `data-theme`; rename on port).
3. No hard-coded colours in components. Use `--mg-*` variables.
4. Status colours (`ok`, `due`, `bad`) mean status and nothing else. `info` is for charts only.
5. Numbers (odometer, money, dates, VIN) use the mono face with tabular figures. Amounts are right-aligned.
6. Panels get a border, not a shadow. Only floating things (menus, paper) get a shadow.

## Colour

| Token | Light | Dark | Use |
|---|---|---|---|
| `--mg-bg` | `#E6EBEF` | `#080B0E` | Page |
| `--mg-surface` | `#FFFFFF` | `#10151A` | Panels, forms |
| `--mg-surface-2` | `#F1F4F7` | `#171E25` | Inputs, table heads, inset blocks |
| `--mg-line` | `#D2DAE1` | `#222B34` | Dividers (decorative only) |
| `--mg-line-strong` | `#7E8B98` | `#5F6E7C` | Input and control edges |
| `--mg-text` | `#0E1318` | `#E8EEF3` | Body |
| `--mg-text-muted` | `#5A6672` | `#8895A2` | Secondary text |
| `--mg-accent` | `#BC7000` | `#FFB000` | Fills, rings, progress, focus |
| `--mg-accent-text` | `#8F5400` | `#FFB000` | Links and small amber text |
| `--mg-on-accent` | `#1A1204` | `#1A1204` | Text on amber buttons |
| `--mg-ok` | `#187A43` | `#3DDC84` | Upcoming, ready, credit |
| `--mg-due` | = accent | = accent | Due soon |
| `--mg-bad` | `#C23B30` | `#FF5B4D` | Overdue, failed, destructive |
| `--mg-info` | `#2B6CB0` | `#5AA9E6` | Chart series only |
| `--mg-rail-*` | dark in both | darker | Navigation rail |
| `--mg-paper*` | same in both | same | Invoices and window stickers |

Soft tints (`--mg-*-soft`) are derived with `color-mix`, so alerts and badges follow the palette automatically.

### Reminder status mapping

| `ReminderStatus` | Token | Treatment |
|---|---|---|
| Overdue | `--mg-bad` | Strip, full bar, "N days overdue" |
| DueSoon | `--mg-due` | Strip, partial bar with glow |
| Upcoming | `--mg-ok` | Strip, partial bar |
| MileageNeeded | `--mg-text-muted` | Dashed bar |
| Paused / Archived | `--mg-line` | Row at 60% / 50% opacity |

## Typography

| Role | Family | Size / weight | Class |
|---|---|---|---|
| Page title | Chakra Petch | 38 / 700, uppercase, +0.02em | `.mg-title` |
| Panel heading | Chakra Petch | 18 / 700, uppercase | `.mg-heading` |
| Eyebrow | JetBrains Mono | 11 / 500, uppercase, +0.18em, accent-text | `.mg-eyebrow` |
| Field label | JetBrains Mono | 10 / 600, uppercase, +0.14em, muted | `.mg-label` |
| Body | Inter | 14 / 400 | `.mg-body` |
| Data | JetBrains Mono | 14 / 500, tabular | `.mg-data` |
| Amount | JetBrains Mono | 16 / 500, tabular, right | `.mg-amount` |
| Readout | JetBrains Mono | 40 / 700, tabular | `.mg-readout` |
| Total | JetBrains Mono | 26 / 700, accent-text | `.mg-total` |
| Statement | Chakra Petch | 56 / 700 | About page only |

Delivery: self-host WOFF2 (all three families are OFL). Chakra Petch 500/600/700 static; Inter and JetBrains Mono variable. Preload Chakra Petch 700 and Inter 400, `font-display: swap`. The mockups load Google Fonts for convenience only; production should not.

Body text drops from Bootstrap's 16px to 14px. Keep form inputs and touch targets at 16px on phones to avoid iOS zoom (a one-line override in the port).

## Spacing, radius, layout, motion

- Spacing: 4 · 8 · 12 · 16 · 24 · 32 · 48 px (`--mg-space-1..7`).
- Radius: 6 chips · 8 inputs and buttons · 12 rows and menus · 14 panels · pill.
- Layout: 68px rail, 1240px content max, 32px page padding (24px vertical). Under 768px the rail becomes a hamburger drawer.
- Motion: 150ms hover/focus, 300ms bar and ring fills, odometer roll 1.4 to 2.2s, scan line 2.2s, ease `cubic-bezier(.2,.8,.2,1)`. Everything is disabled under `prefers-reduced-motion`.

## Accessibility (measured in the preview page, WCAG 2.x)

All text pairs pass 4.5:1 in both themes; UI parts pass 3:1. Lowest passing values: light muted on page 4.89, light on-accent on accent 4.82, light accent fill on page 3.21, light line-strong on surface-2 3.15, dark line-strong on surface-2 3.21.

Two corrections made while checking the mockups' original values:

- The mockups used `#C77900` amber for small text on light surfaces (3.4:1, fails). Fixed by splitting the accent: `--mg-accent` for fills and `--mg-accent-text` for text. The fill itself was darkened to `#BC7000` because the old value was 2.84:1 against the page background.
- Ordinary borders are only 1.4:1 (fine for dividers, too faint for inputs), so form controls use `--mg-line-strong`. The mockups' inputs should switch to it on port.

## Migration from today's tokens

| Current (`app.css`) | New |
|---|---|
| `--mg-page-bg` | `--mg-bg` |
| `--bs-body-bg` | `--mg-surface` (bridged) |
| `--mg-sidebar-bg`, `--mg-sidebar-*` | `--mg-rail-*` |
| `--mg-navbar-bg`, `--mg-navbar-height` | removed (no top bar; `--mg-pubtop-height` for signed-out pages) |
| `--mg-primary`, `--mg-primary-hover/active` | `--mg-accent` (hover and active derived in `.btn-primary`) |
| `--mg-primary-text` | `--mg-accent-text` |
| `--mg-secondary`, `--mg-secondary-strong` | `--mg-text-muted`, `--mg-text` |
| `--mg-accent` (old amber) | `--mg-accent` (new value) |
| `--mg-card-radius`, `--mg-card-shadow` | `--mg-radius-xl`, border only |
| `--mg-chart-1..7`, `--mg-chart-other` | `--mg-chart-fuel/charging/service/other` |
| `--mg-mark-bg`, `--mg-mark-text` | `--mg-mark-bg` (text inherits) |
| `--mg-notfound-*` | removed |
| `--mg-destructive-*`, `--mg-danger-text` | `--mg-bad` |

## Decisions

1. Light-mode accent text stays `#8F5400` (the brownish amber). Legibility wins over matching the dark-mode `#FFB000`.
2. Body text is 14px. Form inputs and touch targets stay 16px on phones to avoid iOS zoom.
3. Fonts are self-hosted (WOFF2, OFL), never loaded from a CDN.
4. Theme options are Light and Dark only. There is no System option; the first visit picks the device's setting, and the choice is then kept in the browser (`localStorage` key `mg-theme`), as today.

## Still open

- Dark-mode `--mg-line` (1.28:1) is quiet by design; raise it if panels feel unseparated on real screens.
