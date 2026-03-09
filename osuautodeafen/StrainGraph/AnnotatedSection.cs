using LiveChartsCore.SkiaSharpView;

namespace osuautodeafen.cs.StrainGraph;

/// <summary>
///     An extended class of RectangularSection but with additional metadata for annotations (tooltips)
/// </summary>
public class AnnotatedSection : RectangularSection
{
    public string? SectionType { get; set; }
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public bool Tooltip { get; set; } = true;
}