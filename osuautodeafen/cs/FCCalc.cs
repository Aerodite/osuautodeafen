using osuautodeafen;
using osuautodeafen.cs;

public class FCCalc
{
    private TosuApi _tosuAPI;

    public FCCalc(TosuApi tosuAPI)
    {
        _tosuAPI = tosuAPI;
    }

    public bool IsFullCombo()
    {
        // If there are any misses or slider breaks, return false
        if (_tosuAPI.GetMissCount() > 0 || _tosuAPI.GetSBCount() > 0)
        {
            return false;
        }
        // If there are no misses and no slider breaks, return true
        return true;
    }
}