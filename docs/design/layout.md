Superseded by "Drag editing removed" in [viewport-layout.md](viewport-layout.md#drag-editing-removed).

# Edit layout (design, 2026-11-06)

Reviewed specification by Astra (GPT-6 Astra).

- Plain left title/grip press becomes a drag after >4 CSS px. Clear/data/scroll controls retain their behavior.
  Untitled cards use an explicit edge grip, outside body text. Wired titles show a lock and explain their authority.
- One mouse state: idle, pending title, card drag or scrollbar drag. Swallowed-up bookkeeping survives cancellation.
  Cancel on view exit, release outside, session/data changes, size/DPI changes, commands or Escape on mouse move.
- Freeze owner-local placements; draw a dashed source, six labelled anchor zones, insertion marker and one owned
  85% alpha ghost bitmap. No per-move rasterization or reflow. View captures show committed cards only.
- Core resolves slots before sorting: wired > manual > original input-index auto. Ranks apply only in OrderSlot;
  ranked cards precede unranked, ties retain input order. Return placements in input order. Drops remove first,
  insert against frozen destination midpoints and batch-normalize destination ranks, including wired neighbours.
- GH Overlay owns a replaceable dictionary of named LayoutOverride records (manual slot, order slot, rank).
  Manager mirrors it per document/owner and keeps original specs; sessions own only gesture graphics. Synchronous
  batch proposals record one custom undo action and update the bound session before removing the ghost.
  Queued publication applies latest overrides at execution; editing never schedules a solve or follows another view.
- Save versioned JSON under ConduitPlus.Layout, validate entries with a load warning, retain absent-card entries.
  Reset layout clears all entries through the same undo/apply path. Demo keeps one wired card.
- Verify pinned GH 8.20 undo/serialization APIs, Core ordering/JSON/geometry tests, Debug build and full tests.
  Interactive capture, input cancellation, undo and multi-view/DPI behavior require Rhino validation.

API check: pinned 8.20 XML/assembly confirms RecordUndoEvent(string, IGH_UndoAction),
GH_ObjectUndoAction(Guid), protected Object_Undo/Object_Redo(GH_Document, IGH_DocumentObject),
virtual Write/Read and expiration properties, and GH_Document.Modified(). The base action does not serialize
its private target id, so our action serializes that id and resolves it in Internal_Undo/Internal_Redo.
Rhino 8 DrawBitmap(DisplayBitmap, int, int) and rounded-rectangle/2D-line APIs are verified against the pinned XML.
