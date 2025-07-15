using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace osuautodeafen.cs.StrainGraph;

public class KiaiTime
{
    public int Start { get; set; }
    public int End { get; set; }
}

public class KiaiTimes
{
    public List<KiaiTime> Times { get; } = new();

    public async Task<List<KiaiTime>> ParseKiaiTimesAsync(string osuFilePath)
    {
        Times.Clear();
        var lines = await File.ReadAllLinesAsync(osuFilePath);
        var inTimingPoints = false;
        int? currentKiaiStart = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("[TimingPoints]"))
            {
                inTimingPoints = true;
                continue;
            }
            if (inTimingPoints)
            {
                if (line.StartsWith("[") && !line.StartsWith("[TimingPoints]"))
                    break;

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 8)
                    continue;

                if (!int.TryParse(parts[0], out var time))
                    continue;

                if (!int.TryParse(parts[7], out var effects))
                    continue;

                bool kiai = (effects & 1) == 1;

                if (kiai && currentKiaiStart == null)
                {
                    currentKiaiStart = time;
                }
                else if (!kiai && currentKiaiStart != null)
                {
                    Times.Add(new KiaiTime
                    {
                        Start = currentKiaiStart.Value,
                        End = time
                    });
                    currentKiaiStart = null;
                }
            }
        }
        
        if (currentKiaiStart != null)
        {
            Times.Add(new KiaiTime
            {
                Start = currentKiaiStart.Value,
                End = currentKiaiStart.Value 
            });
        }

        return Times;
    }
}