using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace osuautodeafen.cs;

public class BreakPeriod
{
    public int Start { get; set; }
    public int End { get; set; }
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public double StartPercentage { get; set; }
    public double EndPercentage { get; set; }
}

public class BreakPeriodCalculator
{
    public List<BreakPeriod> BreakPeriods { get; } = new();

    public async Task<List<BreakPeriod>> ParseBreakPeriodsAsync(string osuFilePath, List<double> xAxis,
        List<double> yAxis)
    {
        BreakPeriods.Clear();

        var lines = await File.ReadAllLinesAsync(osuFilePath);
        var inBreakPeriodSection = false;

        // Filter out x-values corresponding to y-values of -100
        var validXAxis = xAxis.Where((x, index) => yAxis[index] != -100).ToList();
        var totalPoints = validXAxis.Count;

        foreach (var line in lines)
        {
            if (line.StartsWith("//Break Periods"))
            {
                inBreakPeriodSection = true;
                Console.WriteLine("Found break periods section.");
                continue;
            }

            if (inBreakPeriodSection)
            {
                if (line.StartsWith("//")) break;

                var parts = line.Split(',');
                if (parts.Length == 3 && parts[0] == "2")
                {
                    if (double.TryParse(parts[1], out var start) && double.TryParse(parts[2], out var end))
                    {
                        var startIndex = FindClosestIndex(validXAxis, start);
                        var endIndex = FindClosestIndex(validXAxis, end);

                        var startPercentage = startIndex / (double)totalPoints * 100;
                        var endPercentage = endIndex / (double)totalPoints * 100;

                        var breakPeriod = new BreakPeriod
                        {
                            Start = (int)start,
                            End = (int)end,
                            StartIndex = startIndex,
                            EndIndex = endIndex,
                            StartPercentage = startPercentage,
                            EndPercentage = endPercentage
                        };

                        Console.WriteLine(
                            $"Parsed break period: Start={start}, End={end}, StartPercentage={startPercentage}, EndPercentage={endPercentage}");
                        BreakPeriods.Add(breakPeriod);
                    }
                    else
                    {
                        Console.WriteLine($"Failed to parse start or end time from line: {line}");
                    }
                }
                else
                {
                    Console.WriteLine($"Invalid break period format in line: {line}");
                }
            }
        }

        return BreakPeriods;
    }

    public bool IsBreakPeriod(double completionPercentage)
    {
        foreach (var breakPeriod in BreakPeriods)
            if (completionPercentage >= breakPeriod.StartPercentage &&
                completionPercentage <= breakPeriod.EndPercentage)
                return true;
        return false;
    }

    private int FindClosestIndex(List<double> xAxis, double value)
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
}