using System.Collections.Generic;

namespace osuautodeafen.cs;

public class GraphData
{
    public List<Series> Series { get; set; }
    public List<double>? XAxis { get; set; }
    public List<double> YAxis { get; set; }
}

public class Series
{
    public string? Name { get; set; }
    public List<double> Data { get; set; }
}