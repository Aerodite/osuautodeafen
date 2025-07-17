using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace osuautodeafen.cs.StrainGraph;

public class TimePeriod
{
    public int Start { get; set; }
    public int End { get; set; }
}

public class KiaiTime : TimePeriod
{
}

public class BreakPeriod : TimePeriod
{
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public double StartPercentage { get; set; }
    public double EndPercentage { get; set; }
}

public class KiaiTimes
{
    private bool _isInKiaiPeriod;
    public List<KiaiTime> Times { get; } = new();
    public event Action? KiaiPeriodEntered;
    public event Action? KiaiPeriodExited;

    public async Task<List<KiaiTime>> ParseKiaiTimesAsync(string osuFilePath)
    {
        Times.Clear();
        var lines = await File.ReadAllLinesAsync(osuFilePath);
        var inTimingPoints = false;
        int? currentKiaiStart = null;
        var rawPeriods = new List<KiaiTime>();

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
                if (parts.Length < 8) continue;
                if (!int.TryParse(parts[0], out var time)) continue;
                if (!int.TryParse(parts[7], out var effects)) continue;

                var kiai = (effects & 1) == 1;
                if (kiai && currentKiaiStart == null)
                {
                    currentKiaiStart = time;
                }
                else if (!kiai && currentKiaiStart != null)
                {
                    rawPeriods.Add(new KiaiTime { Start = currentKiaiStart.Value, End = time });
                    currentKiaiStart = null;
                }
            }
        }

        if (currentKiaiStart != null)
            rawPeriods.Add(new KiaiTime { Start = currentKiaiStart.Value, End = int.MaxValue });

        Times.AddRange(MergePeriods(rawPeriods));
        return Times;
    }

    public bool IsKiaiPeriod(int currentTime)
    {
        foreach (var kiai in Times)
            if (currentTime >= kiai.Start && currentTime < kiai.End)
                return true;
        return false;
    }
    
    public void UpdateKiaiPeriodState(int currentTime)
    {
        var currentlyInKiai = IsKiaiPeriod(currentTime);
        if (currentlyInKiai != _isInKiaiPeriod)
        {
            _isInKiaiPeriod = currentlyInKiai;
            if (currentlyInKiai)
                KiaiPeriodEntered?.Invoke();
            else
                KiaiPeriodExited?.Invoke();
        }
    }

    private static List<KiaiTime> MergePeriods(List<KiaiTime> periods)
    {
        if (periods.Count == 0) return new List<KiaiTime>();
        var sorted = periods.OrderBy(p => p.Start).ToList();
        var merged = new List<KiaiTime> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            var last = merged[^1];
            var curr = sorted[i];
            if (curr.Start <= last.End)
                last.End = Math.Max(last.End, curr.End);
            else
                merged.Add(curr);
        }

        return merged;
    }
}

public class BreakPeriodCalculator
{
    private bool _isInBreakPeriod;
    public List<BreakPeriod> BreakPeriods { get; } = new();
    public event Action? BreakPeriodEntered;
    public event Action? BreakPeriodExited;

    public async Task<List<BreakPeriod>> ParseBreakPeriodsAsync(string osuFilePath, List<double> xAxis,
        List<double> yAxis)
    {
        BreakPeriods.Clear();
        var lines = await File.ReadAllLinesAsync(osuFilePath);
        var inBreakPeriodSection = false;
        var totalPoints = xAxis.Count;
        var rawPeriods = new List<BreakPeriod>();

        foreach (var line in lines)
        {
            if (line.StartsWith("//Break Periods"))
            {
                inBreakPeriodSection = true;
                continue;
            }

            if (inBreakPeriodSection)
            {
                if (line.StartsWith("//")) break;
                var parts = line.Split(',');
                if (parts.Length == 3 && parts[0] == "2")
                    if (double.TryParse(parts[1], out var start) && double.TryParse(parts[2], out var end))
                    {
                        var startIndex = FindClosestIndex(xAxis, start);
                        var endIndex = FindClosestIndex(xAxis, end);
                        var startPercentage = startIndex / (double)totalPoints * 100;
                        var endPercentage = endIndex / (double)totalPoints * 100;
                        rawPeriods.Add(new BreakPeriod
                        {
                            Start = (int)start,
                            End = (int)end,
                            StartIndex = startIndex,
                            EndIndex = endIndex,
                            StartPercentage = startPercentage,
                            EndPercentage = endPercentage
                        });
                    }
            }
        }

        BreakPeriods.AddRange(MergePeriods(rawPeriods));
        return BreakPeriods;
    }

    public bool IsBreakPeriod(TosuApi tosuApi)
    {
        return tosuApi.IsBreakPeriod();
    }

    public void UpdateBreakPeriodState(TosuApi tosuApi)
    {
        var currentlyInBreak = IsBreakPeriod(tosuApi);
        if (currentlyInBreak != _isInBreakPeriod)
        {
            _isInBreakPeriod = currentlyInBreak;
            if (currentlyInBreak)
                BreakPeriodEntered?.Invoke();
            else
                BreakPeriodExited?.Invoke();
        }
    }

    private static int FindClosestIndex(List<double> xAxis, double value)
    {
        var closestIndex = 0;
        var smallestDifference = double.MaxValue;
        for (var i = 0; i < xAxis.Count; i++)
        {
            var difference = Math.Abs(xAxis[i] - value);
            if (difference < smallestDifference)
            {
                smallestDifference = difference;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private static List<BreakPeriod> MergePeriods(List<BreakPeriod> periods)
    {
        if (periods.Count == 0) return new List<BreakPeriod>();
        var sorted = periods.OrderBy(p => p.Start).ToList();
        var merged = new List<BreakPeriod> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            var last = merged[^1];
            var curr = sorted[i];
            if (curr.Start <= last.End)
            {
                last.End = Math.Max(last.End, curr.End);
                last.EndIndex = Math.Max(last.EndIndex, curr.EndIndex);
                last.EndPercentage = Math.Max(last.EndPercentage, curr.EndPercentage);
            }
            else
            {
                merged.Add(curr);
            }
        }

        return merged;
    }
}