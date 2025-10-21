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
    private string? _activePresetPath;
    private double _blurRadius;

    private bool _isBreakUndeafenToggleEnabled;

    private IniData _mainData;

    private double _minCompletionPercentage;
    private double _performancePoints;
    private IniData? _presetData;
    private double _starRating;

    private double _windowHeight;
    private double _windowWidth;
    
    public IniData Data;
    

    public SettingsHandler()
    {
        _appPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen");
        _iniPath = Path.Combine(_appPath, "settings.ini");
        Directory.CreateDirectory(_appPath);

        string presetsPath = Path.Combine(_appPath, "presets");
        Directory.CreateDirectory(presetsPath);

        if (!File.Exists(_iniPath))
        {
            _mainData = CreateDefaultIniData();
            _parser.WriteFile(_iniPath, _mainData);
        }
        else
        {
            _mainData = _parser.ReadFile(_iniPath);
        }

        _presetData = null;
        Data = _mainData;
        EnsureSectionsExist();
        LoadSettings();
    }

    public int DeafenKeybindKey { get; private set; }
    public int DeafenKeybindControlSide { get; private set; }
    public int DeafenKeybindAltSide { get; private set; }
    public int DeafenKeybindShiftSide { get; private set; }

    public bool IsPresetActive => _activePresetPath != null;
    private IniData CurrentData => IsPresetActive ? _presetData! : _mainData;
    private string ActivePath => _activePresetPath ?? _iniPath;

    public double MinCompletionPercentage
    {
        get => _minCompletionPercentage;
        set
        {
            if (Set(ref _minCompletionPercentage, value))
                SaveSetting("General", "MinCompletionPercentage", value);
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

    public bool IsBreakUndeafenToggleEnabled
    {
        get => _isBreakUndeafenToggleEnabled;
        set
        {
            if (Set(ref _isBreakUndeafenToggleEnabled, value))
                SaveSetting("Behavior", "IsBreakUndeafenToggleEnabled", value);
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
    public bool IsKiaiEffectEnabled { get; set; }
    public string? tosuApiIp { get; set; }
    public string? tosuApiPort { get; set; }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public void ActivatePreset(string presetFilePath)
    {
        if (!File.Exists(presetFilePath))
        {
            Console.WriteLine($"Preset file not found: {presetFilePath}");
            return;
        }

        _activePresetPath = presetFilePath;
        _presetData = _parser.ReadFile(presetFilePath);
        Data = _presetData;
        EnsureSectionsExist();
        LoadSettings();
    }

    public void DeactivatePreset()
    {
        if (_activePresetPath == null)
        {
            Console.WriteLine("No preset is currently active.");
            return;
        }

        _activePresetPath = null;
        _presetData = null;
        Data = _mainData;
        EnsureSectionsExist();
        LoadSettings();
    }

    /// <summary>
    ///     Ensures that all required sections and keys exist in the INI data.
    /// </summary>
    /// <remarks>
    ///     Very useful in the case that a user updates the application and new settings are added,
    ///     should be called on every start up
    /// </remarks>
    private void EnsureSectionsExist()
    {
        IniData defaults = CreateDefaultIniData();
        bool changed = false;

        foreach (SectionData? section in defaults.Sections)
        {
            if (!Data.Sections.ContainsSection(section.SectionName))
            {
                Data.Sections.AddSection(section.SectionName);
                changed = true;
            }

            foreach (KeyData? key in section.Keys)
                if (!Data[section.SectionName].ContainsKey(key.KeyName))
                {
                    Data[section.SectionName][key.KeyName] = key.Value;
                    changed = true;
                }
        }

        if (changed)
            _parser.WriteFile(ActivePath, Data);
    }

    /// <summary>
    ///     Creates a default IniData object with all necessary sections and keys
    /// </summary>
    private static IniData CreateDefaultIniData()
    {
        IniData data = new();

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
        data["Hotkeys"]["DeafenKeybindControlSide"] = "1"; // Left Control
        data["Hotkeys"]["DeafenKeybindAltSide"] = "0";
        data["Hotkeys"]["DeafenKeybindShiftSide"] = "0";

        data.Sections.AddSection("UI");
        data["UI"]["IsBackgroundEnabled"] = "True";
        data["UI"]["IsParallaxEnabled"] = "True";
        data["UI"]["BlurRadius"] = "0";
        data["UI"]["IsKiaiEffectEnabled"] = "True";
        data["UI"]["WindowWidth"] = "630";
        data["UI"]["WindowHeight"] = "630";

        data.Sections.AddSection("Network");
        data["Network"]["tosuApiIp"] = "127.0.0.1";
        data["Network"]["tosuApiPort"] = "24050";

        return data;
    }

    /// <summary>
    ///     Returns the application data path where settings are stored
    /// </summary>
    /// <returns>
    ///     The application data path as a string
    /// </returns>
    public string GetPath()
    {
        return _appPath;
    }

    /// <summary>
    ///     Sets the field to the specified value and raises the PropertyChanged event if the value has changed
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <param name="propertyName"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns>
    ///     True if the value was changed, false if it was the same
    /// </returns>
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        return true;
    }

    /// <summary>
    ///     Loads settings from the INI file into the properties
    /// </summary>
    public void LoadSettings()
    {
        _minCompletionPercentage =
            double.TryParse(Data["General"]["MinCompletionPercentage"], out double mcp) ? mcp : 0;
        _starRating = double.TryParse(Data["General"]["StarRating"], out double sr) ? sr : 0;
        _performancePoints = double.TryParse(Data["General"]["PerformancePoints"], out double pp) ? pp : 0;
        BlurRadius = double.TryParse(Data["UI"]["BlurRadius"], out double blur) ? blur : 0;

        IsBreakUndeafenToggleEnabled =
            bool.TryParse(Data["Behavior"]["IsBreakUndeafenToggleEnabled"], out bool bu) && bu;
        IsFCRequired = bool.TryParse(Data["Behavior"]["IsFCRequired"], out bool fc) && fc;
        UndeafenAfterMiss = bool.TryParse(Data["Behavior"]["UndeafenAfterMiss"], out bool uam) && uam;

        DeafenKeybindKey = int.TryParse(Data["Hotkeys"]["DeafenKeybindKey"], out int keyVal) ? keyVal : 0;
        DeafenKeybindControlSide =
            int.TryParse(Data["Hotkeys"]["DeafenKeybindControlSide"], out int ctrlSide) ? ctrlSide : 0;
        DeafenKeybindAltSide = int.TryParse(Data["Hotkeys"]["DeafenKeybindAltSide"], out int altSide) ? altSide : 0;
        DeafenKeybindShiftSide =
            int.TryParse(Data["Hotkeys"]["DeafenKeybindShiftSide"], out int shiftSide) ? shiftSide : 0;

        IsBackgroundEnabled = bool.TryParse(Data["UI"]["IsBackgroundEnabled"], out bool bg) && bg;
        IsParallaxEnabled = bool.TryParse(Data["UI"]["IsParallaxEnabled"], out bool px) && px;
        IsKiaiEffectEnabled = bool.TryParse(Data["UI"]["IsKiaiEffectEnabled"], out bool kiai) && kiai;
        _windowWidth = double.TryParse(Data["UI"]["WindowWidth"], out double width) ? width : 630;
        _windowHeight = double.TryParse(Data["UI"]["WindowHeight"], out double height) ? height : 630;

        tosuApiIp = Data["Network"]["tosuApiIp"];
        tosuApiPort = Data["Network"]["tosuApiPort"];

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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(tosuApiIp)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(tosuApiPort)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeafenKeybindControlSide)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeafenKeybindAltSide)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeafenKeybindShiftSide)));
    }

    /// <summary>
    ///     Saves a setting to the INI file or preset.
    /// </summary>
    /// <param name="section"></param>
    /// <param name="key"></param>
    /// <param name="value"></param>
    public void SaveSetting(string section, string key, object? value)
    {
        IniData targetData = CurrentData;
        if (!targetData.Sections.ContainsSection(section))
            targetData.Sections.AddSection(section);
        targetData[section][key] = value?.ToString();

        string path = IsPresetActive ? _activePresetPath! : _iniPath;
        Console.WriteLine($"Writing to: {path}");
        _parser.WriteFile(path, targetData);

        DeafenKeybindKey = int.TryParse(targetData["Hotkeys"]["DeafenKeybindKey"], out int keyVal) ? keyVal : 0;
        DeafenKeybindControlSide = int.TryParse(targetData["Hotkeys"]["DeafenKeybindControlSide"], out int ctrlSide)
            ? ctrlSide
            : 0;
        DeafenKeybindAltSide =
            int.TryParse(targetData["Hotkeys"]["DeafenKeybindAltSide"], out int altSide) ? altSide : 0;
        DeafenKeybindShiftSide = int.TryParse(targetData["Hotkeys"]["DeafenKeybindShiftSide"], out int shiftSide)
            ? shiftSide
            : 0;
    }

    /// <summary>
    ///     Resets all settings to their default values and saves them to the active file.
    /// </summary>
    public void ResetToDefaults()
    {
        if (IsPresetActive)
        {
            _presetData = CreateDefaultIniData();
            Data = _presetData;
            _parser.WriteFile(_activePresetPath!, _presetData);
        }
        else
        {
            _mainData = CreateDefaultIniData();
            Data = _mainData;
            _parser.WriteFile(_iniPath, _mainData);
        }

        LoadSettings();
    }
}