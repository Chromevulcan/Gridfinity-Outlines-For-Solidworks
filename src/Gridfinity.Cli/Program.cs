using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Gridfinity.Core;

namespace Gridfinity.Cli
{
    /// <summary>
    /// gridfinity-outlines: outline-app exports in, Gridfinity tray JSON out.
    ///
    ///   gridfinity-outlines tray  wrench.dxf pliers.svg tools_dxf.zip -o tools.json [options]
    ///   gridfinity-outlines watch C:\Users\me\Downloads --out-dir C:\Trays [options]
    ///   gridfinity-outlines check tools.json
    /// </summary>
    public static class Program
    {
        private const string Usage = @"gridfinity-outlines - turn outline-app exports (DXF, SVG or ZIP) into Gridfinity trays

Usage:
  gridfinity-outlines tray <file|folder>... [-o tray.json] [options]
      Puts every outline from the given files into one tray, laid out on the smallest grid that fits.
  gridfinity-outlines watch <folder> [--out-dir <folder>] [options]
      Waits for new .dxf/.svg/.zip files (for example your Downloads folder) and writes a tray for each one.
  gridfinity-outlines check <tray.json>
      Lays out and validates an existing tray and writes its preview.

Build the JSON in SolidWorks with Tools > Gridfinity Outlines > Build from JSON file...

Pocket options:
  --depth <mm>          pocket depth (default 12)
  --clearance <mm>      total clearance around each outline (default 1.0)
  --keep-islands        leave material inside holes in an outline
  --notch <mm>          finger notch diameter at each pocket centre (default 0 = none)
  --no-rotate           do not let auto-layout turn pockets by 90 degrees
Shape options:
  --simplify <mm>       drop points within this distance of a straight line (default 0.1)
  --no-align            keep the angle the part had in the photo
  --min-area <mm2>      ignore traced shapes smaller than this (default 20)
  --scale <factor>      extra scale after the file's units (default 1)
  --mirror              mirror left to right
Tray options:
  --name <text>         tray name (default: first file name)
  --units <X>x<Y>       fixed grid size instead of the smallest that fits
  --max-units <X>x<Y>   largest grid auto-grow may use (default 5x5 = 209.5 mm, fits most print beds)
  --height-units <n>    height in 7 mm units (default: just enough for the depth)
  --open-tray           walled bin with the pockets cut into its floor, instead of a solid block
  --lip                 stacking lip (open tray only)
  --magnets             magnet holes under every cell
  --labels              print each pocket's name under it (debossed text)
  --spacing <mm>        gap between pockets (default 2.0)
  --margin <mm>         gap between pockets and the edge (default 1.6)
Output:
  -o, --out <file>      tray JSON (default: <name>.json next to the first input)
  --out-dir <folder>    where watch writes its trays (default: the watched folder)
  --no-preview          do not write the <name>.preview.svg top view
";

        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help" || args[0] == "help")
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 1 : 0;
            }

            try
            {
                var a = new Args(args.Skip(1).ToArray());
                switch (args[0])
                {
                    case "tray": return Tray(a);
                    case "watch": return Watch(a);
                    case "check": return Check(a);
                    default:
                        Console.Error.WriteLine("Unknown command '" + args[0] + "'. Run with --help.");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                return 2;
            }
        }

        // ---- Commands --------------------------------------------------------------------------------

        private static int Tray(Args a)
        {
            var inputs = ExpandInputs(a.Positional);
            if (inputs.Count == 0) throw new ArgumentException("No .dxf, .svg or .zip files given.");

            string name = a.Get("--name") ?? Path.GetFileNameWithoutExtension(inputs[0]);
            string output = a.Get("-o") ?? a.Get("--out") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputs[0])), Safe(name) + ".json");
            return BuildTray(inputs, name, output, a) ? 0 : 3;
        }

        private static int Check(Args a)
        {
            if (a.Positional.Count != 1) throw new ArgumentException("check takes one tray JSON file.");
            string path = a.Positional[0];
            var tray = TrayDefinition.FromJson(File.ReadAllText(path));
            CompileResult c = TrayCompiler.Compile(tray);
            Report(c);
            if (!a.Has("--no-preview")) WritePreview(c.Resolved, Path.ChangeExtension(path, ".preview.svg"));
            return c.Success ? 0 : 3;
        }

        private static int Watch(Args a)
        {
            if (a.Positional.Count != 1) throw new ArgumentException("watch takes one folder.");
            string folder = Path.GetFullPath(a.Positional[0]);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
            string outDir = a.Get("--out-dir") ?? folder;
            Directory.CreateDirectory(outDir);

            var seen = new HashSet<string>(Directory.GetFiles(folder).Where(IsOutlineFile), StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("Watching " + folder + " for new .dxf/.svg/.zip files. Trays go to " + outDir + ". Ctrl+C to stop.");

            // Polling rather than FileSystemWatcher: browsers write downloads under a temporary name and rename them,
            // and polling also works on network and cloud-synced folders.
            var stop = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; stop.Set(); };
            while (!stop.WaitOne(1000))
            {
                foreach (var file in Directory.GetFiles(folder).Where(IsOutlineFile))
                {
                    if (seen.Contains(file) || !IsComplete(file)) continue;
                    seen.Add(file);
                    string name = Path.GetFileNameWithoutExtension(file);
                    Console.WriteLine();
                    Console.WriteLine("New file: " + Path.GetFileName(file));
                    try { BuildTray(new List<string> { file }, name, Path.Combine(outDir, Safe(name) + ".json"), a); }
                    catch (Exception ex) { Console.Error.WriteLine("  Could not use it: " + ex.Message); }
                }
            }
            return 0;
        }

        // ---- Shared ----------------------------------------------------------------------------------

        private static bool BuildTray(List<string> inputs, string name, string output, Args a)
        {
            var options = ImportOptions(a);
            var notes = new List<string>();
            var shapes = new List<TracedShape>();
            foreach (var f in inputs) shapes.AddRange(OutlineImporter.ReadFile(f, notes));

            TrayDefinition tray = OutlineImporter.CreateTray(name, shapes, options, notes);
            ApplyOverrides(tray, a);

            foreach (var n in notes) Console.WriteLine("  " + n);
            if (tray.Pockets.Count == 0) { Console.Error.WriteLine("  No usable outlines; nothing written."); return false; }

            CompileResult c = OutlineImporter.Resolve(tray, options.NameLabels);
            TrayDefinition resolved = c.Resolved;

            Report(c);
            // Write the laid-out tray with the layout fixed, so SolidWorks builds exactly what the preview shows.
            File.WriteAllText(output, Fixed(resolved).ToJson());
            Console.WriteLine("  Wrote " + output);
            if (!a.Has("--no-preview")) WritePreview(resolved, Path.ChangeExtension(output, ".preview.svg"));
            return c.Success;
        }

        private static TrayDefinition Fixed(TrayDefinition t)
        {
            var copy = t.Clone();
            copy.AutoLayout = false;
            copy.AutoGrowGrid = false;
            return copy;
        }

        private static OutlineImportOptions ImportOptions(Args a)
        {
            return new OutlineImportOptions
            {
                Depth = a.Double("--depth", 12),
                Clearance = a.Double("--clearance", 1.0),
                KeepIslands = a.Has("--keep-islands"),
                NotchDiameter = a.Double("--notch", 0),
                AllowRotate = !a.Has("--no-rotate"),
                SimplifyTolerance = a.Double("--simplify", 0.1),
                AlignToAxes = !a.Has("--no-align"),
                MinArea = a.Double("--min-area", 20),
                MaxUnitsX = GridSize(a.Get("--max-units"), 5, true),
                MaxUnitsY = GridSize(a.Get("--max-units"), 5, false),
                Scale = a.Double("--scale", 1),
                Mirror = a.Has("--mirror"),
                Style = a.Has("--open-tray") ? TrayStyle.OpenTray : TrayStyle.SolidBlock,
                MagnetHoles = a.Has("--magnets"),
                StackingLip = a.Has("--lip"),
                NameLabels = a.Has("--labels"),
            };
        }

        // Explicit layout overrides on top of what CreateTray chose.
        private static void ApplyOverrides(TrayDefinition t, Args a)
        {
            t.PocketSpacing = a.Double("--spacing", t.PocketSpacing);
            t.EdgeMargin = a.Double("--margin", t.EdgeMargin);

            string units = a.Get("--units");
            if (units != null)
            {
                t.UnitsX = GridSize(units, 1, true);
                t.UnitsY = GridSize(units, 1, false);
                t.AutoGrowGrid = false;
            }
            if (a.Get("--height-units") != null) t.HeightUnits = (int)a.Double("--height-units", t.HeightUnits);
        }

        private static int GridSize(string text, int fallback, bool x)
        {
            if (text == null) return fallback;
            var parts = text.ToLowerInvariant().Split('x');
            int v;
            if (parts.Length != 2 || !int.TryParse(parts[x ? 0 : 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < 1)
                throw new ArgumentException("Grid sizes look like 4x3, not '" + text + "'.");
            return v;
        }

        private static void Report(CompileResult c)
        {
            var t = c.Resolved;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Tray '{0}': {1} x {2} cells ({3:0.#} x {4:0.#} mm), {5} height units ({6:0.##} mm), {7} pocket(s).",
                t.Name, t.UnitsX, t.UnitsY, t.OuterWidth, t.OuterDepth, t.HeightUnits, t.TotalHeight, t.Pockets.Count));
            foreach (var i in c.Report.Issues) Console.WriteLine("  " + i);
            Console.WriteLine(c.Success ? "  Ready to build." : "  Cannot build yet; see the errors above.");
        }

        private static void WritePreview(TrayDefinition t, string path)
        {
            File.WriteAllText(path, TrayPreview.ToSvg(t));
            Console.WriteLine("  Preview " + path);
        }

        private static List<string> ExpandInputs(List<string> items)
        {
            var list = new List<string>();
            foreach (var item in items)
            {
                if (Directory.Exists(item)) list.AddRange(Directory.GetFiles(item).Where(IsOutlineFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                else if (File.Exists(item)) list.Add(item);
                else throw new FileNotFoundException("Not found: " + item);
            }
            return list;
        }

        private static bool IsOutlineFile(string f)
        {
            string e = Path.GetExtension(f).ToLowerInvariant();
            return (e == ".dxf" || e == ".svg" || e == ".zip") && !f.EndsWith(".preview.svg", StringComparison.OrdinalIgnoreCase);
        }

        // A download still being written is locked or still growing.
        private static bool IsComplete(string file)
        {
            try
            {
                long size = new FileInfo(file).Length;
                Thread.Sleep(300);
                if (new FileInfo(file).Length != size || size == 0) return false;
                using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None)) return true;
            }
            catch (IOException) { return false; }
        }

        private static string Safe(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private sealed class Args
        {
            public readonly List<string> Positional = new List<string>();
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
            private readonly HashSet<string> _flags = new HashSet<string>();
            private static readonly HashSet<string> WithValue = new HashSet<string>
            {
                "--depth", "--clearance", "--notch", "--simplify", "--min-area", "--scale", "--name", "--units",
                "--height-units", "--max-units", "--spacing", "--margin", "-o", "--out", "--out-dir"
            };

            public Args(string[] args)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string s = args[i];
                    if (WithValue.Contains(s))
                    {
                        if (i + 1 >= args.Length) throw new ArgumentException(s + " needs a value.");
                        _values[s] = args[++i];
                    }
                    else if (s.StartsWith("-")) _flags.Add(s);
                    else Positional.Add(s);
                }
            }

            public bool Has(string flag) { return _flags.Contains(flag); }
            public string Get(string key) { string v; return _values.TryGetValue(key, out v) ? v : null; }
            public double Double(string key, double fallback)
            {
                string v = Get(key);
                if (v == null) return fallback;
                double d;
                if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) throw new ArgumentException(key + " must be a number.");
                return d;
            }
        }
    }
}
