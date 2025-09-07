using System;
using System.Collections.Generic;

namespace osuautodeafen.cs.Log;

public class LogImportant
{
    public readonly Dictionary<string, string> _importantLogs = new();

    /// <summary>
    ///     Logs an important message with optional timestamp, keyword, and hyperlink
    /// </summary>
    /// <param name="message"></param>
    /// <param name="includeTimestamp"></param>
    /// <param name="keyword"></param>
    /// <param name="hyperLink"></param>
    public void logImportant(string message, bool includeTimestamp = true, string? keyword = null,
        string? hyperLink = null)
    {
        string newLine = includeTimestamp
            ? $"[{DateTime.Now:MM-dd HH:mm:ss.fff}]{message}"
            : message;

        if (!string.IsNullOrEmpty(hyperLink))
            newLine += $" {hyperLink}";

        if (!string.IsNullOrEmpty(keyword))
            _importantLogs[keyword] = newLine;
        else
            _importantLogs[Guid.NewGuid().ToString()] = newLine;
    }

    /// <summary>
    ///     Clears all logged important messages
    /// </summary>
    public void ClearLogs()
    {
        _importantLogs.Clear();
    }
}