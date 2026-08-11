using System.Collections.Generic;
using System.Linq;
using osuautodeafen.Tosu;

namespace osuautodeafen.Helpers;

internal sealed class GraphComparer : IEqualityComparer<TosuApi.GraphDataModel>
{
    public static readonly GraphComparer Instance = new();

    public bool Equals(
        TosuApi.GraphDataModel? a,
        TosuApi.GraphDataModel? b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a is null || b is null)
            return false;

        if (!a.XAxis.SequenceEqual(b.XAxis))
            return false;

        if (a.Series.Count != b.Series.Count)
            return false;

        for (int i = 0; i < a.Series.Count; i++)
        {
            var seriesA = a.Series[i];
            var seriesB = b.Series[i];

            if (seriesA.Name != seriesB.Name)
                return false;

            if (!seriesA.Data.SequenceEqual(seriesB.Data))
                return false;
        }

        return true;
    }

    public int GetHashCode(TosuApi.GraphDataModel obj)
    {
        return 0;
    }
}