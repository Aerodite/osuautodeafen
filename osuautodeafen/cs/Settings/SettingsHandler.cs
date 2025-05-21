using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using IniParser;
using IniParser.Model;

namespace osuautodeafen.cs.Settings;

public class SettingsHandler : Control, INotifyPropertyChanged
{
    private readonly string _iniPath;
    private readonly FileIniDataParser _parser = new();
    private IniData _data;

    public SettingsHandler()
    {
        _iniPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(_iniPath)!);
        _data = File.Exists(_iniPath) ? _parser.ReadFile(_iniPath) : new IniData();
        LoadSettings();
    }

    // Example properties (add all you need)
    private double _minCompletionPercentage;
    public double MinCompletionPercentage
    {
        get => _minCompletionPercentage;
        set { if (Set(ref _minCompletionPercentage, value)) SaveSetting("General", "MinCompletionPercentage", value); }
    }

    private double _starRating;
    public double StarRating
    {
        get => _starRating;
        set { if (Set(ref _starRating, value)) SaveSetting("General", "StarRating", value); }
    }

    private double _performancePoints;
    public double PerformancePoints
    {
        get => _performancePoints;
        set { if (Set(ref _performancePoints, value)) SaveSetting("General", "PerformancePoints", value); }
    }

    private bool _isFCRequired;
    public bool IsFCRequired
    {
        get => _isFCRequired;
        set { if (Set(ref _isFCRequired, value)) SaveSetting("General", "IsFCRequired", value); }
    }

    private bool _undeafenAfterMiss;
    public bool UndeafenAfterMiss
    {
        get => _undeafenAfterMiss;
        set { if (Set(ref _undeafenAfterMiss, value)) SaveSetting("General", "UndeafenAfterMiss", value); }
    }

    // Helper for property changed
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        return true;
    }

    public void LoadSettings()
    {
        MinCompletionPercentage = double.TryParse(_data["General"]["MinCompletionPercentage"], out var mcp) ? mcp : 0;
        StarRating = double.TryParse(_data["General"]["StarRating"], out var sr) ? sr : 0;
        PerformancePoints = double.TryParse(_data["General"]["PerformancePoints"], out var pp) ? pp : 0;
        IsFCRequired = bool.TryParse(_data["General"]["IsFCRequired"], out var fc) && fc;
        UndeafenAfterMiss = bool.TryParse(_data["General"]["UndeafenAfterMiss"], out var uam) && uam;
    }

    private void SaveSetting(string section, string key, object value)
    {
        _data[section][key] = value.ToString();
        _parser.WriteFile(_iniPath, _data);
    }

    public void ResetToDefaults()
    {
        MinCompletionPercentage = 60;
        StarRating = 0;
        PerformancePoints = 0;
        IsFCRequired = false;
        UndeafenAfterMiss = false;
    }
}