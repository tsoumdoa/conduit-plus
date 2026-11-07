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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("full", "compact-1", "compact-2", "compact-4", "drag", "tooltip", "selected"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    compact = [args.compact_1, args.compact_2, args.compact_4]
    dimensions = []
    for root, scale in zip(compact, (1, 2, 4)):
        with Image.open(png(root, scale)) as image:
            assert image.size == (474 * scale, 329 * scale), image.size
            dimensions.append(image.size)
        metadata = read(root / "compact" / "captures" / f"raster-{scale}x.json")
        assert metadata["sessions"][0]["raster"]["scale"] == metadata["dpi"] * scale

    baseline = png(args.compact_1)
    drag_difference = compare(baseline, png(args.drag))
    tooltip_difference = compare(baseline, png(args.tooltip))
    assert drag_difference == tooltip_difference == 0, (drag_difference, tooltip_difference)
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
    assert len(full["regions"]) == 9 and full["after"] > full["before"]
    # Every surface must contain opaque pixels AND ink; a flat white rectangle does not pass.
    assert all(r["fraction"] > .98 and r["ink"] > 5 for r in full["regions"])
    # The saved manual layout must not cover other cards.
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
        expected_size = (capture["width"] * point_scale, capture["height"] * point_scale)
        assert abs(page.rect.width - expected_size[0]) < .01
        assert abs(page.rect.height - expected_size[1]) < .01
        text = page.get_text()
        titles = example["cards"]
        assert all(title in text for title in titles), text
        spans = [span for block in page.get_text("dict")["blocks"] if "lines" in block
                 for line in block["lines"] for span in line["spans"]]
        errors = []
        for title, region in zip(titles, full["regions"]):
            span = next(s for s in spans if s["text"] == title)
            expected = ((region["x"] + 12 * capture["dpi"]) * point_scale,
                        (region["y"] + 10 * capture["dpi"]) * point_scale)
            errors.extend(abs(actual - wanted) for actual, wanted in zip(span["bbox"][:2], expected))
            assert abs(span["size"] - 9) < .01
        assert max(errors) < .01, errors
        page.get_pixmap(matrix=pymupdf.Matrix(1, 1)).save(args.full / "captures" / "pdf-page.png")
        (args.full / "captures" / "pdf-text.txt").write_text(text, encoding="utf-8")
        result = dict(png_dimensions=dimensions, full_card_regions=9, minimum_opaque_fraction=min(r["fraction"] for r in full["regions"]),
                      drag_difference=drag_difference, tooltip_difference=tooltip_difference, selected_wire_pixels=highlights,
                      pdf_pages=len(pdf), pdf_points=[page.rect.width, page.rect.height], searchable_titles=len(titles),
                      maximum_title_position_error_points=max(errors), pdf_bytes=pdf_path.stat().st_size)
    (args.full / "verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
