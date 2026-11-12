# Group keys — design, 2026-11-07

Basis: main 2e10e9c (Canvas Card #15), link/layout/canvas designs and PR #14's architectural UX notes. Grouping must work without geometry and identically on both display surfaces.

## Supply and identity

Append optional **Members** (text tree) to Bar and categorical Legend. Branch i, in GH path order, supplies member keys for entry i; require exactly one branch per entry, preserve empty branches, deduplicate members ordinally, and reject blank/reserved keys with path/item diagnostics. An unwired input preserves today's individual-key behavior. A wired empty tree with nonzero entries is an error. Group mode requires explicit existing Keys, avoiding label-derived identities. Gradient Legend rejects Members. Append optional **Key** (text item) and **Members** (text tree, exactly one branch) to Metric: Key alone targets one member; Key plus Members targets a group; Members without Key errors. Append after PR #14's Decimals/Colors inputs when integrated; retain all existing GUIDs and positions.

Store immutable group definitions on CardSpec, keyed by entry key; no new Group component. Examples: `use:residential -> [B01,B03]`, `type:bedroom -> [R02,R04]`, `below-minimum -> [R01,…]`. Metric's body below the title becomes its data hit region; title drag and Clear retain priority. Its numeric value remains supplied data, never recomputed from membership.

Rejected: Link encoding makes groups geometry-dependent; a separate mapping component adds wiring for the common single-card case; delimiter-encoded keys lose validation and ordinary GH tree editing.

Use one ordinal, case-sensitive namespace per brush domain. A group key must not also occur as an individual card/Link key or member. No nested groups. Repeated definitions of a group must have identical member sets (order ignored); identical group definitions on different cards are harmless. Connecting the same card twice to one Overlay reports the duplicate index and retains its first occurrence. Conflicts disable only the conflicting group targets and raise diagnostics on contributing displays; valid individual keys remain usable. Do not silently union inconsistent definitions. Membership is authoritative even when a member has no visible card or Link. A zero-member group is inert, with “0 members” feedback.

Rejected: recursive groups require cycle rules; silent unions conceal inconsistent analysis results; automatic prefixes change existing key semantics.

## Brush contract

BrushState owns a canonical set S of selected **member keys**, never group tokens. Resolve a target to M: singleton for an individual, membership set for a group. Plain click replaces S with M. Shift adds M, Ctrl removes M, and Ctrl+Shift toggles M (remove when all members are selected, otherwise add). Repeating a plain click is a no-op. These rules apply to individual members and groups on both surfaces. Escape cancels gestures or closes overflow UI; it does not change selection. Clear empties the whole brush domain. No selection provenance or sticky “select future members” intent is retained.

Selection outputs are sorted, unique member keys. Keep Events' existing owner/cardId/key/kind fields: key is the clicked group or member target, kind remains click/clear. Add targetKind, sorted affectedKeys (actual changed members), and selectedKeys (post-action snapshot); retain the 32-event bound. One accepted group action produces one event, never one event per member. Empty groups produce none. Pruning produces no click event.

Rejected: storing group tokens makes downstream geometry filtering ambiguous; retaining overlapping selection provenance makes deselection surprising.

Hover stores the original target plus origin and resolves an effective member set H. Hovering a group brushes all its members; hovering a member marks every containing group. Do not recursively expand those related groups: hovering one Bedroom must not hover all Bedrooms. Group projection carries hoveredMemberCount/total and selectedMemberCount/total, plus direct-target status. Preserve HoverKey for the originating target where applicable, but replace single-key painting/highlighting assumptions with effective sets. Clear controls still appear when any selection exists in the domain, including on a keyed Metric.

On data replacement rebuild the live universe from individual card keys, Link keys and valid group members, then intersect S once. Newly added group members are not automatically selected. A member removed from a group stays selected if another card, Link or group still declares it. Re-resolve existing hover against new membership; clear it if its target disappears. For shared displays, reconcile the union atomically as specified in selection-scope.md.

Rejected: pruning against one card loses other cards' selections; storing expanded hover permanently leaves stale highlights after data changes.

## Paint, cost and acceptance

Use existing theme accent roles: effective hover gets a thin outline; directly hovered target also gets a light background; selected members/full groups get the existing strong selection treatment. Partial groups get an accent dash marker and tooltip “n of m selected”; related hover groups get a thin outline and “n of m hovered.” Keep category swatch/bar colors visible. Selection wins the fill/weight while hover remains an outline, so mixed states are readable without relying on color alone. Member tooltips list containing group labels (deduplicated, capped at five plus a remainder count); group tooltips show member and selection counts. Geometry receives only H and S, using existing thin/thick wires and PR #14's face fills. Captures omit all hover-derived fields, retaining selection.

Rejected: painting partial groups as fully selected misstates selection; recoloring category swatches destroys the legend's meaning.

Core owns validation, membership/reverse indexes, set operations, projections and tooltip metadata; hosts own input and geometry. Rebuild indexes only on membership revision; hover costs O(|H| plus touched membership edges), never all-pairs key scans. Cache hash sets/counts and avoid repeated LINQ Contains per geometry item. CardStore still invalidates only changed projected cards. Benchmark 2,000 members with overlapping groups, recording Core projection separately from raster and geometry costs; target Core interaction p95 below 5 ms on the existing benchmark machine, not a claimed end-to-end latency.

Test ordinary-key regression, empty/duplicate/conflicting definitions, reserved keys, Replace/Add/Remove/Toggle modifiers for overlapping groups, partial/full states, nonrecursive hover, pruning and deterministic outputs in Rhino-free tests. Verify Metric hit/title/Clear priority and all themes through painter tests; live-check Bedroom, Residential and below-minimum examples in both hosts, including captures and sorted tables. For PR #14 row reveal, reveal only the first newly selected row in display order once per group action, never repeatedly scroll through members.

## Delivery

PR A1: Core definitions/index/BrushState contract and tests. PR A2: GH inputs, painter/hits/tooltips, Rhino effective-key highlights and example wiring. Merge PR #14 first or explicitly rebase its appended inputs and face/reveal behavior. Scope work may proceed in parallel against A1's agreed contract; shared files are Linking.cs, Cards.cs, CanvasCards.cs, Components.cs, OverlayManager.cs and OverlaySession.cs. Assign one integrator for these; do not independently rewrite BrushState. See selection-scope.md for the remaining split. Design only: no build/install or commit required.
