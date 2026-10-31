# Gridfinity Outlines for SolidWorks

A SolidWorks add-in, command-line tool and C# API that turns a photo of your parts (or outlines traced in
[outline-app](https://github.com/Chromevulcan/outline-app)) into [Gridfinity](https://gridfinity.xyz) tool trays with a cutout for
every part. It is built
on [Gridfinity for SolidWorks](https://github.com/Chromevulcan/Gridfinity_For_Solidworks), so everything that project does
(boxes, open trays, stacking lips, magnets, labels, baseplates, clips, JSON) is still here.

**Version 1.3.0** · Windows only for the add-in · SolidWorks 2026 (64-bit) · .NET Framework 4.8 · CLI runs anywhere with .NET 8

## How it works

The quickest way is the **Parts** tab of the designer: set up the box on the **Box** tab, then on **Parts** press
**Import photo or outlines...**, pick a photo of the parts lying on a sheet of paper, and the traced parts are laid onto the box
as cutouts. Choose a flat or round bottom for each part, set the Y offset, and press **Build box**. See [The Parts tab](#the-parts-tab).

Outlines from outline-app work too:

1. **Trace** a part in outline-app ([online](https://outline.georgs.lv/) or self-hosted): photograph it on a sheet of paper,
   let it find the outline, then **Export** it as **DXF** or **SVG**. Several objects come as one `<name>_dxf.zip`.
2. **Import** the export in one of three ways:
   - **Tools > Gridfinity Outlines > Build tray from photo or outlines...**: pick the files, check the preview, press *Build tray*.
     The tray is saved as `<name>.json` next to the first file as well, so you can reopen it in the designer.
   - The **Parts** tab (or **Import parts...**) in the designer window adds the traced parts to the tray you are already editing.
   - The **`gridfinity-outlines`** command-line tool writes the tray JSON plus a to-scale `.preview.svg`, and can watch your
     Downloads folder so every new export becomes a tray on its own. Build the JSON with **Build from JSON file...**.
3. The importer cleans the trace (removes self-crossings, drops jitter), turns each part square to the grid, adds clearance,
   lays everything out on the smallest grid that fits, picks the fewest 7 mm height units that hold the deepest cut, and splits
   a tray that is too large for your printer into pieces that fit.

outline-app is a separate program and is not part of this repository; this project only reads the files it exports.

## What it builds

| Tab | Builds |
|---|---|
| **Box** | A Gridfinity bin of any grid size and height, solid or open tray, stacking lip, magnet holes, walls, dividers. |
| **Baseplate** | An open-frame baseplate with optional magnet pockets (any diameter and thickness), screw holes and connection slots. |
| **Cutout** | Pockets cut into a box: traced **outlines** (any shape, with holes), rectangles, circles, slots, hexagons, convex polygons and round-bottomed cradles, with auto-layout and debossed text labels. |
| **Clips** | The connection clips as a part of their own, as many as you want. |
| **Parts** | Parts traced from a photo (or outline-app exports) laid onto the box from the Box tab: flat or round bottoms, a Y offset, centrelines and names, split to fit your printer. |

## The Parts tab

1. **Set up the box** on the Box tab (grid, height, style, magnets). The parts are laid onto this box. With
   *Grow the box to fit the parts* ticked it grows (up to MaxUnitsX x MaxUnitsY) when they do not fit; the height is only ever
   raised, never lowered.
2. **Import photo or outlines...** opens the import window. It shows the tray and, on the second tab, the tracing of the
   selected photo (green outlines over the straightened sheet), so you can see what was found before you press *Add parts*.
3. Pick a part in the list to change it: name, **Bottom** (Flat or Round), its own Y offset, its label, clearance, position.
4. **Build box** (or **Lay out parts** first). Too large for the printer? It is built as several parts, `<name>_A1`, `<name>_A2`, ...

### Photos

Lay the parts on a sheet of plain paper on a darker, plain surface, and photograph from straight above with the whole sheet in
view. The tracer (OpenCV, built in; nothing to install) finds the four corners of the sheet, removes the camera's perspective,
evens out the lighting, and traces every part on it. The sheet's size is the scale, so set **Paper** (Letter by default; A4,
Legal, A3, Tabloid or a custom size). Flatbed scans work too: set **PhotoMode** to Scan and **ScanDpi**. If shadows are traced
as part of a part, lower **Sensitivity**; for pale parts, raise it. On the synthetic sample photo
(`samples/photos/drivers.jpg`, three screwdrivers of 250, 235 and 210 mm) the traced lengths are within 0.2 mm.

### Flat or round bottoms

- **Flat**: a flat floor at the part's thickness (Depth) less the Y offset.
- **Round** ("follows the shape"): for parts that are round in section, such as screwdrivers, sockets, pens and drill bits
  lying on their side. The depth at every point is taken from the part's width there in the photo (the distance to its edge),
  so a 30 mm handle sinks 15 mm deeper at its centre than at its edge and the shank gets a narrow, shallow groove. It is cut as
  stacked layers **RoundStep** high (0.4 mm by default, like a print layer), each one a little wider than the true shape so it
  never undercuts it. **RoundMaxThickness** caps the depth for parts that are wider than they are thick.

### Y offset

**PartTopOffset** (Parts tab) sets how high above the tray's top surface the top of each part sits. Picture a plane that
far above the tray: the parts hang down from it, and only what is below the tray surface is cut. At 0 the parts are flush; at
3 mm every part stands 3 mm proud so it is easy to grab. A part can override it with its own **TopOffset**. In SolidWorks the
plane is a reference plane, *Part centerlines plane*, that the centreline sketch below sits on.

### Centrelines and names

Every part gets a centreline along its length (*Part centerlines*, a construction sketch on the part top plane) that you can use
to place text yourself. Give a part a **LabelText** and it is debossed beside the part, parallel to that centreline, **LabelHeight**
mm tall, below or above it (**LabelSide**); the layout leaves room for it. *NameLabels* in the import window names every part
after its file.

### Splitting for your printer

Pick the printer (or a custom bed) on the Parts tab. When the tray is larger than the bed less **BedMargin**, it is cut along cell
lines into the fewest pieces that fit, each one a complete Gridfinity bin that sits in your baseplate. Parts that cross a seam are
cut in both pieces (the cut runs 2 mm past the seam so the halves meet cleanly). Round cradles cannot be split; move them off the
seam or pick a larger printer. Beds (usable X x Y, mm):

| Printer | Bed |
|---|---|
| Bambu Lab A1 mini | 180 x 180 ([Bambu Lab](https://bambulab.com/en-us/a1-mini/tech-specs)) |
| Bambu Lab P1S / X1C / A1 (default) | 256 x 256 ([Bambu Lab](https://bambulab.com/en/p1)) |
| Bambu Lab H2D (single nozzle) | 325 x 320 ([Bambu Lab](https://bambulab.com/en-us/h2d/tech-specs)) |
| Prusa MK4S | 250 x 210 ([Prusa](https://www.prusa3d.com/en/product/original-prusa-mk4s/)) |
| Prusa CORE One | 250 x 220 ([Prusa](https://www.prusa3d.com/product/prusa-core-one/)) |
| Prusa CORE One L | 300 x 300 ([Prusa](https://www.prusa3d.com/p/core-one-l/)) |
| Prusa MINI+ | 180 x 180 ([3D Saga](https://www.3dsaga.com/en/products/original-prusa-mini)) |
| Creality Ender-3 V3 SE | 220 x 220 ([Creality](https://store.creality.com/eu/products/ender-3-v3-se-3d-printer)) |
| Creality K1 / K1C | 220 x 220 ([Creality](https://store.creality.com/blogs/all/k1-vs-k1-max-vs-k1c)) |
| Creality K1 Max | 300 x 300 (same page) |
| Elegoo Neptune 4 Pro | 225 x 225 ([Elegoo](https://www.elegoo.com/products/elegoo-neptune-4-pro-fdm-3d-printer)) |
| Example 350 x 350 (Voron-class) | 350 x 350, an example size; check your own printer |
| Custom | CustomBedWidth x CustomBedDepth |

At the default 2 mm margin a 256 mm bed fits 6 x 6 cells and a 180 mm bed 4 x 4.

![Sample: three screwdrivers traced from a photo, round bottoms, split for an A1 mini](samples/photos/out/drivers.preview.png)

`samples/photos/out` has the tray from the sample photo: three screwdrivers with round bottoms and names, 3 x 7 cells, 6 height
units, split into a 3 x 4 and a 3 x 3 piece for an A1 mini (the dashed red line), plus the tracing (`drivers.trace.jpg`).

## Outline import options

The same options appear in the import window (as a property list) and on the command line.

| Option | CLI | Default | What it does |
|---|---|---|---|
| Depth | `--depth` | 12 mm | Flat parts: the part's thickness. The cut is this less the Y offset. |
| Bottom | `--round` | Flat | Round: the depth follows the part's width (see above). |
| RoundMaxThickness | `--round-max` | 0 (no limit) | Thickest a round part is taken to be. |
| TopOffset | `--offset` | 0 mm | Y offset: how far the part tops stand above the tray surface. |
| Paper / PaperWidth / PaperHeight | `--paper` | Letter | Sheet the parts lie on in a photo. |
| PhotoMode / ScanDpi | `--scan-dpi` | Photo | Flatbed scan at a known resolution instead of a photo. |
| Sensitivity | `--sensitivity` | 50 | Raise for pale parts, lower if shadows are traced. |
| PaperBorder / PixelsPerMm | | 4 mm / 8 | Edge of the sheet to ignore; tracing resolution. |
| Printer | `--printer`, `--bed`, `--no-split` | P1S / X1C / A1 | Printer the tray has to fit. |
| Clearance | `--clearance` | 1.0 mm | Total clearance, half added on every side. Photo traces are rarely better than 0.5 mm, so 1.0 is a safe start. |
| KeepIslands | `--keep-islands` | off | Leave material standing inside holes in an outline (for example the ring of a box-end wrench), shrunk by the clearance. |
| NotchDiameter | `--notch` | 0 | Round finger notch at the centre of each pocket. |
| AllowRotate | `--no-rotate` | on | Let auto-layout turn pockets by 90 degrees. |
| Style | `--open-tray` | SolidBlock | Solid block, or a walled open tray with the pockets cut into a thickened floor. |
| MagnetHoles | `--magnets` | off | 6.5 mm magnet pockets in the four corners of every cell. |
| StackingLip | `--lip` | off | Open tray only. |
| NameLabels | `--labels` | off | Debossed name beside each part, along its centreline (adds room between parts). |
| MaxUnitsX / MaxUnitsY | `--max-units 5x5` | 5 x 5 | Largest grid auto-grow may use. Use `--units 4x3` for a fixed size. |
| SimplifyTolerance | `--simplify` | 0.1 mm | Points closer than this to a straight line are dropped (a trace of 1,200 points usually ends up near 500). |
| AlignToAxes | `--no-align` | on | Turn each part so its smallest enclosing rectangle lines up with the grid. |
| MinArea | `--min-area` | 20 mm² | Smaller shapes are treated as tracing noise. |
| Scale / Mirror | `--scale`, `--mirror` | 1, off | For files in other units, or a part traced from the wrong side. |

Photos: `.jpg`, `.jpeg`, `.png`, `.bmp`, `.tif`, `.tiff`, `.webp`. Outline files are read like this: DXF closed `LWPOLYLINE` / `POLYLINE` (with bulge arcs), `CIRCLE` and `ELLIPSE`, scaled by `$INSUNITS`;
SVG `path`, `polygon`, `polyline`, `rect`, `circle` and `ellipse` with transforms, scaled from the `width`/`height` in mm and the
`viewBox`; ZIP files are opened and every DXF/SVG inside is read. Loops inside loops become holes, and loops inside holes become
separate parts again (even/odd nesting).

### Command line

```
dotnet build src/Gridfinity.Cli -c Release
gridfinity-outlines tray samples/outlines/toolset_dxf.zip --magnets --labels --notch 18 -o toolset.json
gridfinity-outlines tray samples/photos/drivers.jpg --round --labels --offset 2 --printer "A1 mini" --max-units 8x8
gridfinity-outlines split big-tray.json --printer "MK4S"
gridfinity-outlines watch %USERPROFILE%\Downloads --out-dir C:\Trays --depth 15
gridfinity-outlines check toolset.json
```

The first line gives a 5 x 3 cell tray (209.5 x 125.5 mm, 21 mm tall) for the three sample tools. Run with `--help` for every option.
A photo also writes `<name>.trace.png` showing what was traced. `watch` checks the folder once a second and waits until a download has finished before reading it.

### Outline pockets in JSON

`"Shape": "Outline"` with `Points` (the outer edge, mm, about the pocket centre), `Holes` (a list of point lists) and
`KeepIslands`. They lay out, rotate, validate and save like the other shapes. The importer writes these for you.

## Accuracy to the Gridfinity specification

Pocket geometry is cut only into the top of the bin, so the parts that have to fit other Gridfinity items are unchanged.
`tests/Gridfinity.Core.Tests/SpecConformanceTests.cs` checks the dimensions against the
[Gridfinity specification](https://gridfinity.xyz/specification/) (the "Gridfinity Design Reference" drawing on that page):

| Item | Specification | This project |
|---|---|---|
| Grid pitch / bin size | 42 mm / 41.5 mm (0.5 mm tolerance) | same |
| Base profile | 0.8 chamfer, 1.8 vertical, 2.15 chamfer (4.75 mm); 35.6 mm at the bottom | same |
| Corner diameters | 7.5 / 3.2 / 1.6 mm | same |
| Height unit | 7 mm; 2 u bin with lip 18.4 mm | same |
| Stacking lip | 0.7 / 1.8 / 1.9 mm (4.4 mm) | same, top truncated by a 0.8 mm land (`LipTopInset`) for printing |
| Magnet holes | 6.5 mm, 13 mm from the cell centre, all four corners, for 6 x 2 mm magnets | same; 2.4 mm deep |
| Baseplate profile | 0.7 / 1.8 / 2.15 mm, 8 mm corner | same |

Known differences, left as they were:

- **Baseplate magnet and screw positions** are 13.5 mm from the cell centre (measured from a gridfinitygenerator.com plate),
  while bin magnets are at the specification's 13.0 mm, so the two are 0.5 mm apart. Change `BaseplateSpec.HoleOffset` to 13.0
  for strict alignment.
- **Bins have no M3 screw holes** (the specification shows 3 mm screw holes alongside the magnets).
- The stacking lip has the 0.8 mm flat top mentioned above instead of a sharp edge.

A test also confirms that adding outline pockets never changes the bin, base or lip. Split pieces are whole-cell bins with the
full base and lip, so they sit in a baseplate like any other bin. Round bottoms and labels are cut into the top only.

## Install

**With the installer** (`GridfinityOutlines-Setup.exe`, made by `scripts\make-installer.cmd`): run it as administrator, confirm
your SolidWorks folder, confirm the install folder, then start SolidWorks and open **Tools > Gridfinity Outlines**. If the menu is
missing, tick *Gridfinity Outlines* under **Tools > Add-Ins**.

This add-in has its own COM identity, menu, log folder and assembly names (`GridfinityOutlines.Sw.dll`,
`GridfinityOutlines.Core.dll`), so it can be installed next to the original Gridfinity for SolidWorks add-in.

## Tested, and not tested

- Tested here: the core library, photo tracer and command-line tool on .NET 8 (70 tests, including specification checks, a
  synthetic perspective photo traced to within 0.5 mm, round bottoms never undercutting the part, labels along the centreline,
  and splitting for every printer), and the add-in compiles against the SolidWorks 2026 interop files.
- **Not yet tested in SolidWorks:** the Parts tab, the import window, the *Build tray from photo or outlines* command, round
  bottoms (one cut per layer), the offset reference plane (its direction is checked and flipped if needed), the centreline sketch, text
  along a turned centreline, building split pieces, and loading the native tracer (`dll\x64\OpenCvSharpExtern.dll`) inside
  SolidWorks.
- Round bottoms are many cuts: the 31.6 mm deep sample handle is 79 layers at 0.4 mm. If a build is slow, raise RoundStep to
  0.8 or 1.0 mm. Large traces are simplified to keep sketches light; if a build is slow or
  fails, raise `SimplifyTolerance` to 0.2 to 0.3 mm.
- The sample photo and the files in `samples\outlines` are synthetic tools written with the same DXF library and calls as outline-app's
  exporter (`tools\outline-fixtures`), not real photo traces. Please try a real export and report how it went.
- Layout packs the bounding boxes of the parts, turning them only by 0 or 90 degrees, so long diagonal or curved parts may use
  more room than a hand layout would. Turn `AutoLayout` off and set `X`, `Y`, `RotationDeg` to place them yourself.

Everything inherited from 1.1.0 was tested by the original author in SolidWorks Design 2026 (box and baseplate builds, magnets,
screws, slots, cradles, labels, the clip part, add-in loading).

## Help wanted: please try it and tell me how it went

If you install it, please open an [issue](../../issues) with your SolidWorks version, what you traced and built, anything
that looked wrong, and the last lines of `%LocalAppData%\GridfinityOutlines\Logs\Gridfinity_addin.log`.

## Licensing

This project is MIT licensed, copyright (c) 2026 Scott Myers (see `LICENSE`). It uses [Clipper2](https://github.com/AngusJohnson/Clipper2) (Boost Software
License 1.0) for offsetting and cleaning outlines, [OpenCvSharp](https://github.com/shimat/opencvsharp) (Apache-2.0, with OpenCV
under Apache-2.0) for tracing photos, and Newtonsoft.Json (MIT). outline-app is AGPL-3.0; none of its code is
included or linked here, only the DXF/SVG files it exports are read. The photo tracer is written from scratch on OpenCV.

---

## Layout

| Project | Target | Purpose |
|---|---|---|
| `src/Gridfinity.Core` | netstandard2.0 | No SolidWorks dependency. Gridfinity spec, pocket shapes, outline import (DXF/SVG/ZIP), auto-layout, validation, SVG preview, and the **build plan** (an ordered list of sketch + extrude/cut features). |
| `src/Gridfinity.Trace` | netstandard2.0 | The photo tracer: finds the paper, straightens it, traces the parts (OpenCvSharp). |
| `src/Gridfinity.Cli` | net8.0 | `gridfinity-outlines`: outline exports to tray JSON and preview, folder watching, checking. |
| `src/Gridfinity.Sw` | net48, x64 | Replays a build plan through the SolidWorks API; the `ISwAddin` shell; the designer dialog and the outline import window. |
| `tests/Gridfinity.Core.Tests` | net8.0 | xUnit tests for the core (specification dimensions, closed contours, layout, validation, JSON, outline import). |
| `tools/outline-fixtures` | Python, Node | Makes the synthetic sample exports in `samples/outlines` with the same DXF library calls as outline-app. |

All dimensional logic is in Core, so it can be tested and debugged without SolidWorks, and the same plan
could drive another CAD system.

## Build and install

1. Install Visual Studio 2022 (or the .NET SDK plus the .NET Framework 4.8 targeting pack).
2. Make sure `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist` has the three interop DLLs, or build with
   `-p:SolidWorksApiDir="<your path>"`.
3. `dotnet test` (core tests), then build the solution in **Release**.
4. From an **administrator** prompt: `scripts\register.cmd`. Restart SolidWorks.
5. A **Gridfinity Outlines** menu appears with *Gridfinity designer...*, *Build tray from JSON file...* and *Build tray from photo or outlines...*.
   Use `scripts\unregister.cmd` to remove it.

SolidWorks add-ins need the full API. I could not confirm whether your "SolidWorks Design" edition allows
add-ins and macros; if the add-in will not load, check that first.

## The designer window (Tools > Gridfinity Outlines > Gridfinity designer...)

| Tab | What it holds |
|---|---|
| **Box** | The bin itself: grid size (UnitsX, UnitsY), HeightUnits, style (SolidBlock or OpenTray), stacking lip, magnet holes, walls, floor height, compartments and dividers. |
| **Baseplate** | A baseplate, always an open frame: UnitsX, UnitsY; **Magnets** (on/off, diameter, thickness, material under the magnet); **Screws** (on/off, diameter); **Connection** (slots on/off, which sides, clearance); **Clip length** for the slots. |
| **Clips** | The connection clips as a part of their own: how many (side by side) and their length. `samples\clips-6.json` is an example. |
| **Cutout** | What is cut into the box: the pocket list (rectangle, circle, slot, hex, polygon, with depth, clearance, position and finger notch), the auto-layout settings (including MaxUnitsX/MaxUnitsY), and the debossed labels. **Import parts...** adds traced parts. |
| **Parts** | Photos and outlines laid onto the box as cutouts: the parts list, each part's bottom, Y offset and label, the tray-wide Y offset, round step, centrelines and printer, a to-scale preview with the split seams. See [The Parts tab](#the-parts-tab). |

Box, Cutout and Parts edit the same definition, so **Build box** on any of them builds the box with its cutouts and labels.
**Build baseplate** builds the Baseplate tab. *Open JSON* detects whether a file is a box or a baseplate and switches tab;
*Save JSON* saves whichever tab you are on. The menu command *Build from JSON file* does the same without opening the window.
`samples\baseplate-4x3.json` is an example baseplate with magnets, screws, slots and six clips.

### Baseplate details (measured from the site's own plate)

The baseplate geometry was read from a connectable 2 x 2 plate exported from gridfinitygenerator.com, so it follows that plate:

- **Height:** a 3 mm lower layer plus the 4.65 mm bin-shaped profile (2.15 chamfer, 1.8 vertical, 0.7 chamfer), 7.65 mm in all.
- **Lower layer:** every cell is open all the way through, apart from a rounded boss in each corner (4.5 mm radius, centred 13.5 mm from the cell centre).
- **Magnets:** a pocket in each boss, opening upward so the magnet drops in from the top. Defaults 6.2 mm diameter x 2.1 mm thick, with 0.9 mm of material under it. The lower layer thickens automatically if you choose a thicker magnet.
- **Screws:** a 3.5 mm hole through that 0.9 mm (or through the whole layer when magnets are off).
- **Connection slots:** a 20 mm notch (the 19.6 mm clip plus 0.4 mm) in the outer wall at the middle of every cell edge, with a stepped hook that traps the clip. Because they sit on the 42 mm grid, plates of different sizes line up. Each side can be switched off.
- **Clips:** the clip profile (4.3 mm wide, 3.6 mm tall, hollow underside, 19.6 mm long) comes from the site's Connection Clip STL. Build them from the **Clips** tab: one part holding as many clips as you ask for. (They are a separate build because a second part opening on top of the plate looked as if the plate had not been made.) The slot is cut in steps rather than with the slope the site uses, so test-print one joint before printing a big plate.

`WebAtTop` is still the number to change if bins fit too tight or loose.

## Using the API

```csharp
using Gridfinity.Core;
using Gridfinity.Sw;

var tray = new TrayDefinition
{
    Name = "SocketTray",
    UnitsX = 3, UnitsY = 2, HeightUnits = 3,
    Style = TrayStyle.SolidBlock,     // or OpenTray (walls, cavity, dividers, stacking lip)
    MagnetHoles = true,
    DefaultClearance = 0.4,
};
tray.Pockets.Add(Pocket.Round("1/2 in socket", 24.0, depth: 14));
tray.Pockets.Add(Pocket.SlotPocket("Ratchet", 150, 30, depth: 10));
tray.Pockets.Add(Pocket.Rect("Extension", 100, 14, depth: 8, cornerRadius: 2));

// Without SolidWorks: validate and inspect the plan.
CompileResult c = TrayCompiler.Compile(tray);       // auto-layout + validation, never mutates `tray`
Console.WriteLine(c.Report);                         // errors and warnings
foreach (var f in c.Plan.Features) Console.WriteLine(f.Name);

// With SolidWorks (swApp is an ISldWorks):
SwBuildResult r = new GridfinityBuilder(swApp).Build(tray, new SwBuildOptions { SaveAsPath = @"C:\Parts\SocketTray.sldprt" });
```

JSON in and out: `tray.ToJson()`, `TrayDefinition.FromJson(text)`. See `samples\`.

### Pockets

| Shape | `Width` | `Height` |
|---|---|---|
| `Rectangle` | length (local X) | width (local Y), optional `CornerRadius` |
| `Circle` | diameter | ignored |
| `Slot` | overall length | slot width |
| `Hex` | across flats | ignored |
| `Polygon` | (unused) | convex `Points` list, JSON only |
| `Outline` | (unused) | any `Points` outline plus `Holes`, `KeepIslands`; written by the outline importer |
| `Cradle` | part diameter (X) | part length (Y); half-round bottom, `Depth` is to the lowest point, no rotation |

Each pocket also has `Depth`, `Clearance` (overrides the tray default), `X`/`Y`/`RotationDeg` (used when
`AutoLayout` is off), `AllowRotate`, and an optional round finger notch (`NotchDiameter`, `NotchOffset`).

### Layout

- `AutoLayout` (default on) shelf-packs pockets, rotating them 90 degrees where that helps.
- `AutoGrowGrid` finds the smallest `UnitsX` x `UnitsY` that fits everything.
- Turn `AutoLayout` off to place pockets by hand with `X`, `Y`, `RotationDeg`.
- Validation errors (overlap, outside the usable area, too little material under a pocket, thin walls) stop the
  build before SolidWorks is touched; warnings are returned in `SwBuildResult.Warnings`.

## What gets built

Sketches are on the Front Plane, extruded along +Z, with the bin centred on X/Y = 0 (so the part appears on its
side in SolidWorks' default Y-up view; re-orient if you prefer).

1. Per cell, three extrudes form the base: 0.8 mm 45-degree chamfer, 1.8 mm vertical, 2.15 mm 45-degree chamfer
   (35.6 mm up to 41.5 mm, corner radius 0.8 up to 3.75 mm), using draft angles.
2. Body block from 4.75 mm to the top.
3. `OpenTray`: cavity cut (with dividers), then four cuts forming the 4.4 mm stacking lip if enabled.
4. Pockets and notches, one cut per depth.
5. Debossed labels (one text cut, 0.8 mm deep by default).
6. 6.5 x 2.4 mm magnet pockets at 13 mm from each cell centre.

Assumptions you can change in `GridfinitySpec.cs` / `TrayModeler.cs`: height units are measured to the top of
the walls with the lip added on top; the lip top land defaults to 0.8 mm (`LipTopInset`). The lip follows the
commonly published 0.7 / 1.8 / 1.9 mm profile; print a test stack before trusting fit.

## If the Gridfinity Outlines menu is missing

The add-in writes a progress log to `%LocalAppData%\GridfinityOutlines\Logs\Gridfinity_addin.log` (paste that path into File Explorer's address bar). It says whether
the command group and menu items were created. Check **Tools > Add-Ins** that *Gridfinity Outlines* is ticked, and look
under **Tools > Gridfinity Outlines** with a part open.

## First-run checklist

- **Compile errors.** Most likely spots are the long `FeatureExtrusion3` / `FeatureCut4` argument lists in
  `FeatureExecutor.cs`; the compiler will show the expected signature.
- **Direction probe.** Before each build the add-in makes a scratch part (closed again without saving),
  measures which way extrudes, cuts and start offsets go, and adapts. Anything unexpected shows up in the
  warnings. Set `SwBuildOptions.RunDirectionProbe = false` to skip it.
- **Overall size check.** After the build, the part box is compared with the planned size; a mismatch is
  reported as a warning.
- **Drafted extrudes with a start offset** (base chamfers, lip cuts) are the most likely place for SolidWorks to
  behave differently from what is assumed. If the base looks wrong, inspect the first three features.
- **Icons.** The commands are menu-only and have no icon list.

## Ideas for next steps

Label recesses or engraved text, scoops and fingertip cut-outs on open trays, baseplate generation, a table of
tool dimensions (for example the Quinn set) fed in as JSON, exporting STL/3MF straight from the build, true-shape nesting
of traced parts at any angle, and M3 screw holes in bins.

## Socket tray (1/2" drive)

`trays\Sockets_1-2in_ALL.json` is one tray holding all 73 sockets from `data\sockets_half_inch_drive.csv`. Open it with
**Tools > Gridfinity Outlines > Build from JSON file...** (about 130 grid cells, 73 cradles and 79 labels, so the build takes a
while; the cradles are cut from the Top plane, and the add-in first checks in a scratch part how that plane behaves). `trays\SUMMARY.md` has the sizes and `trays\PREVIEW_top_view.png` shows the layout.

- **Size:** 16 x 8 cells, 671.5 x 335.5 mm (26.4 x 13.2 in), 42 mm tall; limit used: 51.5 x 19.75 in.
- **Layout:** SAE on the left half, Metric on the right, everything in each half justified to the left. Top to bottom:
  6-point (wrapped onto two rows, small to large), 12-point, then the 3.125 in deep wells at the bottom (the front of
  the drawer). A title is centred above each section and each socket's size is centred below it. Rows line up across
  the two halves.
- **Sockets lie flat**, long axis front to back, each in a round-bottomed cradle (`Shape: Cradle`): a half-round trough
  with straight sides, sized to the socket's maximum diameter plus 0.4 mm total (0.2 mm per side) and its length plus 0.4 mm.
  0.4 mm is a safe FDM fit; 0.002 in (0.05 mm) would be tighter than a printer can hold. Change it with `--clearance`.
- **Level tops:** every socket top sits 47.5 mm above the tray base. A socket resting in its cradle has its top at *lowest
  point + diameter*, so `depth = tray height - 47.5 + diameter`. The lowest point of the largest cradle is 6 mm up (the
  4.75 mm feet plus 1.25 mm of floor), which stops the big sockets breaking through the bottom. The tray is 42 mm tall so
  every cradle is at least as deep as its radius; the sockets stand about 5.6 mm above the tray top.
- **Labels** are debossed 0.8 mm (four layers at 0.2 mm) for a colour change or paint fill.
- The SAE 12-point lengths (blank in the original sheet) use 1.500 in, as confirmed.

To change sizes, edit the CSV and run `python tools\make_socket_tray.py`, then `python tools\check_socket_tray.py`
(needs Python; not needed to use the supplied file). Options: `--max-width-in`, `--max-depth-in`, `--clearance`,
`--center-deep` (centre the deep rows instead), `--wrap 2,1,1` (rows for 6-point, 12-point, deep), `--optimize-all`
(also try wrapping the 12-point and deep rows; it saves only about 1.5 % of the area).

Label centring uses the widths of Arial Bold and assumes the sketch-text size is the font size. If the labels look
a different size than expected or sit off-centre, tell me what you see and I will adjust one number.
