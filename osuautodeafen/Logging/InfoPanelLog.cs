using System;
using System.Collections.Generic;

namespace osuautodeafen.Logging;

public class InfoPanelLog
{
    public readonly Dictionary<string, string> Logs = new();

    /// <summary>
    /// Creates or updates an info panel entry
    /// </summary>
    public void LogToInfoPanel(string message, bool includeTimestamp = true, string? keyword = null,
        string? hyperLink = null)
    {
        string newLine = includeTimestamp
            ? $"[{DateTime.Now:MM-dd HH:mm:ss.fff}] {message}"
            : message;

        if (!string.IsNullOrEmpty(hyperLink))
            newLine += $" {hyperLink}";

        if (!string.IsNullOrEmpty(keyword))
            Logs[keyword] = newLine;
        else
            Logs[Guid.NewGuid().ToString()] = newLine;
    }

    /// <summary>
    /// Erases all entries from the info panel
    /// </summary>
    public void ClearInfoPanelLogs()
    {
        Logs.Clear();
    }
}