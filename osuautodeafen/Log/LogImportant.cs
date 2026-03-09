using System;
using System.Collections.Generic;

namespace osuautodeafen.cs.Log;

public class LogImportant
{
    public readonly Dictionary<string, string> Logs = new();

    /// <summary>
    ///     Logs a message to be displayed only in the info panel
    /// </summary>
    public void logToInfoPanel(string message, bool includeTimestamp = true, string? keyword = null,
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

    public void ClearLogs()
    {
        Logs.Clear();
    }
}