using System;

namespace osuautodeafen.Helpers;

public static class Extensions
{
    public static void SetValueSafe<T>(this T? obj, Action<T> setter) where T : class
    {
        if (obj != null) setter(obj);
    }
}