using osuautodeafen.cs;

public class FCCalc
{
    private readonly TosuApi _tosuAPI;

    public FCCalc(TosuApi tosuAPI)
    {
        _tosuAPI = tosuAPI;
    }

    public bool IsFullCombo()
    {
        // if there are any misses or slider breaks, return false
        if (_tosuAPI.GetMissCount() > 0 || _tosuAPI.GetSBCount() > 0) return false;
        // if there are no misses and no slider breaks, return true
        return true;
    }
}