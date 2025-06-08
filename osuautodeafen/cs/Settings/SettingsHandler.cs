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
    public IniData Data;

    private double _minCompletionPercentage;
    private double _performancePoints;
    private double _starRating;
    
    public static IniData CreateDefaultIniData()
    {
        var data = new IniData();

        data.Sections.AddSection("General");
        data["General"]["MinCompletionPercentage"] = "60";
        data["General"]["StarRating"] = "0";
        data["General"]["PerformancePoints"] = "0";

        data.Sections.AddSection("Behavior");
        data["Behavior"]["IsFCRequired"] = "False";
        data["Behavior"]["UndeafenAfterMiss"] = "False";
        data["Behavior"]["IsBreakUndeafenToggleEnabled"] = "False";

        data.Sections.AddSection("Hotkeys");
        data["Hotkeys"]["DeafenKeybind"] = "Control+D";

        data.Sections.AddSection("UI");
        data["UI"]["IsBackgroundEnabled"] = "True";
        data["UI"]["IsParallaxEnabled"] = "True";
        data["UI"]["IsBlurEffectEnabled"] = "False";

        return data;
    }

    public SettingsHandler()
    {
        _appPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen");
        _iniPath = Path.Combine(_appPath, "settings.ini");
        Directory.CreateDirectory(_appPath);

        if (!File.Exists(_iniPath))
        {
            Data = CreateDefaultIniData();
            _parser.WriteFile(_iniPath, Data);
        }
        else
        {
            Data = _parser.ReadFile(_iniPath);
        }

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
    public string? DeafenKeybind { get; set; }
    public bool IsBreakUndeafenToggleEnabled { get; set; }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        return true;
    }

    public void LoadSettings()
    {
        _minCompletionPercentage = double.TryParse(Data["General"]["MinCompletionPercentage"], out var mcp) ? mcp : 0;
        _starRating = double.TryParse(Data["General"]["StarRating"], out var sr) ? sr : 0;
        _performancePoints = double.TryParse(Data["General"]["PerformancePoints"], out var pp) ? pp : 0;
        IsBreakUndeafenToggleEnabled =
            bool.TryParse(Data["Behavior"]["IsBreakUndeafenToggleEnabled"], out var bu) && bu;

        IsFCRequired = bool.TryParse(Data["Behavior"]["IsFCRequired"], out var fc) && fc;
        UndeafenAfterMiss = bool.TryParse(Data["Behavior"]["UndeafenAfterMiss"], out var uam) && uam;

        DeafenKeybind = Data["Hotkeys"]["DeafenKeybind"];

        IsBackgroundEnabled = bool.TryParse(Data["UI"]["IsBackgroundEnabled"], out var bg) && bg;
        IsParallaxEnabled = bool.TryParse(Data["UI"]["IsParallaxEnabled"], out var px) && px;
        IsBlurEffectEnabled = bool.TryParse(Data["UI"]["IsBlurEffectEnabled"], out var blur) && blur;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinCompletionPercentage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StarRating)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PerformancePoints)));
    }

    public void SaveSetting(string section, string key, object? value)
    {
        if (!Data.Sections.ContainsSection(section))
            Data.Sections.AddSection(section);
        Data[section][key] = value?.ToString();
        _parser.WriteFile(_iniPath, Data);
    }

    public void ResetToDefaults()
    {
        Data = CreateDefaultIniData();
        _parser.WriteFile(_iniPath, Data);
        LoadSettings();
    }
}