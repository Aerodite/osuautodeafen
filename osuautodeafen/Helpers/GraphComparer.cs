using System.Collections.Generic;
using osuautodeafen.Tosu;

namespace osuautodeafen.Helpers;

public class GraphComparer : IEqualityComparer<TosuApi.GraphDataModel>
{
    private static bool GraphEquals(TosuApi.GraphDataModel a, TosuApi.GraphDataModel b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Series.Count != b.Series.Count) return false;

        for (int i = 0; i < a.Series.Count; i++)
        {
            var seriesA = a.Series[i];
            var seriesB = b.Series[i];

            if (seriesA.Data is not null && seriesB.Data is not null && seriesA.Data.Count != seriesB.Data.Count)
                return false;

            if (seriesA.Data != null)
                for (int j = 0; j < seriesA.Data.Count; j++)
                {
                    if (seriesB.Data != null && seriesA.Data[j] != seriesB.Data[j])
                        return false;
                }
        }

        return true;
    }

    public bool Equals(TosuApi.GraphDataModel? x, TosuApi.GraphDataModel? y)
        => GraphEquals(x!, y!);

    public int GetHashCode(TosuApi.GraphDataModel obj)
        => 0;
}