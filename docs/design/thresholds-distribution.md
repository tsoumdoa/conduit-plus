# Colour Scale thresholds, range modes, palettes and Distribution card: design, 2026-11-24

Basis: main 0708850 (0.2.0-beta.33). Reviewed by Astra (gpt-6.2-astra) on 2026-11-24; its 8 findings are folded in. The owner agreed the scope using an interactive mock: [Thresholds and Distribution](https://claude.ai/artifact/EmyoXpaG7V6cRNp9yfrDvk).
The work ships as three small stacked PRs (see Delivery). Each comes with a Yak package to try.

## 1. Range modes (PR 1)

Add a saved, undoable **Range** section to Colour Scale's right-click menu. It sets how an *unset* end of the range is found:

| Mode | Unset end becomes |
|---|---|
| **Auto (rounded)**, the default and today's behaviour | the nice floor or ceiling from `ChartTicks.Nice`, unchanged |
| **Exact data** | the true finite min or max, not rounded |
| **Clip outliers** | the 1st/99th, 2nd/98th or 5th/95th percentile of the finite values. **Clip at** submenu; 2% is the default |

- A wired Min or Max still sets its own end. The mode applies only to the end left unset, as the auto bounds do today.
- The Auto code path stays exactly as it is.
- **Exact and Clip around explicit controls.** Each mode first works out a provisional bound for each unset end. The data-only extent is then widened to include any explicit Min, Max or Mid, as Auto does now (`ColourScale.cs:63`).
  - An unset end that would equal or cross an explicit Mid, or an explicit opposite end, moves outward by 1% of the resulting span. If the span is zero, it moves by `BitIncrement`/`BitDecrement`.
  - The existing errors for contradictions between explicit controls stay as they are.
- **Percentiles** use linear interpolation between closest ranks (Hyndman–Fan type 7) over the finite values only. Missing values don't count.
- **Collapsed clip.** Non-constant data can give equal percentiles, for example 99 zeros and one high value. That range is shown as constant at that value, using today's constant-range path. The outlier counts are kept.
- Clipped values use Below/Above when those are set, otherwise the end colours. The existing "n of m values are outside…" remark reports them.
- Persist the mode and the Clip at percentile with the component, as the Show counts menu item is persisted (`ColourComponents.cs:154`). An archive that has neither setting (any file saved before this change) opens in Auto with 2%, so its colours don't change.
- Tests: Exact with Mid outside the data; Exact with a wired Min above the data maximum; a collapsed Clip; an archive with neither setting.

Rejected: a Range input. The owner asked for the menu, and Min/Max already cover fixed ranges. A free percentile input: three menu choices cover the use cases.

### Palettes (also PR 1)
Add these 13 ordered palettes, five stops each. They are named by how they look, never after a firm. Each one also gets the usual "reversed" entry automatically.

| Group | Name | Kind | Stops |
|---|---|---|---|
| Perceptual | Viridis | Sequential | #440154 #3B528B #21918C #5EC962 #FDE725 |
| | Magma | Sequential | #000004 #51127C #B73779 #FC8961 #FCFDBF |
| | Cividis | Sequential | #00204D #414D6B #7C7B78 #BCAF6F #FFEA46 |
| | Greys | Sequential | #F2F2F2 #BDBDBD #8A8A8A #525252 #1F1F1F |
| | Greens | Sequential | #E5F5E0 #A1D99B #5DB66D #238B45 #00441B |
| Diverging | Red–Yellow–Green | Diverging | #A50026 #F46D43 #FFFFBF #66BD63 #006837 |
| | Brown–Teal | Diverging | #8C510A #D8B365 #F5F5F5 #5AB4AC #01665E |
| | Orange–Purple | Diverging | #B35806 #F1A340 #F7F7F7 #998EC3 #542788 |
| | Spectral | Diverging | #9E0142 #F46D43 #FFFFBF #66C2A5 #5E4FA2 |
| Analysis-style | Daylight | Sequential | #1B1F4B #2D5DA8 #3FB0C9 #F2E35B #FFF8E0 |
| | Solar | Sequential | #2A0B4F #7B1E7A #D9483B #F7A12B #FCF4A3 |
| | Comfort | Diverging | #2B5DA8 #8FC1E3 #F2F0E6 #F2A36B #B8322A |
| | Wind | Sequential | #F4F8FB #A9D3E8 #4F9BC9 #2A5A9E #3B2A6B |

- Keep the existing palettes and their order. The new ones go after them, grouped in the Palette input's right-click menu as above.
- Name lookup stays case-insensitive. A single string is matched against palette names *before* named colours (`ColourComponents.cs:37`), so a new palette must never share a `System.Drawing` colour name. That rules out `Green`, which is why the ramp is called `Greens`.
  - Add a test that no palette name, reversed names included, is a known colour, apart from the existing `Blue` and `Teal`, which already take precedence today.
  - Test the real input parser with: a single `Green` (still the colour #008000); a custom list containing `Green`; and the single string `Greens` (the palette).
- Comfort is Diverging, so Mid defaults to zero when the range spans zero, like Blue–Red.

## 2. Thresholds (PR 2)

Add three optional inputs to Colour Scale, after Above:

| Input | Type | Meaning |
|---|---|---|
| **Thresholds** (Th) | number list | Threshold values |
| **Threshold colours** (TC) | colour list | Required when there are N ≥ 1 thresholds: either 1 colour (used for every threshold) or N, one per threshold |
| **Threshold sides** (TS) | text list, right-click options `Above` / `Below` | 0 entries (every threshold is Above), 1 entry (used for every threshold) or N |

- `Above` means value ≥ t and `Below` means value ≤ t. A value exactly at the threshold is caught by it.
- Where thresholds overlap, the outermost one wins: the highest matching `Above`, otherwise the lowest matching `Below`. So ≥0.9 amber plus ≥1.0 red gives stepped colours beyond the target.
- If a value meets both an Above and a Below threshold (the two overlap), Above wins and a warning names the overlap.
- Order of precedence per value: missing grey, then thresholds, then Below/Above, then the gradient. The gradient itself does not change: thresholds recolour values, they don't move the range.
- The winner never depends on input order.
- Errors:
  - non-finite thresholds;
  - no colours, or a colour count other than 1 or N, when there are thresholds;
  - a side count other than 0, 1 or N;
  - unknown side text.
- Colours or sides wired with no Thresholds give a warning ("Threshold colours has no Thresholds") and are ignored.
- A duplicate value on the same side gives a warning, and the first one is kept.
- A threshold outside the range is allowed (see Legend).
- Core resolves the inputs **once** into an immutable `ThresholdSet` of (value, colour, side) entries. It is sorted and checked, and `WinnerFor(value)` gives a value's one winning threshold. Colour Scale, the Legend painter and Distribution all use that same set.

**Legend.** Add `Thresholds` to `CardSpec`: the resolved set, plus a count for each entry. Each finite value is counted once, against its single winner. The gradient Legend painter (GDI and PDF) works like this:
- **Bands.** Bands come from the *winning intervals* intersected with the domain the legend shows, not from every mark. Examples:
  - With ≥ 0.9 and ≥ 1.0, the bar shows [0.9, 1.0) in amber and [1.0, Max] in red.
  - An Above threshold higher than Max gets no band.
  - Where Above and Below overlap, the band shows Above's colour.
- **Marks off the scale.** A threshold outside the range gets a small caret just past that end of the bar, with its label and count. It gets no band.
- **Constant range.** Constant data shows one swatch. Its colour is the winning threshold's colour if the constant value is caught, otherwise the mid colour. The marks are listed beside it.
- **Labels.** Each tick gets a label such as `≥ 24 · 7` or `≤ 6 · 3` (side, value, number of values caught), in theme ink.
  - Above labels run right from the tick and Below labels run left. Near an end, a label flips inwards.
  - When two labels would overlap, the second moves to a second row above the bar, and the card grows by that row. Labels are never clipped, because PDF has no tooltip to recover them.
  - On screen, the tooltip also shows the full-precision value.

A Legend wired by hand (the Legend component) has no thresholds; it does not get these inputs in this PR.

**Colour mapping (blocker fix, in this PR).** The Legend's stops are sampled and repositioned, so close knots can collapse onto one position. That means the evaluator cannot be rebuilt from them. So Colour Scale's Legend also carries an immutable `ColourMapping` on `CardSpec.Mapping`, holding:
- the original palette stops (in reversed order if the palette is reversed);
- Min and Max;
- the resolved Mid;
- Below and Above;
- the `ThresholdSet`.

`ColourScaleResult.ColourAt` delegates to `ColourMapping.ColourAt`. Distribution later calls the same method, so there is one evaluator.

Mapping and Thresholds are part of the card's snapshot ownership and value equality (`Cards.cs:303`), so changing either repaints the card. Tests check exact RGB equality between `ColourScale.Create` colours and `Mapping.ColourAt` for:
- an even-stop custom palette;
- an extreme Mid;
- a constant range;
- every new palette.

Remarks: one line per threshold, e.g. `≥ 24: 7 of 48 values use the threshold colour`. The existing out-of-range remark counts only values that no threshold catches.

Rejected: one Low and one High threshold, because it caps at two and can't step. Encoding the side in text such as `>=30`, because it is fragile to type and its validation is less clear. Deriving the side from Mid, because the behaviour would be surprising.

## 3. Distribution card (PR 3)

New **Distribution** component (Cards category, new stable GUID, new icon) and `CardKind.Distribution`.

**Inputs, in order:**
1. **Values**: number list.
2. **Labels**: optional; default 1…n.
3. **Keys**: optional unique stable keys; default labels.
4. **Title**: default "Distribution".
5. **Unit**: optional.
6. **Slot**: right-click option, as on the other cards.
7. **Scale**: optional; takes Colour Scale's Legend output.
8. **Colors**: optional per-value colours.
9. **Thresholds**, **Threshold colours**, **Threshold sides**: same rules as Colour Scale.
10. **Show as**: `Dots` / `Histogram`, right-click options.
11. **Bin width**: optional positive number; unset means Auto.

The only output is **Card**. No Members input: each value is one member, and a bin selects its members as a group.

**Scale.** Scale reads `CardSpec.Mapping` from Colour Scale's Legend (added in PR 2) and colours each value with `Mapping.ColourAt`, the same evaluator Colour Scale uses.
- Any Legend without a `Mapping` on Scale is an error: "Scale needs a Legend from Colour Scale". That includes a categorical Legend and a gradient built by hand with the Legend component.
- **Effective thresholds:** wired Thresholds inputs, otherwise the Scale's `ThresholdSet`, otherwise none. Replacing them replaces both the threshold fills and the annotations; nothing from the Scale's thresholds carries over.
- **Fills:** wired per-value **Colors** are the final fills when present. Otherwise the Scale's mapping applies, and when wired Thresholds replace the Scale's, the winner's colour takes precedence over the Scale gradient. With nothing wired, fills use the theme's neutral `Data` colour.
- The effective thresholds always draw their lines and labels, whatever the fills are.
- **Lengths:** Colors, Labels and Keys are checked against the *original* Values count, before missing values are dropped. Missing values are dropped together with their colour, label and key.

**Layout.**
- **X domain.** The x domain always comes from the finite observations, outliers included. It never comes from the Scale's range: a clipped Scale must not hide dots or put them at the wrong x. It is widened to bin edges and to any threshold the axis shows. An off-domain threshold gets an edge caret, as on the legend.
- **Empty and constant data.** With no finite values the card shows "No values" and an empty axis, and still has a valid size. With constant data there is one bin centred on the value (width = Bin width, or 1 under Auto), and the dots form a single column.
- **Bins.** Bins are aligned to multiples of the bin width. Auto width is the nice step (1, 2, 2.5 or 5 × 10^k) of range / 10. The last bin includes its upper edge.
  - Store only *occupied* bins, sparsely, keyed by a `long` index computed with checked arithmetic.
  - If span / width exceeds 10,000 or can't be represented, warn "Bin width too small for this range; using Auto" and use Auto.
- Tick labels use the existing chart tick and number formatting.
- **Dots.** One dot per finite value, at its exact x. Dots are stacked upward greedily, so no two in a row overlap. The diameter starts at 10 px and shrinks by 15% steps until the tallest stack fits, with a 3 px minimum.
- **Histogram.** One bar per bin. Each bar is a stack of slices, one per value, sorted by value and each in that value's colour. So Dots → Histogram keeps both the colours and the counts. With the neutral colour the slices merge into a solid bar, with no seams.
- **Too many for dots.** When there are more than 400 finite values, or the dots would need less than 3 px, the card shows Histogram whatever the chosen mode. It adds the remark "Too many values for dots (n); showing histogram". The chosen mode comes back when the count drops.
- **Optional extras.** A median and quartiles strip under the axis (shown by default). Threshold lines, dashed in the threshold colour, with a light tint beyond them and a `≥ t` label (shown by default). Both are saved, undoable menu toggles.
- **Missing values** are left out, with the remark "n missing values not shown". There are never zeros in their place.

**Switch and state.** The effective mode is decided in this order, first match wins:
1. Forced histogram (too many values).
2. A wired **Show as** input.
3. The display's local choice.
4. The component default.

- The right-click **Show as** menu sets the component default, saved and undoable.
- **Header switch.** A small Dots | Histogram switch sits in a reserved header hit region, the same way table sorting reserves header hits (`TableSort.cs:15`). It writes the display's local choice, as view state on that display's card copy. The choice lasts for the session and doesn't trigger a GH solve.
- **Changing the menu default** clears the local choice on every display.
- **Wiring Show as** hides the switch but keeps the local choice, which comes back if Show as is disconnected.
- **Forced histogram** shows the switch disabled, with the remark as its tooltip.
- **Animation.** On Overlay the change is animated (about 450 ms; reduced-motion settings skip it). On Canvas Card it is immediate.
  - During the animation, hit-testing and tooltips use the *destination* layout only.
  - Captures always use the settled effective mode, never a frame from the middle of the animation.
- Test: two displays of the same card with different local choices; changing the menu default; wiring and then disconnecting Show as.

**Interaction**, using the existing brush and group rules (`Brushing.cs:21`):
- Hit-testing is pure Core logic. It runs against the layout that was actually displayed, and that same layout supplies the dot hit-tests and the tooltips.
- **Dots.** Hovering a dot shows its label and value. Clicking a dot selects that member.
- **Bars.** Hovering a bar shows its range, count and percentage, and highlights the bin.
  - A bar hit resolves to the bin's *original member keys*, never to a bin index or a synthetic key. That way it can't collide with a member key, and it can't change meaning after rebinning.
  - The modifier (none, Shift, Ctrl or Ctrl+Shift) is applied once to the whole set.
  - Whether a bar is fully or partly selected is worked out from those members. A partly selected bar shows the existing partial dash.
- Activation today carries one string key (`CardInteractionController.cs:7`). Extend it to carry a member set, or resolve the set from the layout revision when the click is released.
- A click counts only if the press and the release hit the same layout revision.
- Selected dots and slices get the accent stroke.
- Clicking empty plot space clears the selection, as on other cards.
- Tests: rebinning while a selection is active; clicking a partly selected bin with each modifier.

**Cost.**
- Cache geometry per (card revision, display, effective mode, measured plot bounds).
- Hover and selection repaint the card but never re-lay it out, in line with the PR #52 big-table budget.
- Benchmark 2,000 values with: Histogram and Dots; a tiny bin width; many coincident values; warm hover; and selection. Record layout-build counts (warm hover and selection must build 0 layouts), and record layout and paint time separately against the PR #52 repaint budget.

Rejected: an index-vs-value scatter (the owner chose the dot stack). Dots centred in their bin column, because exact x shows more and still turns into the same bins. A Members input, because nothing needed it.

## Delivery

1. **Range modes and palettes.** Core: the bound modes and the quantile, plus palette entries and name tests. GH: the menu, its persistence, and Auto for older files.
2. **Thresholds.** Core: `ThresholdSet` (matching, precedence, validation and counts) and `ColourMapping`, with `ColourAt` delegating to it. Also `CardSpec.Thresholds` and `CardSpec.Mapping`, with their snapshot and equality. Painter: legend bands, carets, label rows and the constant swatch in every theme, and PDF.
3. **Distribution.** Core: sparse bins, layout, the forced histogram and mode precedence, hit-testing that resolves to member sets, and the Scale input reading `Mapping`. Painter in every theme, the component and its icon, and the Overlay and Canvas Card interaction. Add a distribution of room areas to `showcase.gh`.

Later, separately: several thresholds with colours on Bar and Line, using the same three-list inputs.

Tests stay free of Rhino. Live checks for each PR: the Yak package, the menu modes, the palettes on room areas, a ≥ and ≤ threshold pair, the switch on the card in the viewport and on Canvas Card, group selection from a bar, and a capture.
