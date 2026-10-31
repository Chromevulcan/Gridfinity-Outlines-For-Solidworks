# Changelog

## 1.3.0
- **Parts tab** (the designer's fifth tab): import a photo or outline-app exports and lay the parts onto the box from the Box tab, with a parts list, per-part settings, a to-scale preview and a status line.
- **Built-in photo tracer** (`Gridfinity.Trace`, OpenCvSharp): finds the paper sheet, removes perspective, evens the light and traces every part; paper sizes, flatbed scans, sensitivity; a tracing overlay in the import window and `.trace.png` from the CLI.
- **Round bottoms** ("follows the shape"): depth from the part's width at every point, cut in RoundStep layers grown so they never undercut; RoundMaxThickness cap.
- **Y offset** (`PartTopOffset`, per-part `TopOffset`): the part tops sit on a plane above the tray; only what is below the surface is cut. The centreline sketch sits on a reference plane at that height.
- **Part centrelines** (construction sketch) and **part names** debossed along them (`LabelText`, `LabelHeight`, `LabelSide`).
- **Splitting for the printer**: printer list with bed sizes, custom bed, margin; oversized trays build as whole-cell pieces `<name>_A1`, ...; `split` CLI command.
- CLI: photo input, `--round`, `--round-max`, `--offset`, `--step`, `--no-centerlines`, `--paper`, `--scan-dpi`, `--sensitivity`, `--printer`, `--bed`, `--no-split`.
- Add-in resolves its dependencies and the native tracer from its own folder.

## 1.2.0 (Gridfinity Outlines)
- New `Outline` pocket shape: any traced outline (concave, with holes), offset by the clearance with Clipper2, optional `KeepIslands`.
- Outline import from outline-app exports: DXF (LWPOLYLINE/POLYLINE with bulges, CIRCLE, ELLIPSE, `$INSUNITS`), SVG (paths, shapes, transforms, mm units) and `_dxf.zip`. Cleans, simplifies and squares each trace to the grid.
- SolidWorks: *Build tray from outlines (DXF/SVG)...* menu command and *Import outlines...* in the designer, with a to-scale preview.
- `gridfinity-outlines` command-line tool: `tray`, `watch` (Downloads folder to trays) and `check`, with SVG previews.
- `MaxUnitsX` / `MaxUnitsY` cap auto-grow (5 x 5 by default for outline trays).
- Specification conformance tests against gridfinity.xyz/specification.
- New add-in identity (COM GUID, ProgId, menu group, log folder `%LocalAppData%\GridfinityOutlines\Logs`, assembly names, installer AppId) so it installs alongside Gridfinity for SolidWorks.

## 1.1.0
- Baseplate rebuilt as an open frame measured from a connectable baseplate STL: bossed base layer, magnet pockets (diameter, thickness, floor), screw holes, connection slots with per-side switches.
- New Clips tab / `ClipDefinition` / `GridfinityBuilder.BuildClips`; clip profile taken from the Connection Clip STL.
- New `Cradle` pocket shape (round bottom) built from the Top plane, with a scratch-part direction probe.
- Pocket floors in solid blocks must clear the 4.75 mm Gridfinity feet (validator error otherwise).
- Debossed labels: sketch-text call reworked, typed and late-bound attempts, detailed log lines.
- Add-in menu created once after SolidWorks idles, with saved command-group settings preserved (fixes the missing Tools menu).
- Inno Setup installer with SolidWorks folder detection and confirmation.
- Log moved to `%LocalAppData%\GridfinityForSolidWorks\Logs`.
- Removed: baseplate `Style` / `FloorThickness` (old files still load).

## 1.0.0
- First release: box, pockets, auto-layout, labels, baseplate, add-in shell.
