namespace Gridfinity.Core
{
    public sealed class CompileResult
    {
        /// <summary>Copy of the input after auto-layout / auto-grow; this is what the plan was built from.</summary>
        public TrayDefinition Resolved;
        public ValidationReport Report = new ValidationReport();
        public LayoutResult Layout;

        /// <summary>Null when validation found errors.</summary>
        public BuildPlan Plan;

        public bool Success { get { return Plan != null && Report.IsValid; } }
    }

    /// <summary>One call from tray definition to validated build plan. Never mutates its input.</summary>
    public static class TrayCompiler
    {
        public static CompileResult Compile(TrayDefinition input)
        {
            var t = input.Clone();
            var result = new CompileResult { Resolved = t };

            if (t.AutoGrowGrid)
            {
                if (!TrayPlanner.SuggestGrid(t))
                    result.Report.Error("Pockets do not fit in any grid up to " + System.Math.Min(12, t.MaxUnitsX) + " x " + System.Math.Min(12, t.MaxUnitsY) +
                                        " units. Raise MaxUnitsX / MaxUnitsY, or split the parts over two trays.");
                result.Layout = TrayPlanner.AutoLayout(t);
            }
            else if (t.AutoLayout)
            {
                result.Layout = TrayPlanner.AutoLayout(t);
                if (!result.Layout.Fits)
                    result.Report.Error(result.Layout.Unplaced.Count + " pocket(s) do not fit in " + t.UnitsX + " x " + t.UnitsY +
                                        " units (e.g. '" + result.Layout.Unplaced[0].Name + "'). Enlarge the grid or enable AutoGrowGrid.");
            }

            var validation = TrayValidator.Validate(t);
            result.Report.Issues.AddRange(validation.Issues);

            if (result.Report.IsValid) result.Plan = TrayModeler.CreatePlan(t);
            return result;
        }
    }
}
