using System;
using System.Collections.Generic;

namespace osuautodeafen.cs.Log;

public class LogImportant
{
    public readonly Dictionary<string, string> _importantLogs = new();

    public void logImportant(string message, bool includeTimestamp = true, string? keyword = null,
        string? hyperLink = null)
    {
        var newLine = includeTimestamp
            ? $"[{DateTime.Now:MM-dd HH:mm:ss.fff}]{message}"
            : message;

        if (!string.IsNullOrEmpty(hyperLink))
            newLine += $" {hyperLink}";

        if (!string.IsNullOrEmpty(keyword))
            _importantLogs[keyword] = newLine;
        else
            _importantLogs[Guid.NewGuid().ToString()] = newLine;
    }
    
    public void ClearLogs()
    {
        _importantLogs.Clear();
    }
}