using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gridfinity.Core;

namespace Gridfinity.Trace
{
    /// <summary>Reads outlines from any supported file: a photo or scan (traced here), or a DXF, SVG or ZIP.</summary>
    public static class ShapeSources
    {
        public static readonly string FileFilter =
            "Photos and outlines|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp;*.dxf;*.svg;*.zip|" +
            "Photos and scans (*.jpg;*.png;*.tif;...)|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp|" +
            "Outlines (*.dxf;*.svg;*.zip)|*.dxf;*.svg;*.zip|All files (*.*)|*.*";

        public static bool IsSupported(string path)
        {
            string e = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            return PhotoTracer.IsImage(path) || e == ".dxf" || e == ".svg" || e == ".zip";
        }

        /// <summary>
        /// The shapes in one file, corrected by the scale reference if one is set. For a photo,
        /// <paramref name="overlayPath"/> receives the tracing preview.
        /// </summary>
        public static List<TracedShape> Read(string path, OutlineImportOptions o, List<string> notes, out string overlayPath)
        {
            overlayPath = null;
            List<TracedShape> shapes;
            if (PhotoTracer.IsImage(path))
            {
                var r = PhotoTracer.Trace(path, o, notes);
                overlayPath = r.OverlayPath;
                shapes = r.Shapes;
            }
            else shapes = OutlineImporter.ReadFile(path, notes);
            // Each photo has its own scale, so the reference is found and applied file by file.
            ScaleReferences.Apply(shapes, o, Path.GetFileNameWithoutExtension(path), notes);
            return shapes;
        }

        public static List<TracedShape> Read(string path, OutlineImportOptions o, List<string> notes)
        {
            string ignored;
            return Read(path, o, notes, out ignored);
        }
    }
}
