# Outline fixtures

Makes the synthetic sample exports in `samples/outlines`.

- `shapes.py` (Python, needs `shapely`) draws a 13 mm wrench (with the box-end hole), pliers and a 5 mm hex key, then samples
  their edges every 0.35 mm with 0.12 mm of jitter so they look like a photo trace. It writes `contours.json` in outline-app's
  internal form (mm on an A4 sheet, Y down).
- `export.mjs` (Node, `npm install`) writes the DXF, SVG and `_dxf.zip` files. It mirrors outline-app's `DxfExport.ts` and
  `SvgExport.ts`, using the same public [@tarikjabiri/dxf](https://www.npmjs.com/package/@tarikjabiri/dxf) library calls; no
  outline-app code is copied.

```
python shapes.py
npm install
node export.mjs ../../samples/outlines
```
