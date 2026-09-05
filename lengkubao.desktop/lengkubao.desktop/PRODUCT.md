# Product

## Register

product

## Platform

web

## Users

Primary audience on the PC desktop app is the warehouse owner or manager: someone who oversees operations, reviews figures, manages fiscal years, pairs handheld devices, and triggers sync—not the person doing floor-level scanning and data entry.

Handheld Android users are front-line cold-storage operators (inbound, picking, on-site queries). They are a secondary audience served by the mobile app; the desktop home surface is optimized for managerial oversight, not operator throughput.

Typical PC context: office desk or back-office PC at the warehouse, often during business hours, multitasking between accounting, customer calls, and system administration.

## Product Purpose

冷库宝 desktop is the command center for cold-storage warehouse management on Windows: inventory, inbound and sales workflows, customer accounts, statistics, backup, and bidirectional sync with handheld terminals.

Success on the desktop home surface means the manager can see connection/sync status at a glance, pair a handheld quickly, switch fiscal year confidently, and reach core business modules without hunting through clutter.

Current design focus (scoped): refine the top console header only—the full-width bar with branding, pairing QR, pairing code, fiscal-year controls, and pairing actions.

## Positioning

The only desktop system a cold-storage boss needs to run the warehouse and keep handhelds in sync—clear, trustworthy, and fast to operate from a desk.

## Brand Personality

Professional console. Calm authority, operational clarity, no decorative noise. The interface should feel like a serious business tool (Stripe Dashboard, Linear, modern ERP shells), not a consumer app or marketing site.

Three words: 稳重、清晰、可信 (steady, clear, trustworthy).

## Anti-references

Crowded toolbars where critical actions (pairing, sync, fiscal year) compete for space with tiny hit targets. Avoid squeezing QR, pairing code, and four action buttons into one unreadable strip.

Also avoid: flashy gradients for their own sake, glassmorphism, oversized marketing hero typography on app chrome, and consumer-style illustration on operational surfaces.

## Design Principles

Practice what you preach: if sync and pairing are core jobs, the header must make connection state and pairing affordances obvious without reading docs.

Density with hierarchy: managers need many controls reachable from home, but the header groups them into brand | connect (QR + code) | fiscal | actions—not one flat row of equal-weight widgets.

Earned familiarity: WinForms controls styled to match a coherent console vocabulary (ghost buttons on dark chrome, card-style QR host, semantic greens/reds for sync state elsewhere).

Identity preservation: keep the established teal brand (#004c54 family); refine layout and scale, do not rebrand to generic AI palette.

Ship the scoped surface completely: header polish is done when QR is fully visible, pairing actions are comfortably clickable, and narrow widths degrade gracefully.

## Accessibility & Inclusion

Target WCAG 2.1 AA contrast on header text and controls (pairing code, button labels on dark teal). Minimum comfortable click targets (~32px height) on header actions.

Support Windows display scaling; layout must not clip QR or truncate pairing code at 125–150% DPI.

Respect reduced motion: no mandatory animations on the header; static layout first.
