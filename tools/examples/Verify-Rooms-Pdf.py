"""Check fresh RhinoCore rooms exports produced with Build-Examples.ps1 -RoomsPdf."""
import argparse
import json
from pathlib import Path

import pymupdf


def verify(output, shaded):
    root = output / "room-areas"
    fixture = json.loads((root / "pdf-samples.json").read_text(encoding="utf-8"))
    path = root / "captures" / "overlay-vector.pdf"
    with pymupdf.open(path) as pdf:
        assert len(pdf) == 1, "Expected one PDF page"
        page = pdf[0]
        width, height = fixture["width"], fixture["height"]
        points_per_dot = 72 / (96 * fixture["dpi"])
        assert abs(page.rect.width - width * points_per_dot) < .01
        assert abs(page.rect.height - height * points_per_dot) < .01

        titles = fixture["titles"]
        lines = page.get_text().splitlines()
        assert len(titles) == 8 and all(title in lines for title in titles)
        assert all(lines.count(f"R{i:02}") == 1 for i in range(1, 18))
        title_bounds = [page.search_for(title) for title in titles]

        images = page.get_images()
        if shaded:
            assert len(images) == 1, "Expected one model underlay"
            xref, mask, image_width, image_height = images[0][:4]
            assert (image_width, image_height) == (width, height)
            assert page.get_image_rects(xref) == [page.rect]
            image = pymupdf.Pixmap(pdf, xref)
            alpha = pymupdf.Pixmap(pdf, mask)
            points = [(round(p["x"]), round(p["y"])) for p in fixture["points"]]
            colours = [image.pixel(x, y) for x, y in points]
            assert len(colours) == 17 and len(set(colours)) >= 2
            assert all(max(c) - min(c) > 35 for c in colours), colours
            assert all(alpha.pixel(x, y)[0] > 240 for x, y in points)
            # This viewport corner contains an opaque metric card in the final PDF.
            # The scene bitmap must have no card pixels there.
            dpi = fixture["dpi"]
            assert alpha.pixel(round(30 * dpi), round(50 * dpi))[0] == 0
        else:
            assert not images, "Vector export unexpectedly contains an image"

        print(f"{'Shaded' if shaded else 'Vector'}: {path.stat().st_size:,} bytes, "
              f"{page.rect.width:g} x {page.rect.height:g} pt, 8 titles and 17 labels")
        return title_bounds


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("shaded", type=Path)
    parser.add_argument("vector", type=Path)
    args = parser.parse_args()
    shaded_bounds = verify(args.shaded, shaded=True)
    assert shaded_bounds == verify(args.vector, shaded=False), "Card text moved"
    print("17 shaded interiors, matching title positions, no cards in the model underlay")
