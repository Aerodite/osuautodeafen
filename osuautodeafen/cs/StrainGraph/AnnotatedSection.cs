using LiveChartsCore.SkiaSharpView;

namespace osuautodeafen.cs.StrainGraph;

public class AnnotatedSection : RectangularSection
{
    public string? SectionType { get; set; }
    public double StartTime { get; set; }
    public double EndTime { get; set; }
}