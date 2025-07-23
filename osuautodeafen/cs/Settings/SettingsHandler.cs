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
    private readonly string _appPath;
    private readonly string _iniPath;
    private readonly FileIniDataParser _parser = new();
    private double _blurRadius;

    private double _minCompletionPercentage;
    private double _performancePoints;
    private double _starRating;

    private double _windowHeight;
    private double _windowWidth;

    public IniData Data;

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

    public string DeafenKeybindKey => Data["Hotkeys"]["DeafenKeybindKey"];
    public string DeafenKeybindModifiers => Data["Hotkeys"]["DeafenKeybindModifiers"];

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

    public double BlurRadius
    {
        get => _blurRadius;
        set
        {
            if (Set(ref _blurRadius, value)) SaveSetting("UI", "BlurRadius", value);
        }
    }

    public double WindowWidth
    {
        get => _windowWidth;
        set
        {
            if (Set(ref _windowWidth, value)) SaveSetting("UI", "WindowWidth", value);
        }
    }

    public double WindowHeight
    {
        get => _windowHeight;
        set
        {
            if (Set(ref _windowHeight, value)) SaveSetting("UI", "WindowHeight", value);
        }
    }

    public bool IsFCRequired { get; set; }
    public bool UndeafenAfterMiss { get; set; }
    public bool IsBackgroundEnabled { get; set; }
    public bool IsParallaxEnabled { get; set; }

    public string? DeafenKeybind { get; set; }
    public bool IsBreakUndeafenToggleEnabled { get; set; }
    public bool IsKiaiEffectEnabled { get; set; }

    public new event PropertyChangedEventHandler? PropertyChanged;

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
        data["Hotkeys"]["DeafenKeybindKey"] = "47"; // D
        data["Hotkeys"]["DeafenKeybindModifiers"] = "2"; // Ctrl

        data.Sections.AddSection("UI");
        data["UI"]["IsBackgroundEnabled"] = "True";
        data["UI"]["IsParallaxEnabled"] = "True";
        data["UI"]["BlurRadius"] = "0";
        data["UI"]["IsKiaiEffectEnabled"] = "True";
        data["UI"]["WindowWidth"] = "630";
        data["UI"]["WindowHeight"] = "630";

        return data;
    }

    public string GetPath()
    {
        return _appPath;
    }

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
        BlurRadius = double.TryParse(Data["UI"]["BlurRadius"], out var blur) ? blur : 0;

        IsBreakUndeafenToggleEnabled =
            bool.TryParse(Data["Behavior"]["IsBreakUndeafenToggleEnabled"], out var bu) && bu;
        IsFCRequired = bool.TryParse(Data["Behavior"]["IsFCRequired"], out var fc) && fc;
        UndeafenAfterMiss = bool.TryParse(Data["Behavior"]["UndeafenAfterMiss"], out var uam) && uam;

        DeafenKeybind = $"{Data["Hotkeys"]["DeafenKeybindKey"]},{Data["Hotkeys"]["DeafenKeybindModifiers"]}";

        IsBackgroundEnabled = bool.TryParse(Data["UI"]["IsBackgroundEnabled"], out var bg) && bg;
        IsParallaxEnabled = bool.TryParse(Data["UI"]["IsParallaxEnabled"], out var px) && px;
        IsKiaiEffectEnabled = bool.TryParse(Data["UI"]["IsKiaiEffectEnabled"], out var kiai) && kiai;
        _windowWidth = double.TryParse(Data["UI"]["WindowWidth"], out var width) ? width : 630;
        _windowHeight = double.TryParse(Data["UI"]["WindowHeight"], out var height) ? height : 630;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinCompletionPercentage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StarRating)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PerformancePoints)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFCRequired)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UndeafenAfterMiss)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBackgroundEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsParallaxEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlurRadius)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeafenKeybind)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBreakUndeafenToggleEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsKiaiEffectEnabled)));
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