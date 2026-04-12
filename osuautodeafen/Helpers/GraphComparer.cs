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
            var sa = a.Series[i];
            var sb = b.Series[i];

            if (sa.Data is not null && sb.Data is not null && sa.Data.Count != sb.Data.Count)
                return false;

            if (sa.Data != null)
                for (int j = 0; j < sa.Data.Count; j++)
                {
                    if (sb.Data != null && sa.Data[j] != sb.Data[j])
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