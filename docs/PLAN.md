# Conduit+ plan (v2, 2026-11-05)

Revised after research (Sol, GPT-6.1 Sol) and review (Astra, GPT-6 Astra).
Interactive mocks plus the full write-up: `docs/concept/index.html`.

## Goal
A modern successor to Proving Ground's Conduit (MIT, Dave Stasiuk; ~8.8k LOC C#, 19 live components, ported to Rhino 8 / .NET 8 in July 2025). HUD cards are written with web tech, placed by direct manipulation, and talk back to Grasshopper.

## Principles
1. Each card needs its data and one Overlay; everything else has a default.
2. Precedence is always: wired input > manual edit in viewport > automatic.
3. Two-way: Overlay outputs Selection/Events. Inputs bind to picked sliders (not nicknames), commit on release, one undo step.
4. `Link` (geometry + keys) makes charts ↔ geometry brushing and picking explicit.
5. Paper (opaque) theme by default; no fake blur over Rhino pixels.
6. Progressive power: built-in cards → Vega-Lite Spec → custom card SDK.

## Architecture
- The **card spec** is the product. Renderers are swappable behind it.
- Primary candidate: web renders each card offscreen → cached bitmap + hit regions + revision (published atomically) → DisplayConduit composites per frame. Fallback: native DisplayPipeline 2D for pilot cards.
- No transparent overlay window. Rich editing and accessible controls live in a docked Inspector panel (Eto WebView).
- Three update rates: per solve (dirty cards), per interaction (one card, no solve), per frame (reposition only).
- Foundation: WebView2 UI-thread ownership, doc/view lifecycle, undo transactions, pending intent outside ScheduleSolution, explicit data semantics (nulls, negatives, units, ranges).

## Week 1–2 gate (web renderer passes if)
- Card change → pixels ≤ 50 ms p95; hover ≤ 33 ms p95 (240×180 card, 150% DPI)
- Correct alpha and crisp text at 100/150/200% scaling
- Appears in ViewCaptureToFile at 2×/4× via re-render at target scale
- 10 cards × 4 viewports cost ≤ 5% orbit fps; no focus stealing
- Recovers from browser-process crash via host snapshot

## Phases (1 experienced C# + TS dev, full-time)
| Phase | Scope | Effort |
|---|---|---|
| Gate | Web-to-bitmap vs native spikes on Rhino 8.20+ Windows; decision memo | 1–2 wk |
| Pilot | Overlay, Metric, Bar, Text, Legend, Link, Theme; drag-to-slot; keyed selection; PNG capture; Yak; 3–5 users | 4–5 wk |
| v1 interactive | Input + bindings/undo, brushing, Pins, Edit layout (resize/undo/overflow), Inspector, Line/Table/Histogram/Scatter, Peek, Quality, Recipes, multi-view/DPI | 10–12 wk |
| v1.x reach | Hi-res capture parity, presentation snapshots, HTML dashboard export, Spec, Custom card SDK + docs, A/B compare, second screen | 8–12 wk |
| macOS / GH2 | WKWebView renderer + packaging; GH2 adapter | 6–10 wk / 3–5 wk |

**Totals:** pilot ~6 wk · interactive Windows v1 ~4–5 months · full Windows scope ~6–8 months · macOS +1.5–2.5 months.

## Decisions (2026-11-05)
- **Decision: Raster renderer.**
- **Renderer:** build both web and native in the spike and pick from measurements.
- **Platform:** Windows-only for the pilot and v1 (Rhino 8.20+; dev machine has Rhino 8.36).
- **ViewCapture:** wanted in the pilot if feasible, so it's part of the gate.
- **Licence:** MIT.
- **First workflow:** structural results (metrics, utilisation bars, result tables).
- **Pilot user:** the owner.
- Bold ideas are recorded in `docs/IDEAS.md`.
