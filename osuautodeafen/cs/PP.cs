using System.Threading.Tasks;
using osuautodeafen;
using osuautodeafen.cs;

public class PP
{
    private readonly TosuApi _tosuAPI;

    public PP(TosuApi tosuAPI)
    {
        _tosuAPI = tosuAPI;
    }

    public async Task<double> GetMaxPP()
    {
        await _tosuAPI.ConnectAsync();
        return _tosuAPI.GetMaxPP();
    }
}