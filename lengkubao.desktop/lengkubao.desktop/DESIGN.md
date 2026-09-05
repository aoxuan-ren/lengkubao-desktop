# Design System — 冷库宝 Desktop (WinForms)

Captured from existing `Form1.cs` tokens and header console work. Scoped to product register; primary surface is the desktop home header and dashboard shell.

## Color

### Brand

| Token | Value | Usage |
|-------|-------|-------|
| primary-dark | `#004C54` (rgb 0, 76, 84) | Header background, primary buttons |
| primary | `#005A64` (rgb 0, 90, 100) | Secondary emphasis, sync CTA |
| header-gradient-bottom | `#003A42` (rgb 0, 58, 66) | Header vertical gradient end |
| header-accent-line | `#008C9B` (rgb 0, 140, 155) | Header bottom accent rule |

### Surfaces

| Token | Value | Usage |
|-------|-------|-------|
| surface | `#F2F6F9` | App background |
| card | `#FFFFFF` | Cards, side panels |
| section | `#F7F9FB` | Section backgrounds |
| core-secondary | `#F8F9FB` | Secondary function tiles |

### Ink & borders

| Token | Value | Usage |
|-------|-------|-------|
| muted | `#5C6B7A` | Hints, secondary labels |
| border | `#CED8E3` | Card borders |
| header-ghost-fill | `rgba(255,255,255,0.14)` | Ghost buttons on dark header |
| header-ghost-border | `rgba(255,255,255,0.35)` | Ghost button borders |
| pairing-pill-fill | `rgba(255,255,255,0.16)` | Pairing code capsule on header |

### Semantic

| Token | Value | Usage |
|-------|-------|-------|
| success | `#2D7D52` | Running / online |
| danger | `#A94442` | Stop / error |
| list-online | `#256E55` | Device online |
| list-offline | `#969EA8` | Device offline |

## Typography

- Family: `Microsoft YaHei UI` → `Segoe UI` → system sans (see `UiFontFamily` resolver).
- Header title: 17pt bold, white.
- Header subtitle: 9pt, `#AFCED4`.
- Pairing code (header): 26–32pt bold (scale with header height).
- Header actions: 10pt on ghost buttons.
- QR hint: 8pt, muted on white QR card.
- Body/dashboard: 9–10.5pt per existing dashboard buttons.

## Layout

- Header height: `112px` (`HeaderConsoleHeight`), full viewport width, flush to top edge.
- Content below header: 10px side margin, 10px gap under header.
- Header zones (left → right): brand block | centered QR card | pairing pill | fiscal cluster | ghost action buttons | username (wide screens only).
- QR card: white rounded rect (8px radius), QR up to 96px with hint below inside card.
- Minimum header control height: 32px; action buttons 58×32px target.

## Components

### Console header (`headerPanel`)

- Background: vertical gradient `primary-dark` → `header-gradient-bottom`, 2px accent line at bottom.
- QR host (`panelQrHost`): white card, centered horizontally in header.
- Pairing pill (`panelPairingPill`): semi-transparent rounded capsule, label + large code.
- Ghost buttons: flat, light border, white text, hover brighten—used for 更改/复制/随机/广播 and fiscal 保存/新建.
- Fiscal controls hidden below ~1020px width to prevent crowding.

### Dashboard tiles (existing)

- Primary tiles: `primary-dark` fill, white text.
- Secondary tiles: `core-secondary` fill, `primary-dark` text, 1px `border`.

## Motion

- Reduced motion first: no required header animations.
- Hover states on buttons only (background brighten).

## Notes

- Stack: .NET Framework 4.7.2 WinForms—not a web app; impeccable `live` mode does not apply.
- Re-run `/impeccable document` after major token changes to refresh this file.
