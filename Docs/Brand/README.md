# Wbskt logo

The logo is a white Inter ExtraBold Italic **w** on an orange (`#e85d26`) tile with rounded
corners (radius 14 on a 64 grid).

- `wbskt-logo.svg`: the source. The letter is a path taken from the font, so it renders the same
  without Inter installed.
- `wbskt-logo.png`: 512×512 with transparent corners, for package icons and anywhere SVG is not
  accepted.

The w is centred on its ink, not on its bounding box. An italic centred by its box looks left
of centre, so the letter sits 2 units (of 64) right of the box centre. Keep that offset if you
redraw it.

The same mark is used by the console (`app-brand-mark`), the website, the SDK packages and the
HTML pages in this repo. Change them together.
