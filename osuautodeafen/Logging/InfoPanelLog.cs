using System;
using System.Collections.Generic;

namespace osuautodeafen.Logging;

public record InfoPanelLogEntry(
    string Text,
    string? Hyperlink = null,
    int Order = 0);

public class InfoPanelLog
{
    public readonly Dictionary<string, InfoPanelLogEntry> Logs = new();

    public void LogToInfoPanel(
        string message,
        bool includeTimestamp = true,
        string? keyword = null,
        string? hyperLink = null,
        int order = Int32.MaxValue)
    {
        string text = includeTimestamp
            ? $"[{DateTime.Now:MM-dd HH:mm:ss.fff}] {message}"
            : message;

        Logs[keyword ?? Guid.NewGuid().ToString()] =
            new InfoPanelLogEntry(text, hyperLink, order);
    }

    public void ClearInfoPanelLogs()
    {
        Logs.Clear();
    }
}