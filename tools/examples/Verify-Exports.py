"""Inspect RhinoCore artifacts, using Pillow and PyMuPDF; never writes into installed plugins."""
import argparse
import json
from pathlib import Path

import pymupdf
from PIL import Image


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def png(root, scale=1):
    return root / "compact" / "captures" / f"raster-{scale}x.png"


def compare(a, b):
    with Image.open(a) as first, Image.open(b) as second:
        assert first.size == second.size
        return sum(x != y for x, y in zip(first.convert("RGBA").getdata(), second.convert("RGBA").getdata()))


def verify_scope(root):
    captures = root / "captures"
    check = read(captures / "label-check.json")
    with pymupdf.open(captures / "overlay-vector.pdf") as document:
        page = document[0]
        hits = page.search_for(check["sentinel"])
        assert len(hits) == 1, hits
        scale = 96 * check["dpi"] / 72
        pill, dot = check["pill"], check["dot"]
        expected = ((pill["x"] + 12) * check["dpi"], (pill["y"] + 1) * check["dpi"])
        assert max(abs(x * scale - y) for x, y in zip(hits[0].tl, expected)) < 2, (hits, expected)
        render = page.get_pixmap(matrix=pymupdf.Matrix(scale * 4, scale * 4))
        swatch = (208, 32, 96)
        for x, y in ((pill["x"] + 6, pill["y"] + 9), (dot["x"] + 2.5, dot["y"] + 2.5)):
            pixel = render.pixel(round(x * check["dpi"] * 4), round(y * check["dpi"] * 4))
            assert max(abs(a - b) for a, b in zip(pixel, swatch)) <= 5, pixel
    with pymupdf.open(captures / "preview-off.pdf") as document:
        text = document[0].get_text()
        assert "Preview off control" in text and check["sentinel"] not in text, text

    with pymupdf.open(captures / "label-opacity.pdf") as document, Image.open(captures / "label-opacity.png") as png:
        page = document[0]
        assert "Fade probe" in page.get_text()
        pdf = page.get_pixmap(matrix=pymupdf.Matrix(96 * 4 / 72, 96 * 4 / 72))
        samples = {}
        for name, point in {"dot center": (30, 30), "dot rim": (22, 30),
                            "pill center": (230, 57), "pill rim": (230, 22),
                            "pill swatch": (104, 56)}.items():
            raster = png.getpixel(point)[:3]
            vector = pdf.pixel(*point)
            assert max(abs(a - b) for a, b in zip(raster, vector)) <= 8, (name, raster, vector)
            samples[name] = {"png": raster, "pdf": vector}
        print(json.dumps(samples, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scope", type=Path)
    for name in ("full", "compact-1", "compact-2", "compact-4", "tooltip", "selected"):
        parser.add_argument("--" + name, type=Path)
    args = parser.parse_args()
    if args.scope:
        verify_scope(args.scope)
        return
    if any(value is None for name, value in vars(args).items() if name != "scope"):
        parser.error("provide --scope or all six structural export directories")
    compact = [args.compact_1, args.compact_2, args.compact_4]
    dimensions = []
    zooms = []
    for root, scale in zip(compact, (1, 2, 4)):
        with Image.open(png(root, scale)) as image:
            assert image.size == (474 * scale, 329 * scale), image.size
            dimensions.append(image.size)
        metadata = read(root / "compact" / "captures" / f"raster-{scale}x.json")
        # Cards scale with the viewport: raster scale = dpi x layout zoom x capture multiplier.
        zooms.append(metadata["sessions"][0]["raster"]["scale"] / (metadata["dpi"] * scale))
    assert all(abs(z - zooms[0]) < 1e-9 for z in zooms) and .625 - 1e-9 <= zooms[0] <= 2 + 1e-9, zooms

    baseline = png(args.compact_1)
    tooltip_difference = compare(baseline, png(args.tooltip))
    assert tooltip_difference == 0, tooltip_difference
    assert read(args.selected / "compact-capture-check.json")["selected"] == ["M04"]
    highlights = 0
    with Image.open(baseline) as before, Image.open(png(args.selected)) as after:
        before, after = before.convert("RGBA"), after.convert("RGBA")
        for y in range(after.height):
            for x in range(after.width):
                # This fixture's metric has no keys or accent ink: new blue pixels come from linked wires/markers.
                r, g, b, alpha = after.getpixel((x, y))
                if alpha > 0 and b > r + 40 and b > g + 20 and after.getpixel((x, y)) != before.getpixel((x, y)):
                    highlights += 1
    assert highlights > 10, highlights

    full = read(args.full / "full-capture-checks.json")[0]
    assert len(full["regions"]) == 10 and full["after"] > full["before"]
    # Every surface must contain opaque pixels AND ink; a flat white rectangle does not pass.
    # A plain Title has no surface by design; ExportChecks only requires its text to be visible.
    assert all(r["fraction"] > .98 and r["ink"] > 5 for r in full["regions"])
    # The example's slot arrangement must not cover other cards.
    for i, a in enumerate(full["regions"]):
        for b in full["regions"][i + 1:]:
            assert (a["x"] + a["w"] <= b["x"] or b["x"] + b["w"] <= a["x"] or
                    a["y"] + a["h"] <= b["y"] or b["y"] + b["h"] <= a["y"])
    example = read(args.full / "example.json")
    capture = read(args.full / "captures" / "raster-1x.json")
    pdf_path = args.full / "captures" / "overlay-vector.pdf"
    assert pdf_path.is_file() and pdf_path.stat().st_size > 0
    with pymupdf.open(pdf_path) as pdf:
        assert len(pdf) == 1
        page = pdf[0]
        point_scale = 72 / (96 * capture["dpi"])
        zoom = capture["sessions"][0]["raster"]["scale"] / capture["dpi"]
        expected_size = (capture["width"] * point_scale, capture["height"] * point_scale)
        assert abs(page.rect.width - expected_size[0]) < .01
        assert abs(page.rect.height - expected_size[1]) < .01
        text = page.get_text()
        titles = example["cards"]
        assert all(title in text for title in titles), text
        spans = [span for block in page.get_text("dict")["blocks"] if "lines" in block
                 for line in block["lines"] for span in line["spans"]]
        errors = []
        # Card titles sit at the card padding; a plain Title's heading is laid out by its own alignment, so skip it here.
        for title, region in ((t, r) for t, r in zip(titles, full["regions"]) if not r.get("plain")):
            span = next(s for s in spans if s["text"] == title)
            expected = ((region["x"] + 12 * capture["dpi"] * zoom) * point_scale,
                        (region["y"] + 10 * capture["dpi"] * zoom) * point_scale)
            errors.extend(abs(actual - wanted) for actual, wanted in zip(span["bbox"][:2], expected))
            assert abs(span["size"] - 9 * zoom) < .01
        assert max(errors) < .01, errors
        page.get_pixmap(matrix=pymupdf.Matrix(1, 1)).save(args.full / "captures" / "pdf-page.png")
        (args.full / "captures" / "pdf-text.txt").write_text(text, encoding="utf-8")
        result = dict(png_dimensions=dimensions, compact_zoom=zooms[0], full_zoom=zoom, full_card_regions=10, minimum_opaque_fraction=min(r["fraction"] for r in full["regions"]),
                      tooltip_difference=tooltip_difference, selected_wire_pixels=highlights,
                      pdf_pages=len(pdf), pdf_points=[page.rect.width, page.rect.height], searchable_titles=len(titles),
                      maximum_title_position_error_points=max(errors), pdf_bytes=pdf_path.stat().st_size)
    (args.full / "verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
