using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IniParser.Model;
using osuautodeafen.cs.Background;
using osuautodeafen.cs.Logo;
using osuautodeafen.cs.Settings;
using osuautodeafen.cs.Settings.Keybinds;
using osuautodeafen.cs.Settings.Presets;
using osuautodeafen.cs.StrainGraph;
using osuautodeafen.cs.Tooltips;
using osuautodeafen.cs.Tosu;
using osuautodeafen.cs.Update;
using osuautodeafen.cs.ViewModels;

namespace osuautodeafen.Views;

public partial class SettingsView : UserControl
{
    private static Button? _updateNotificationBarButton;
    private static ProgressBar? _updateProgressBar;
    private readonly KeybindHelper _keybindHelper = new();
    private readonly SettingsHandler _settingsHandler;

    private readonly UpdateChecker _updateChecker = new(_updateNotificationBarButton, _updateProgressBar);

    private readonly SemaphoreSlim _updateCheckLock = new(1, 1);
    private BackgroundManager? _backgroundManager;
    private ChartManager _chartManager;
    private DispatcherTimer? _completionPercentageSaveTimer;

    private double _pendingCompletionPercentage;
    private int _pendingPP;
    private double _pendingStarRating;

    private DispatcherTimer? _ppSaveTimer;
    private SettingsViewModel _settingsViewModel;
    private DispatcherTimer? _starRatingSaveTimer;
    private TooltipManager _tooltipManager;
    private TosuApi _tosuApi;
    private SharedViewModel _viewModel;

    public SettingsView(
        SettingsHandler settingsHandler,
        TosuApi tosuApi,
        SharedViewModel viewModel,
        ChartManager chartManager,
        BackgroundManager? backgroundManager,
        TooltipManager tooltipManager,
        SettingsViewModel settingsViewModel)
    {
        InitializeComponent();

        _settingsHandler = settingsHandler;
        _tosuApi = tosuApi;
        _viewModel = viewModel;
        _chartManager = chartManager;
        _backgroundManager = backgroundManager;
        _tooltipManager = tooltipManager;
        _settingsViewModel = settingsViewModel;

        DataContext = _viewModel;

        DeafenKeybindButton.DataContext = _settingsViewModel;
        if (DeafenKeybindButton.Flyout is Flyout { Content: Control content })
            content.DataContext = _settingsViewModel;
    }

    public void AttachManagers(
        ChartManager chartManager,
        BackgroundManager backgroundManager)
    {
        _chartManager = chartManager;
        _backgroundManager = backgroundManager;
    }

    /// <summary>
    ///     Sets the necessary controls and references from MainWindow for the SettingsView to function
    /// </summary>
    /// <param name="tosuApi"></param>
    /// <param name="viewModel"></param>
    /// <param name="chartManager"></param>
    /// <param name="backgroundManager"></param>
    /// <param name="tooltipManager"></param>
    /// <param name="settingsViewModel"></param>
    public void SetViewControls(TosuApi tosuApi, SharedViewModel viewModel, ChartManager chartManager,
        BackgroundManager? backgroundManager, TooltipManager tooltipManager,
        SettingsViewModel settingsViewModel)
    {
        _tosuApi = tosuApi;
        _viewModel = viewModel;
        _chartManager = chartManager;
        _backgroundManager = backgroundManager;
        _tooltipManager = tooltipManager;
        _settingsViewModel = settingsViewModel;
        DataContext = _viewModel;

        DeafenKeybindButton.DataContext = _settingsViewModel;
        if (DeafenKeybindButton.Flyout is Flyout { Content: Control content }) content.DataContext = _settingsViewModel;
    }

    private void UpdateViewModel()
    {
        _viewModel.MinCompletionPercentage = _settingsHandler.MinCompletionPercentage;
        _viewModel.StarRating = _settingsHandler.StarRating;
        _viewModel.PerformancePoints = (int)Math.Round(_settingsHandler.PerformancePoints);
        _viewModel.BlurRadius = _settingsHandler.BlurRadius;

        _viewModel.IsFCRequired = _settingsHandler.IsFCRequired;
        _viewModel.UndeafenAfterMiss = _settingsHandler.UndeafenAfterMiss;
        _viewModel.IsBreakUndeafenToggleEnabled = _settingsHandler.IsBreakUndeafenToggleEnabled;
        _viewModel.IsPauseUndeafenToggleEnabled = _settingsHandler.IsPauseUndeafenToggleEnabled;

        _viewModel.IsBackgroundEnabled = _settingsHandler.IsBackgroundEnabled;
        _viewModel.IsParallaxEnabled = _settingsHandler.IsParallaxEnabled;
        _viewModel.IsKiaiEffectEnabled = _settingsHandler.IsKiaiEffectEnabled;

        CompletionPercentageSlider.ValueChanged -= CompletionPercentageSlider_ValueChanged;
        StarRatingSlider.ValueChanged -= StarRatingSlider_ValueChanged;
        PPSlider.ValueChanged -= PPSlider_ValueChanged;
        BlurEffectSlider.ValueChanged -= BlurEffectSlider_ValueChanged;

        CompletionPercentageSlider.Value = _viewModel.MinCompletionPercentage;
        StarRatingSlider.Value = _viewModel.StarRating;
        PPSlider.Value = _viewModel.PerformancePoints;
        BlurEffectSlider.Value = _viewModel.BlurRadius;

        CompletionPercentageSlider.ValueChanged += CompletionPercentageSlider_ValueChanged;
        StarRatingSlider.ValueChanged += StarRatingSlider_ValueChanged;
        PPSlider.ValueChanged += PPSlider_ValueChanged;
        BlurEffectSlider.ValueChanged += BlurEffectSlider_ValueChanged;

        FCToggle.IsChecked = _viewModel.IsFCRequired;
        UndeafenOnMissToggle.IsChecked = _viewModel.UndeafenAfterMiss;
        BreakUndeafenToggle.IsChecked = _viewModel.IsBreakUndeafenToggleEnabled;
        PauseUndeafenToggle.IsChecked = _viewModel.IsPauseUndeafenToggleEnabled;

        BackgroundToggle.IsChecked = _viewModel.IsBackgroundEnabled;
        ParallaxToggle.IsChecked = _viewModel.IsParallaxEnabled;
        KiaiEffectToggle.IsChecked = _viewModel.IsKiaiEffectEnabled;
    }

    /// <summary>
    ///     Creates a .preset.data file that contains beatmap information for the current beatmap
    /// </summary>
    private void CreatePresetData()
    {
        string checksum = _tosuApi.GetBeatmapChecksum();
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        string presetDataFilePath = Path.Combine(presetsPath, $"{checksum}.preset.data");

        string? artist = _tosuApi.GetBeatmapArtist();
        string? beatmapName = _tosuApi.GetBeatmapTitle();
        string fullBeatmapName = $"{artist} - {beatmapName}";
        string beatmapDifficulty = _tosuApi.GetBeatmapDifficulty();
        string backgroundPath = _tosuApi.GetBackgroundPath();
        string beatmapId = _tosuApi.GetBeatmapId().ToString();
        string rankedStatus = _tosuApi.GetRankedStatus().ToString(CultureInfo.InvariantCulture);
        string starRating = _tosuApi.GetFullSR().ToString("F1", CultureInfo.InvariantCulture);
        string mapper = _tosuApi.GetBeatmapMapper();


        LogoUpdater? logoUpdater = _backgroundManager?.LogoUpdater;
        string avgColor1 = logoUpdater?.AverageColor1.ToString() ?? "#000000";
        string avgColor2 = logoUpdater?.AverageColor2.ToString() ?? "#000000";
        string avgColor3 = logoUpdater?.AverageColor3.ToString() ?? "#000000";

        var lines = new List<string>
        {
            "[Preset]",
            $"FullBeatmapName={fullBeatmapName}",
            $"Artist={artist}",
            $"BeatmapName={beatmapName}",
            $"BeatmapDifficulty={beatmapDifficulty}",
            $"BeatmapID={beatmapId}",
            $"RankedStatus={rankedStatus}",
            $"BackgroundPath={backgroundPath}",
            $"Mapper={mapper}",
            $"Checksum={checksum}",
            $"StarRating={starRating}",
            $"AverageColor1={avgColor1}",
            $"AverageColor2={avgColor2}",
            $"AverageColor3={avgColor3}"
        };

        File.WriteAllLines(presetDataFilePath, lines);
    }

    /// <summary>
    ///     Deletes the .preset.data file for the current beatmap if it exists
    /// </summary>
    private void DeletePresetData()
    {
        string checksum = _tosuApi.GetBeatmapChecksum();
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        string presetDataFilePath = Path.Combine(presetsPath, $"{checksum}.preset.data");

        if (File.Exists(presetDataFilePath))
            File.Delete(presetDataFilePath);
    }

    private void DeleteAllPresetData()
    {
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        if (Directory.Exists(presetsPath))
        {
            string[] presetDataFiles = Directory.GetFiles(presetsPath, "*.preset.data");
            foreach (string file in presetDataFiles)
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Could not delete preset data file {file}: {ex}");
                }
        }

        _viewModel.RefreshPresets();
    }

    /// <summary>
    ///     Initializes the button with the selected keybind from settings
    /// </summary>
    public void UpdateDeafenKeybindDisplay()
    {
        string currentKeybind = RetrieveKeybindFromSettings();
        _settingsViewModel.DeafenKeybindDisplay = currentKeybind;
    }

    /// <summary>
    ///     Retrieves the currently set keybind from settings
    /// </summary>
    /// <returns></returns>
    public string RetrieveKeybindFromSettings()
    {
        KeyDataCollection? hotkeys = _settingsHandler?.Data["Hotkeys"];
        if (hotkeys == null) return "Set Keybind";

        string? keyStr = hotkeys["DeafenKeybindKey"];
        string? controlSideStr = hotkeys["DeafenKeybindControlSide"];
        string? altSideStr = hotkeys["DeafenKeybindAltSide"];
        string? shiftSideStr = hotkeys["DeafenKeybindShiftSide"];

        if (string.IsNullOrEmpty(keyStr))
            return "Set Keybind";

        if (!int.TryParse(keyStr, out int keyVal))
            return "Set Keybind";

        string display = "";

        if (int.TryParse(controlSideStr, out int controlSide) && controlSide != 0)
            display += controlSide == 2 ? "RCtrl+" : "LCtrl+";
        if (int.TryParse(altSideStr, out int altSide) && altSide != 0)
            display += altSide == 2 ? "RAlt+" : "LAlt+";
        if (int.TryParse(shiftSideStr, out int shiftSide) && shiftSide != 0)
            display += shiftSide == 2 ? "RShift+" : "LShift+";

        if (keyVal == (int)Key.None)
            // signifies that only modifiers are used, so we should remove the trailing +
            return display.EndsWith('+') ? display[..^1] : display.Length > 0 ? display : "Set Keybind";

        display += _keybindHelper.GetFriendlyKeyName((Key)keyVal);
        return display;
    }

    private void CompletionPercentageImage_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Avalonia.Svg.Svg) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Minimum Map \nProgress to Deafen");
    }

    private void CompletionPercentageImage_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void CompletionPercentageSlider_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0.00}%");
    }

    private void CompletionPercentageSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0.00}%");
    }

    private void CompletionPercentageSlider_PointerMove(object? sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0.00}%");
    }

    private void CompletionPercentageSlider_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void PPSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0}pp");
    }

    private void PPSlider_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0}pp");
    }

    private void PPSlider_PointerMove(object? sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0}pp");
    }

    private void PPSlider_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void PPImage_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Avalonia.Svg.Svg) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point,
            "Minimum SS PP to Deafen\n (" + _tosuApi.GetMaxPP() + "pp for this map)");
    }

    private void PPImage_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void StarRatingSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:F1}*");
    }

    private void StarRatingSlider_PointerMove(object? sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:F1}*");
    }

    private void StarRatingSlider_PointerEnter(object? sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:F1}*");
    }

    private void StarRatingSlider_PointerLeave(object? sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void StarRatingImage_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Avalonia.Svg.Svg) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Minimum SR to Deafen\n(" + _tosuApi.GetFullSR() + "* for this map)");
    }

    private void StarRatingImage_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void BlurEffectImage_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Avalonia.Svg.Svg) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Background Blur Radius\n(0-20 multiplied by 5)");
    }

    private void BlurEffectImage_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void BlurEffectSlider_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value * 5:F0}% Blur");
    }

    private void BlurEffectSlider_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value * 5:F0}% Blur");
    }

    private void BlurEffectSlider_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void FCToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = FCToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point, "" + (isEnabled ? "Disable" : "Enable") + " FC Requirement");
    }

    private void FCToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void UndeafenOnMissToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = UndeafenOnMissToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point, "" + (isEnabled ? "Disable" : "Enable") + " Undeafening after a miss");
    }

    private void UndeafenOnMissToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void BreakUndeafenToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = BreakUndeafenToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point,
            "" + (isEnabled ? "Disable" : "Enable") + " Undeafening during breaks");
    }

    private void BreakUndeafenToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void BlurEffectSlider_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value * 5:F0}% Blur");
    }

    private void PauseUndeafenToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = PauseUndeafenToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point,
            "" + (isEnabled ? "Disable" : "Enable") + " Undeafening during a pause");
    }

    private void PauseUndeafenToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void ResetButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button)
            return;

        Point pointerPosition = Tooltips.GetWindowRelativePointer(this, e);

        string tooltipText = _settingsHandler!.IsPresetActive
            ? "Reset current preset to default settings"
            : "Reset global settings to default";

        _tooltipManager.ShowTooltip(this, pointerPosition, tooltipText);
    }

    private void ResetButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void PresetCreate_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Create Preset for\n" + _viewModel.FullBeatmapName);
    }

    private void PresetCreate_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void PresetDelete_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Delete Preset for\n" + _viewModel.FullBeatmapName);
    }

    private void PresetDelete_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void LoadPresetButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Load a different map's preset on to\n" + _viewModel.FullBeatmapName);
    }

    private void LoadPresetButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void DeleteAllPresetsButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Delete All Presets\n(CAN NOT BE UNDONE)");
    }

    private void DeleteAllPresetsButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void DeafenKeybindPanel_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Set Deafen Keybind");
    }

    private void DeafenKeybindPanel_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void BGToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = BackgroundToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point, "" + (isEnabled ? "Disable" : "Enable") + " Beatmap Background");
    }

    private void BGToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void ParallaxToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = ParallaxToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point, "" + (isEnabled ? "Disable" : "Enable") + " Parallax Effect");
    }

    private void ParallaxToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void KiaiEffectToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = KiaiEffectToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point, "" + (isEnabled ? "Disable" : "Enable") + " Kiai Effect");
    }

    private void KiaiEffectToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void CheckForUpdatesButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Check for New Updates");
    }

    private void CheckForUpdatesButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void FileLocationButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point,
            "Open AppData File Location\n(" + _settingsHandler!.GetPath(true) + ")");
        OpenFileLocationImage.Path = "avares://osuautodeafen/Icons/folder-open.svg";
    }

    private void FileLocationButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
        OpenFileLocationImage.Path = "avares://osuautodeafen/Icons/folder.svg";
    }

    private void ReportIssueButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Report an Issue on GitHub");
    }

    private void ReportIssueButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void DebugConsoleButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isOpen = true;
        _tooltipManager.ShowTooltip(this, point, isOpen ? "Close Debug Console" : "Open Debug Console");
    }

    private void DebugConsoleButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void DebugConsoleButton_Click(object sender, RoutedEventArgs e)
    {
        MainWindow? window = this.GetVisualRoot() as MainWindow;
        window?.ToggleDebugConsole(sender, e);
    }

    public async void CompletionPercentageSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        try
        {
            if (DataContext is not SharedViewModel vm) return;
            double roundedValue = Math.Round(e.NewValue, 2);
            vm.MinCompletionPercentage = roundedValue;
            _pendingCompletionPercentage = roundedValue;

            _completionPercentageSaveTimer?.Stop();
            _completionPercentageSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _completionPercentageSaveTimer.Tick += (s, args) =>
            {
                _settingsHandler?.SaveSetting("General", "MinCompletionPercentage", _pendingCompletionPercentage);
                _completionPercentageSaveTimer?.Stop();
            };
            _completionPercentageSaveTimer.Start();

            try
            {
                await _chartManager.UpdateDeafenOverlayAsync(roundedValue);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Task was canceled while updating deafen overlay.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception in CompletionPercentageSlider_ValueChanged: {ex}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error in CompletionPercentageSlider_ValueChanged: {ex.Message}", ex);
        }
    }

    public void StarRatingSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider || DataContext is not SharedViewModel vm) return;
        double roundedValue = Math.Round(slider.Value, 1);
        Console.WriteLine($"Min SR Value: {roundedValue:F1}");
        vm.StarRating = roundedValue;
        _pendingStarRating = roundedValue;

        _starRatingSaveTimer?.Stop();
        _starRatingSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _starRatingSaveTimer.Tick += (s, args) =>
        {
            _settingsHandler?.SaveSetting("General", "StarRating", _pendingStarRating);
            _starRatingSaveTimer?.Stop();
        };
        _starRatingSaveTimer.Start();
    }

    public void PPSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider || DataContext is not SharedViewModel vm) return;
        int roundedValue = (int)Math.Round(slider.Value);
        Console.WriteLine($"Min PP Value: {roundedValue}");
        vm.PerformancePoints = roundedValue;
        _pendingPP = roundedValue;

        _ppSaveTimer?.Stop();
        _ppSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _ppSaveTimer.Tick += (s, args) =>
        {
            _settingsHandler?.SaveSetting("General", "PerformancePoints", _pendingPP);
            _ppSaveTimer?.Stop();
        };
        _ppSaveTimer.Start();
    }

    public void BlurEffectSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider || DataContext is not SharedViewModel vm) return;
        double roundedValue = Math.Round(slider.Value, 1);
        Console.WriteLine($"Blur Radius: {roundedValue:F1}");
        vm.BlurRadius = roundedValue;
        _settingsHandler?.SaveSetting("UI", "BlurRadius", roundedValue);
    }

    /// <summary>
    ///     Resets all settings to their default values on click
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsHandler?.ResetToDefaults();
        UpdateViewModel();
        UpdateDeafenKeybindDisplay();
        try
        {
            _chartManager.UpdateDeafenOverlaySection(_viewModel.MinCompletionPercentage);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERROR] Exception when updating Deafen Section after reset: " + ex.Message);
        }
    }

    /// <summary>
    ///     Deletes the preset for the current beatmap
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PresetButtonDeleteYes_Click(object sender, RoutedEventArgs e)
    {
        DeletePresetButton.Flyout?.Hide();
        string checksum = _tosuApi.GetBeatmapChecksum();
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        string presetFilePath = Path.Combine(presetsPath, $"{checksum}.preset");
        if (File.Exists(presetFilePath))
            File.Delete(presetFilePath);
        _viewModel.PresetExistsForCurrentChecksum = false;
        _settingsHandler?.DeactivatePreset();
        CreatePresetButton.Flyout?.Hide();
        UpdateViewModel();
        UpdateDeafenKeybindDisplay();
        try
        {
            _chartManager.UpdateDeafenOverlaySection(_viewModel.MinCompletionPercentage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception while updating Deafen Section after deleting preset: {ex}");
        }

        DeletePresetData();
        _viewModel.RefreshPresets();
    }

    /// <summary>
    ///     Creates a preset for the current beatmap
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PresetButtonYes_Click(object sender, RoutedEventArgs e)
    {
        CreatePresetButton.Flyout?.Hide();
        string checksum = _tosuApi.GetBeatmapChecksum();
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        string presetFilePath = Path.Combine(presetsPath, $"{checksum}.preset");
        string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.ini");
        File.Copy(settingsPath, presetFilePath, true);
        _viewModel.PresetExistsForCurrentChecksum = true;
        _settingsHandler?.ActivatePreset(presetFilePath);
        CreatePresetData();
        _viewModel.RefreshPresets();
    }

    private void PresetButtonNo_Click(object sender, RoutedEventArgs e)
    {
        CreatePresetButton.Flyout?.Hide();
    }

    private void PresetButtonDeleteNo_Click(object sender, RoutedEventArgs e)
    {
        DeletePresetButton.Flyout?.Hide();
    }

    /// <summary>
    ///     Applies the selected preset when a preset item is clicked
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PresetItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PresetInfo preset)
        {
            string selectedPresetPath = preset.FilePath;
            Console.WriteLine($"Selected Preset Path: {selectedPresetPath}");
            string currentChecksum = _tosuApi.GetBeatmapChecksum();
            string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "osuautodeafen", "presets");
            string currentPresetPath = Path.Combine(presetsPath, $"{currentChecksum}.preset");

            // selectedPresetPath ends with .data because that is what is being used to display the background,
            // this just ensures we copy from the right file
            string presetSourcePath = selectedPresetPath.EndsWith(".data")
                ? selectedPresetPath.Substring(0, selectedPresetPath.Length - 5)
                : selectedPresetPath;

            File.Copy(presetSourcePath, currentPresetPath, true);
            _settingsHandler?.ActivatePreset(currentPresetPath);
            _viewModel.PresetExistsForCurrentChecksum = true;

            CreatePresetData();
            UpdateViewModel();
            UpdateDeafenKeybindDisplay();
            try
            {
                _chartManager.UpdateDeafenOverlaySection(_viewModel.MinCompletionPercentage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception while updating Deafen Section after applying preset: {ex}");
            }

            btn.Flyout?.Hide();
        }
    }

    /// <summary>
    ///     Opens the preset selection flyout and refreshes the presets list
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshPresets();
        if (sender is Button btn) btn.Flyout?.ShowAt(btn);
    }

    private void DeleteAllPresetsButtonYes_Click(object sender, RoutedEventArgs e)
    {
        DeleteAllPresetsButton.Flyout?.Hide();
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        if (Directory.Exists(presetsPath))
        {
            string[] presetFiles = Directory.GetFiles(presetsPath, "*.preset");
            foreach (string file in presetFiles)
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Could not delete preset file {file}: {ex}");
                }
        }

        DeleteAllPresetData();
        _settingsHandler?.DeactivatePreset();
        _viewModel.PresetExistsForCurrentChecksum = false;
        UpdateViewModel();
        UpdateDeafenKeybindDisplay();
        try
        {
            _chartManager.UpdateDeafenOverlaySection(_viewModel.MinCompletionPercentage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception while updating Deafen Section after deleting all presets: {ex}");
        }
    }

    private void DeleteAllPresetsButtonNo_Click(object sender, RoutedEventArgs e)
    {
        DeleteAllPresetsButton.Flyout?.Hide();
    }

    /// <summary>
    ///     Shows a flyout for the user to set a new deafen keybind
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void DeafenKeybindButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsViewModel.IsKeybindCaptureFlyoutOpen = !_settingsViewModel.IsKeybindCaptureFlyoutOpen;
        if (DeafenKeybindButton.Flyout is not Flyout flyout) return;
        if (_settingsViewModel.IsKeybindCaptureFlyoutOpen)
            flyout.ShowAt(DeafenKeybindButton, true);
        else
            flyout.Hide();
    }

    public async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await _updateCheckLock.WaitAsync(0))
            return;

        try
        {
            Button? button = this.FindControl<Button>("CheckForUpdatesButton");
            if (button == null) return;

            button.Content = "Checking for updates...";
            await Task.Delay(1000);

            await _updateChecker.CheckForUpdatesAsync();
            if (_updateChecker?.Mgr.IsInstalled == false)
            {
                button.Content = "Velopack not installed...";
                await Task.Delay(1000);
                button.Content = "Please reinstall osuautodeafen";
                await Task.Delay(1000);
                button.Content = "Check for Updates";
                return;
            }

            if (_updateChecker?.UpdateInfo == null)
            {
                button.Content = "No updates found";
                await Task.Delay(1000);
                button.Content = "Check for Updates";
                return;
            }

            button.Content = "Update available!";
            await _updateChecker.ShowUpdateNotification();
        }
        finally
        {
            _updateCheckLock.Release();
        }
    }

    /// <summary>
    ///     Opens the file location of the osuautodeafen appdata folder
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OpenFileLocationButton_Click(object? sender, RoutedEventArgs e)
    {
        string? appPath = _settingsHandler?.GetPath();
        if (appPath != null)
        {
            if (Directory.Exists(appPath))
                Process.Start(new ProcessStartInfo
                {
                    FileName = appPath,
                    UseShellExecute = true
                });
            else
                Console.WriteLine($"[ERROR] Directory does not exist: {appPath}");
        }
        else
        {
            Console.WriteLine("[ERROR] App path is null.");
        }
    }

    /// <summary>
    ///     Opens a new GitHub issue creation page
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ReportIssueButton_Click(object? sender, RoutedEventArgs e)
    {
        string issueUrl =
            "https://github.com/aerodite/osuautodeafen/issues/new?template=help.md&title=[BUG]%20Something%20Broke&body=help&labels=bug";
        Process.Start(new ProcessStartInfo
        {
            FileName = issueUrl,
            UseShellExecute = true
        });
    }
}