using System.Threading.Tasks;
using osuautodeafen.cs;

public class SR
{
    private readonly TosuApi _tosuAPI;

    public SR(TosuApi tosuAPI)
    {
        _tosuAPI = tosuAPI;
    }

    public async Task<double> GetFullSR()
    {
        await _tosuAPI.ConnectAsync();
        return _tosuAPI.GetFullSR();
    }
}