using System.Threading.Tasks;
using osuautodeafen;

public class PP
{
    private readonly TosuAPI _tosuAPI;

    public PP(TosuAPI tosuAPI)
    {
        _tosuAPI = tosuAPI;
    }

    public async Task<double> GetMaxPP()
    {
        await _tosuAPI.ConnectAsync();
        return _tosuAPI.GetMaxPP();
    }
}