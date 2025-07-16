using System;
using System.Collections.Generic;
using LiveChartsCore.SkiaSharpView;

namespace osuautodeafen.cs.StrainGraph;

public class RectangularSectionComparer : IEqualityComparer<RectangularSection>
{
    public bool Equals(RectangularSection? x, RectangularSection? y)
    {
        if (x == null || y == null) return false;
        return x.Xi == y.Xi && x.Xj == y.Xj && x.Yi == y.Yi && x.Yj == y.Yj;
    }

    public int GetHashCode(RectangularSection obj)
    {
        return HashCode.Combine(obj.Xi, obj.Xj, obj.Yi, obj.Yj);
    }
}