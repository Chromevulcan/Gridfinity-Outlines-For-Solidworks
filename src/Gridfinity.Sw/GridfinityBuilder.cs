using System;
using System.Collections.Generic;
using System.IO;
using Gridfinity.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Gridfinity.Sw
{
    public sealed class SwBuildOptions
    {
        /// <summary>Part template (.prtdot). Empty = the default part template from Tools > Options.</summary>
        public string PartTemplatePath;

        /// <summary>If set, the finished part is saved here (.sldprt).</summary>
        public string SaveAsPath;

        /// <summary>Measure extrude/offset directions in a scratch part first. Recommended; costs about a second.</summary>
        public bool RunDirectionProbe = true;

        public bool ZoomToFit = true;
    }

    public sealed class SwBuildResult
    {
        public bool Success;
        public string Message;

        /// <summary>The finished part, left open in SolidWorks (also on failure after the part was created, for inspection).</summary>
        public ModelDoc2 Model;

        public ValidationReport Validation;
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Public entry point. Typical use from your own code or macro:
    /// <code>
    /// var def = new TrayDefinition { Name = "SocketTray", UnitsX = 3, UnitsY = 2, HeightUnits = 3 };
    /// def.Pockets.Add(Pocket.Round("1/2 in socket", 24.0, 20));
    /// var result = new GridfinityBuilder(swApp).Build(def);
    /// </code>
    /// </summary>
    public sealed class GridfinityBuilder
    {
        private readonly ISldWorks _app;

        public GridfinityBuilder(ISldWorks app)
        {
            if (app == null) throw new ArgumentNullException("app");
            _app = app;
        }

        /// <summary>Builds a box (tray or part holder, including its cutouts and labels).</summary>
        public SwBuildResult Build(TrayDefinition definition, SwBuildOptions options = null)
        {
            CompileResult compiled = TrayCompiler.Compile(definition);
            return Run(compiled.Plan, compiled.Report, "The tray definition has errors:\n", options);
        }

        /// <summary>Builds a baseplate.</summary>
        public SwBuildResult BuildBaseplate(BaseplateDefinition definition, SwBuildOptions options = null)
        {
            BaseplateCompileResult compiled = BaseplateModeler.Compile(definition);
            return Run(compiled.Plan, compiled.Report, "The baseplate definition has errors:\n", options);
        }

        /// <summary>Builds a part holding the connection clips.</summary>
        public SwBuildResult BuildClips(ClipDefinition definition, SwBuildOptions options = null)
        {
            ValidationReport report = definition.Validate();
            BuildPlan plan = report.IsValid ? BaseplateModeler.CreateClipPlan(definition.Name, definition.ClipCount, definition.ClipLength) : null;
            return Run(plan, report, "The clip definition has errors:\n", options);
        }

        private SwBuildResult Run(BuildPlan plan, ValidationReport report, string errorPrefix, SwBuildOptions options)
        {
            if (options == null) options = new SwBuildOptions();
            var result = new SwBuildResult();
            result.Validation = report;
            foreach (Issue w in report.Warnings) result.Warnings.Add(w.Message);

            if (plan == null || !report.IsValid)
            {
                result.Message = errorPrefix + report;
                return result;
            }

            string template = options.PartTemplatePath;
            if (string.IsNullOrEmpty(template))
                template = _app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            if (string.IsNullOrEmpty(template) || !File.Exists(template))
            {
                result.Message = "No part template found. Set one under Tools > Options > Default Templates, or pass SwBuildOptions.PartTemplatePath.";
                return result;
            }

            DirectionFlags flags = options.RunDirectionProbe ? DirectionProbe.Run(_app, template, plan.Features.Exists(f => f.Plane == SketchPlane.Top)) : new DirectionFlags();
            result.Warnings.AddRange(flags.Notes);

            ModelDoc2 model = _app.NewDocument(template, 0, 0, 0) as ModelDoc2;
            if (model == null)
            {
                result.Message = "SolidWorks could not create a new part.";
                return result;
            }
            result.Model = model;

            try
            {
                model.Extension.SetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitsLinear, 0, (int)swLengthUnit_e.swMM);
                model.FeatureManager.EnableFeatureTree = false; // faster; restored below

                foreach (FeaturePlan fp in plan.Features)
                {
                    double before = FeatureExecutor.Volume(model);
                    try
                    {
                        // Sketch text may need the feature tree to be live, so switch it back on just for the text feature.
                        if (fp.Texts.Count > 0) model.FeatureManager.EnableFeatureTree = true;
                        try { FeatureExecutor.Execute(model, fp, flags); }
                        finally { if (fp.Texts.Count > 0) model.FeatureManager.EnableFeatureTree = false; }
                    }
                    catch (BuildException ex) when (fp.Optional)
                    {
                        result.Warnings.Add("Skipped '" + fp.Name + "': " + ex.Message);
                        continue;
                    }
                    double after = FeatureExecutor.Volume(model);

                    if (fp.Op == FeatureOp.Cut && before - after < 1e-12)
                        result.Warnings.Add("Feature '" + fp.Name + "' removed no material; it may have cut the wrong way or missed the part.");
                    if (fp.Op == FeatureOp.Boss && after - before < 1e-12 && fp.Name != "Body")
                        result.Warnings.Add("Feature '" + fp.Name + "' added no material.");
                }

                model.FeatureManager.EnableFeatureTree = true;
                model.EditRebuild3();

                SetProperties(model, plan);
                CheckOverallSize(model, plan, result);

                if (options.ZoomToFit)
                {
                    model.ShowNamedView2("*Isometric", (int)swStandardViews_e.swIsometricView);
                    model.ViewZoomtofit2();
                }

                if (!string.IsNullOrEmpty(options.SaveAsPath))
                {
                    int errors = 0, warnings = 0;
                    bool saved = model.Extension.SaveAs(options.SaveAsPath,
                        (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                        (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                        null, ref errors, ref warnings);
                    if (!saved) result.Warnings.Add("Save failed (SolidWorks error code " + errors + ").");
                }

                result.Success = true;
                result.Message = "Built " + plan.Features.Count + " features for '" + plan.Name + "'.";
            }
            catch (BuildException ex)
            {
                result.Message = ex.Message;
            }
            catch (Exception ex)
            {
                result.Message = "Unexpected error: " + ex.Message;
            }
            finally
            {
                try { model.FeatureManager.EnableFeatureTree = true; } catch { }
            }
            return result;
        }

        private static void SetProperties(ModelDoc2 model, BuildPlan plan)
        {
            try
            {
                CustomPropertyManager cpm = model.Extension.CustomPropertyManager[""];
                foreach (KeyValuePair<string, string> kv in plan.Properties)
                    cpm.Add3(kv.Key, (int)swCustomInfoType_e.swCustomInfoText, kv.Value,
                             (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            }
            catch { /* properties are a nicety */ }
        }

        // The part must sit at X/Y centred on the origin and Z from 0 to the planned height.
        private static void CheckOverallSize(ModelDoc2 model, BuildPlan plan, SwBuildResult result)
        {
            PartDoc part = model as PartDoc;
            double[] box = part == null ? null : part.GetPartBox(true) as double[];
            if (box == null || box.Length < 6) return;

            double w = (box[3] - box[0]) * 1000, d = (box[4] - box[1]) * 1000;
            double zMin = box[2] * 1000, zMax = box[5] * 1000;
            const double tol = 0.05;

            if (Math.Abs(w - plan.ExpectedWidth) > tol || Math.Abs(d - plan.ExpectedDepth) > tol)
                result.Warnings.Add("Overall footprint is " + w.ToString("0.##") + " x " + d.ToString("0.##") + " mm; expected " +
                                    plan.ExpectedWidth.ToString("0.##") + " x " + plan.ExpectedDepth.ToString("0.##") + " mm.");
            if (Math.Abs(zMin) > tol || Math.Abs(zMax - plan.ExpectedHeight) > tol)
                result.Warnings.Add("Part spans Z " + zMin.ToString("0.##") + " to " + zMax.ToString("0.##") + " mm; expected 0 to " +
                                    plan.ExpectedHeight.ToString("0.##") + " mm.");
        }
    }
}
