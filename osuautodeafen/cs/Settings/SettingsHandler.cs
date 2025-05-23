using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using IniParser;
using IniParser.Model;

namespace osuautodeafen.cs.Settings;

public class SettingsHandler : Control, INotifyPropertyChanged
{
    private readonly string _iniPath;
    private readonly string _appPath;
    private readonly FileIniDataParser _parser = new();
    public IniData _data;

    private double _minCompletionPercentage;
    private double _performancePoints;
    private double _starRating;

    public SettingsHandler()
    {
        _appPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen");
        _iniPath = Path.Combine(_appPath, "settings.ini");
        Directory.CreateDirectory(_appPath);

        if (!File.Exists(_iniPath))
        {
            _data = new IniData();
            _data.Sections.AddSection("General");
            _parser.WriteFile(_iniPath, _data);
        }

        _data = _parser.ReadFile(_iniPath);
        LoadSettings();
    }

    public string GetPath() => _appPath;

    public double MinCompletionPercentage
    {
        get => _minCompletionPercentage;
        set
        {
            if (Set(ref _minCompletionPercentage, value)) SaveSetting("General", "MinCompletionPercentage", value);
        }
    }

    public double StarRating
    {
        get => _starRating;
        set
        {
            if (Set(ref _starRating, value)) SaveSetting("General", "StarRating", value);
        }
    }

    public double PerformancePoints
    {
        get => _performancePoints;
        set
        {
            if (Set(ref _performancePoints, value)) SaveSetting("General", "PerformancePoints", value);
        }
    }

    public bool IsFCRequired { get; set; }
    public bool UndeafenAfterMiss { get; set; }
    public bool IsBackgroundEnabled { get; set; }
    public bool IsParallaxEnabled { get; set; }
    public bool IsBlurEffectEnabled { get; set; }
    public string DeafenKeybind { get; set; }
    public bool IsBreakUndeafenToggleEnabled { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        return true;
    }

    public void LoadSettings()
    {
        // Load directly into backing fields to avoid triggering SaveSetting
        _minCompletionPercentage = double.TryParse(_data["General"]["MinCompletionPercentage"], out var mcp) ? mcp : 0;
        _starRating = double.TryParse(_data["General"]["StarRating"], out var sr) ? sr : 0;
        _performancePoints = double.TryParse(_data["General"]["PerformancePoints"], out var pp) ? pp : 0;
        IsBreakUndeafenToggleEnabled =
            bool.TryParse(_data["General"]["IsBreakUndeafenToggleEnabled"], out var bu) && bu;

        IsFCRequired = bool.TryParse(_data["Behavior"]["IsFCRequired"], out var fc) && fc;
        UndeafenAfterMiss = bool.TryParse(_data["Behavior"]["UndeafenAfterMiss"], out var uam) && uam;

        DeafenKeybind = _data["Hotkeys"]["DeafenKeybind"];

        IsBackgroundEnabled = bool.TryParse(_data["UI"]["IsBackgroundEnabled"], out var bg) && bg;
        IsParallaxEnabled = bool.TryParse(_data["UI"]["IsParallaxEnabled"], out var px) && px;
        IsBlurEffectEnabled = bool.TryParse(_data["UI"]["IsBlurEffectEnabled"], out var blur) && blur;

        // Notify property changed for bindings
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinCompletionPercentage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StarRating)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PerformancePoints)));
    }

    public void SaveSetting(string section, string key, object value)
    {
        if (!_data.Sections.ContainsSection(section))
            _data.Sections.AddSection(section);
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