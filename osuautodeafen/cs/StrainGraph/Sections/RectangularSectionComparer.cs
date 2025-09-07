using System;
using System.Collections.Generic;
using LiveChartsCore.SkiaSharpView;

namespace osuautodeafen.cs.StrainGraph;

public class RectangularSectionComparer : IEqualityComparer<RectangularSection>
{
    /// <summary>
    ///     Compares two RectangularSection objects for equality based on their coordinates
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public bool Equals(RectangularSection? x, RectangularSection? y)
    {
        if (x == null || y == null) return false;
        return x.Xi == y.Xi && x.Xj == y.Xj && x.Yi == y.Yi && x.Yj == y.Yj;
    }

    /// <summary>
    ///     Generates a hash code for a RectangularSection object based on its coordinates
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public int GetHashCode(RectangularSection obj)
    {
        return HashCode.Combine(obj.Xi, obj.Xj, obj.Yi, obj.Yj);
    }
}