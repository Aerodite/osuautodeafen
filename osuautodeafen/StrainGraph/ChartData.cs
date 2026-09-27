using System.Collections.ObjectModel;
using LiveChartsCore.Defaults;

namespace osuautodeafen.StrainGraph;

public class ChartData
{
    public ObservableCollection<ObservablePoint> Series1Values { get; set; } = new();
    public ObservableCollection<ObservablePoint> Series2Values { get; set; } = new();
    public ObservableCollection<ObservablePoint> Series3Values { get; set; } = new();
}