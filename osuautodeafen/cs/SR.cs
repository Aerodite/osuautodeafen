using System.Threading.Tasks;
using osuautodeafen;

public class SR
{
    private readonly TosuAPI _tosuAPI;

    public SR(TosuAPI tosuAPI)
    {
        _tosuAPI = tosuAPI;
    }

    public async Task<double> GetFullSR()
    {
        await _tosuAPI.ConnectAsync();
        return _tosuAPI.GetFullSR();
    }
}