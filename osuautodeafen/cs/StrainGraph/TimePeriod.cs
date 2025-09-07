using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace osuautodeafen.cs.StrainGraph;

/// <summary>
///     Represents a generic time period with start and end times
/// </summary>
public class TimePeriod
{
    public int Start { get; set; }
    public int End { get; set; }
}

/// <summary>
///     Represents a kiai time period with start and end times
/// </summary>
public class KiaiTime : TimePeriod
{
}

/// <summary>
///     Represents a break period with additional properties for indexing and percentage calculations
/// </summary>
public class BreakPeriod : TimePeriod
{
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public double StartPercentage { get; set; }
    public double EndPercentage { get; set; }
}

/// <summary>
///     Manages kiai time periods parsed from an osu! beatmap file
/// </summary>
public class KiaiTimes
{
    private bool _isInKiaiPeriod;
    public List<KiaiTime> Times { get; } = new();
    public event Action? KiaiPeriodEntered;
    public event Action? KiaiPeriodExited;

    /// <summary>
    ///     Parses kiai times from the specified osu! beatmap file
    /// </summary>
    /// <param name="osuFilePath"></param>
    /// <returns></returns>
    public async Task<List<KiaiTime>> ParseKiaiTimesAsync(string? osuFilePath)
    {
        Times.Clear();
        string[] lines = await File.ReadAllLinesAsync(osuFilePath);
        bool inTimingPoints = false;
        int? currentKiaiStart = null;
        var rawPeriods = new List<KiaiTime>();

        foreach (string line in lines)
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

                string[] parts = line.Split(',');
                if (parts.Length < 8) continue;
                if (!int.TryParse(parts[0], out int time)) continue;
                if (!int.TryParse(parts[7], out int effects)) continue;

                bool kiai = (effects & 1) == 1;
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

    /// <summary>
    ///     Checks if the current time is within any kiai period
    /// </summary>
    /// <param name="currentTime"></param>
    /// <returns></returns>
    public bool IsKiaiPeriod(int currentTime)
    {
        foreach (KiaiTime kiai in Times)
            if (currentTime >= kiai.Start && currentTime < kiai.End)
                return true;
        return false;
    }

    /// <summary>
    ///     Updates the internal state and triggers events if entering or exiting a kiai period
    /// </summary>
    /// <param name="currentTime"></param>
    public void UpdateKiaiPeriodState(int currentTime)
    {
        bool currentlyInKiai = IsKiaiPeriod(currentTime);
        if (currentlyInKiai != _isInKiaiPeriod)
        {
            _isInKiaiPeriod = currentlyInKiai;
            if (currentlyInKiai)
                KiaiPeriodEntered?.Invoke();
            else
                KiaiPeriodExited?.Invoke();
        }
    }

    /// <summary>
    ///     Merges overlapping or contiguous kiai periods into a single period
    /// </summary>
    /// <param name="periods"></param>
    /// <returns></returns>
    private static List<KiaiTime> MergePeriods(List<KiaiTime> periods)
    {
        const int mergeThresholdMs = 100;
        if (periods.Count == 0) return new List<KiaiTime>();
        var sorted = periods.OrderBy(p => p.Start).ToList();
        var merged = new List<KiaiTime> { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            KiaiTime last = merged[^1];
            KiaiTime curr = sorted[i];
            if (curr.Start <= last.End + mergeThresholdMs)
                last.End = Math.Max(last.End, curr.End);
            else
                merged.Add(curr);
        }

        return merged;
    }
}

/// <summary>
///     Manages break periods parsed from an osu! beatmap file
/// </summary>
public class BreakPeriodCalculator
{
    private bool _isInBreakPeriod;
    public List<BreakPeriod> BreakPeriods { get; } = new();
    public event Action? BreakPeriodEntered;
    public event Action? BreakPeriodExited;

    /// <summary>
    ///     Parses break periods from the specified osu! beatmap file and maps them to the provided xAxis and yAxis data
    /// </summary>
    /// <param name="osuFilePath"></param>
    /// <param name="xAxis"></param>
    /// <param name="yAxis"></param>
    /// <returns></returns>
    public async Task<List<BreakPeriod>> ParseBreakPeriodsAsync(string? osuFilePath, List<double> xAxis,
        List<double> yAxis)
    {
        BreakPeriods.Clear();
        string[] lines = await File.ReadAllLinesAsync(osuFilePath);
        bool inBreakPeriodSection = false;
        int totalPoints = xAxis.Count;
        var rawPeriods = new List<BreakPeriod>();

        foreach (string line in lines)
        {
            if (line.StartsWith("//Break Periods"))
            {
                inBreakPeriodSection = true;
                continue;
            }

            if (inBreakPeriodSection)
            {
                if (line.StartsWith("//")) break;
                string[] parts = line.Split(',');
                if (parts.Length == 3 && parts[0] == "2")
                    if (double.TryParse(parts[1], out double start) && double.TryParse(parts[2], out double end))
                    {
                        int startIndex = FindClosestIndex(xAxis, start);
                        int endIndex = FindClosestIndex(xAxis, end);
                        double startPercentage = startIndex / (double)totalPoints * 100;
                        double endPercentage = endIndex / (double)totalPoints * 100;
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

    /// <summary>
    ///     Uses the Tosu WebSocket response to check if currently in a break period
    /// </summary>
    /// <param name="tosuApi"></param>
    /// <returns></returns>
    public bool IsBreakPeriod(TosuApi tosuApi)
    {
        return tosuApi.IsBreakPeriod();
    }

    /// <summary>
    ///     Updates the internal state and triggers events if entering or exiting a break period
    /// </summary>
    /// <param name="tosuApi"></param>
    public void UpdateBreakPeriodState(TosuApi tosuApi)
    {
        bool currentlyInBreak = IsBreakPeriod(tosuApi);
        if (currentlyInBreak != _isInBreakPeriod)
        {
            _isInBreakPeriod = currentlyInBreak;
            if (currentlyInBreak)
                BreakPeriodEntered?.Invoke();
            else
                BreakPeriodExited?.Invoke();
        }
    }

    /// <summary>
    ///     Finds the index of the closest value in the xAxis to the specified value
    /// </summary>
    /// <param name="xAxis"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    private static int FindClosestIndex(List<double> xAxis, double value)
    {
        int closestIndex = 0;
        double smallestDifference = double.MaxValue;
        for (int i = 0; i < xAxis.Count; i++)
        {
            double difference = Math.Abs(xAxis[i] - value);
            if (difference < smallestDifference)
            {
                smallestDifference = difference;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    /// <summary>
    ///     Merges overlapping or contiguous break periods into a single period
    /// </summary>
    /// <param name="periods"></param>
    /// <returns></returns>
    private static List<BreakPeriod> MergePeriods(List<BreakPeriod> periods)
    {
        const int mergeThresholdMs = 100;
        if (periods.Count == 0) return new List<BreakPeriod>();
        var sorted = periods.OrderBy(p => p.Start).ToList();
        var merged = new List<BreakPeriod> { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            BreakPeriod last = merged[^1];
            BreakPeriod curr = sorted[i];
            if (curr.Start <= last.End + mergeThresholdMs)
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