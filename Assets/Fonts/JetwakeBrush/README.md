# Jetwake Brush

Original heavy brush lettering for the Jetski Runner project, designed from vector stroke paths. The supplied specimen was used only to communicate a broad hand-painted style; its raster pixels and exact contours were not traced. This font is a new work, not a digitization of the font shown in the specimen.

## Contents

- `JetwakeBrush-Regular.ttf` — installable and embeddable TrueType font.
- `Uxxxx.svg` — editable vector source for each encoded glyph (`U0041.svg` is `A`).
- `specimen.html` — live specimen showing all glyphs from the TTF.
- `OFL.txt` — SIL Open Font License 1.1, including required notices.
- `../../../Tools/Fonts/build_jetwake.py` — deterministic build source for the TTF and SVG glyphs.

The font maps 105 characters: A–Z, a–z, 0–9, Turkish Çç Ğğ İi Iı Öö Şş Üü, and the included punctuation and currency symbols. It is an informal brush display face; use at generous sizes for titles and short labels.

## Rebuild

```sh
python3 -m pip install fonttools shapely
python3 Tools/Fonts/build_jetwake.py
```

## License

Jetwake Brush is distributed under the SIL Open Font License, Version 1.1. See `OFL.txt` for the full terms. This independent font does not use the unidentified commercial/reference font or its glyph artwork.
