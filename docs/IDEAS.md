# Conduit+ ideas backlog

Ideas beyond Conduit's feature set. These are for future reference and are not scheduled. They came out of the planning round (Claude, Sol, Astra) on 2026-11-05. Value and effort are rough calls.

| Idea | What it is | Value | Effort |
|---|---|---|---|
| **Data quality card** | Flags missing values, out-of-range values, mixed units and broken key links. Missing values are drawn as gaps, never as 0. | High | Low |
| **Recipes** | One action inserts a complete working dashboard (structural results, massing, daylight) with sensible defaults. | High | Low–medium |
| **Peek** | Drop it on any wire and the data gets an automatic chart in the viewport. It replaces hunting for Panels. | High | Medium |
| **Explain this value** | Click a metric to see its source component, units, aggregation and the geometry that contributed. | High | Medium |
| **Budgets** | Remaining allowance (floor area, carbon, deflection, utilisation), with a button that selects the members causing a breach. | High | Medium |
| **Presentation snapshots** | Camera, committed metrics, parameter values and definition version saved as one reproducible review state. | High | Medium |
| **Solve history** | Every Metric keeps a trend line of recent solves and a delta against the previous one. | Medium | Low |
| **Pinned option A/B compare** | Pin the current option, keep exploring, and see deltas against the pin. | High | Medium |
| **Gradient-aware Legend** | Reads colours and range straight from a GH Gradient wire. | Medium | Low |
| **Second screen** | The same runtime served on localhost, so a tablet or second monitor can show the dashboard or act as a presentation remote. | Medium | Medium |
| **Dashboard export** | A standalone HTML file with the data baked in, for reports and sharing. | Medium | Medium |
| **Vector PDF of the view** | Rhino's own `FilePdf` (vector model via `AddPage(ViewCaptureSettings)`) with the cards drawn on top as vectors through the same `ICanvas2D` code as the on-screen renderer. One drawing path, so the PDF can't drift from the screen. | High | Low–medium |
| **PDF report** | A multi-page report with paginated tables, notes and charts. HTML/CSS print layout via WebView2 `PrintToPdfAsync` is the natural fit; latency doesn't matter for export, so web's slow capture is irrelevant here. | High | Medium |

## Structural-results angle
Structural results are the first workflow, so these variants matter most:
- Utilisation bar and table cards sorted worst-first, with an "over 1.0" threshold band.
- A budget card for deflection and utilisation, with "select failing members" via `Link`.
- A legend that matches the colours of the analysis plugin's mesh results, e.g. Karamba.
- Explain-this-value from max deflection to the member, load case and node.
