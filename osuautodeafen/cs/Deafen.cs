using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using AutoHotkey.Interop;
using Timer = System.Timers.Timer;
using System.Timers;
using Avalonia.Input;
using osuautodeafen.cs;
using osuautodeafen.cs.Screen;

namespace osuautodeafen
{
    public class Deafen : IDisposable
    {
        private readonly TosuApi _tosuAPI;
        private readonly AutoHotkeyEngine _ahk;
        private readonly Timer _timer;
        private readonly SharedViewModel _viewModel;
        private readonly FCCalc _fcCalc;
        private bool _isPlaying = false;
        private bool _hasReachedMinPercent = false;
        private bool _deafened = false;
        public double MinCompletionPercentage;
        private Timer _fileCheckTimer;
        public double StarRating;
        public double PerformancePoints;
        private bool _wasFullCombo;
        private bool _isFileCheckTimerRunning = false;
        private ScreenBlankerForm _screenBlanker;
        private bool screenBlankEnabled;
        private bool isScreenBlanked;

        public Deafen(TosuApi tosuAPI, SettingsPanel settingsPanel, ScreenBlankerForm screenBlanker)
        {
            _tosuAPI = tosuAPI;
            _ahk = AutoHotkeyEngine.Instance;
            _viewModel = new SharedViewModel();
            _fcCalc = new FCCalc(tosuAPI);
            _timer = new Timer(250);

            _timer.Elapsed += TimerElapsed;
            _timer.Elapsed += (sender, e) => ReadSettings();
            _timer.AutoReset = true;
            _timer.Start();

            _tosuAPI.StateChanged += TosuAPI_StateChanged;

            _fileCheckTimer = new Timer(1000);
            _fileCheckTimer.Elapsed += FileCheckTimer_Elapsed;
            _fileCheckTimer.Start();

            var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "osuautodeafen", "settings.txt");
            if (File.Exists(settingsFilePath))
            {
                var lines = File.ReadAllLines(settingsFilePath);
                foreach (var line in lines)
                {
                    var settings = line.Split('=');
                    if (settings.Length == 2)
                    {
                        switch (settings[0].Trim())
                        {
                            case "MinCompletionPercentage" when
                                double.TryParse(settings[1], out double parsedPercentage):
                                MinCompletionPercentage = parsedPercentage;
                                break;
                            case "StarRating" when
                                double.TryParse(settings[1], out double parsedStarRating):
                                StarRating = parsedStarRating;
                                break;
                            case "PerformancePoints" when
                                double.TryParse(settings[1], out double parsedPerformancePoints):
                                PerformancePoints = parsedPerformancePoints;
                                break;
                        }
                    }
                }
            }
            else
            {
                MinCompletionPercentage = 60;
                StarRating = 0;
                PerformancePoints = 0;
            }

            _screenBlanker = screenBlanker;
        }

        private async Task ToggleScreenBlankAsync()
        {
            await _screenBlanker.BlankScreensAsync();
        }

        private async Task ToggleScreenDeBlankAsync()
        {
            await _screenBlanker.UnblankScreensAsync();
        }

        private void FileCheckTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_isFileCheckTimerRunning) return;
            _isFileCheckTimerRunning = true;

            try
            {
                _viewModel.UpdateIsFCRequired();
                _viewModel.UpdateUndeafenAfterMiss();
                _viewModel.UpdateIsBlankScreenEnabled();
                screenBlankEnabled = _viewModel.IsBlankScreenEnabled;
                //Console.WriteLine($"screenBlankEnabled: {screenBlankEnabled}");
                string settingsFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "osuautodeafen", "settings.txt");
                if (File.Exists(settingsFilePath))
                {
                    var lines = File.ReadAllLines(settingsFilePath);
                    foreach (var line in lines)
                    {
                        var settings = line.Split('=');
                        if (settings.Length == 2)
                        {
                            switch (settings[0].Trim())
                            {
                                case "MinCompletionPercentage"
                                    when double.TryParse(settings[1], out double parsedPercentage):
                                    MinCompletionPercentage = parsedPercentage;
                                    break;
                                case "StarRating" when double.TryParse(settings[1], out double parsedStarRating):
                                    StarRating = parsedStarRating;
                                    break;
                                case "PerformancePoints"
                                    when double.TryParse(settings[1], out double parsedPerformancePoints):
                                    PerformancePoints = parsedPerformancePoints;
                                    break;
                                case "ScreenBlankEnabled"
                                    when bool.TryParse(settings[1], out bool parsedScreenBlankEnabled):
                                    screenBlankEnabled = parsedScreenBlankEnabled;
                                    break;
                            }
                        }
                    }
                }
                else
                {
                    MinCompletionPercentage = 60;
                    StarRating = 0;
                    PerformancePoints = 0;
                    screenBlankEnabled = false;
                }
            }
            finally
            {
                _isFileCheckTimerRunning = false;
            }
        }

        public double GetMinCompletionPercentage()
        {
            return MinCompletionPercentage;
        }

        private void TosuAPI_StateChanged(int state)
        {
            _isPlaying = (state == 2);
            if (!_isPlaying && !_wasFullCombo && _deafened)
            {
                Console.WriteLine("8-TSC");
                ToggleDeafenState();
                _deafened = false;
                if (screenBlankEnabled)
                {
                    ToggleScreenDeBlankAsync();
                }
            }
        }

        private async void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            var completionPercentage = _tosuAPI.GetCompletionPercentage();
            var currentSR = _tosuAPI.GetFullSR();
            var currentPP = _tosuAPI.GetMaxPP();
            var maxCombo = _tosuAPI.GetMaxCombo();
            var rankedStatus = _tosuAPI.GetRankedStatus();
            bool hitOneCircle;
            bool isPracticeDifficulty;
            bool didHitOneCircle = false;
            hitOneCircle = maxCombo != 0;


            isPracticeDifficulty = rankedStatus == 1;

            bool isStarRatingMet = currentSR >= StarRating;
            bool isPerformancePointsMet = currentPP >= PerformancePoints;
            bool isFullCombo = _fcCalc.IsFullCombo();

            // dogshit code up ahead you've been warned.
            // i might have to rewrite a lot of this
            // any more toggles and this might be unmaintainable

            if (_viewModel.IsFCRequired)
            {
                // if the user wants to deafen after a full combo
                if (_isPlaying && isFullCombo && completionPercentage >= MinCompletionPercentage && !_deafened && isStarRatingMet && isPerformancePointsMet && !_deafened && hitOneCircle && !isPracticeDifficulty)
                {
                    ToggleDeafenState();
                    _deafened = true;
                    _wasFullCombo = true;
                    Console.WriteLine("1");
                    didHitOneCircle = true;
                    if(screenBlankEnabled)
                    {
                        await ToggleScreenBlankAsync();
                    }
                }
                // if the user wants to undeafen after a combo break
                if (_viewModel.UndeafenAfterMiss)
                {
                    if (_wasFullCombo && !isFullCombo && _deafened && !isPracticeDifficulty)
                    {
                        ToggleDeafenState();
                        _deafened = false;
                        _wasFullCombo = false;
                        Console.WriteLine("2");
                        didHitOneCircle = true;
                        if (screenBlankEnabled)
                        {
                            await ToggleScreenDeBlankAsync();
                        }
                    }
                }
                // if the playing state was exited during a full combo run
                if (!_isPlaying && _wasFullCombo && _deafened)
                {
                    ToggleDeafenState();
                    _deafened = false;
                    _wasFullCombo = false;
                    Console.WriteLine("6");
                    didHitOneCircle = false;
                    if (screenBlankEnabled)
                    {
                        await ToggleScreenDeBlankAsync();
                    }
                }
            }
            else
            {
                // if the user wants to deafen after a certain percentage
                if (_isPlaying && !_deafened && completionPercentage >= MinCompletionPercentage && isStarRatingMet && isPerformancePointsMet && hitOneCircle && !isPracticeDifficulty)
                {
                    ToggleDeafenState();
                    _deafened = true;
                    Console.WriteLine("3");
                    didHitOneCircle = true;
                    if (screenBlankEnabled)
                    {
                        await ToggleScreenBlankAsync();
                    }
                }
            }

            if (!_isPlaying && _deafened)
            {
                didHitOneCircle = false;
                if (!_viewModel.IsFCRequired)
                {
                    ToggleDeafenState();
                    _deafened = false;
                    Console.WriteLine("4");
                    if (screenBlankEnabled)
                    {
                        await ToggleScreenDeBlankAsync();
                    }
                }
                else if (_viewModel.IsFCRequired && _wasFullCombo && !isFullCombo && _deafened)
                {
                    _deafened = false;
                    Console.WriteLine("5");
                    if (screenBlankEnabled)
                    {
                        await ToggleScreenDeBlankAsync();
                    }
                }
            }

            // this is assuming a retry occured.
            if (completionPercentage <= 0 && _deafened)
            {
                ToggleDeafenState();
                _deafened = false;
                Console.WriteLine("7");

                if (screenBlankEnabled)
                {
                    await ToggleScreenDeBlankAsync();
                }
            }
        }



        private string _customKeybind = "^p";

        private void ReadSettings()
        {
            string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "osuautodeafen", "settings.txt");
            if (File.Exists(settingsFilePath))
            {
                var lines = File.ReadAllLines(settingsFilePath);
                foreach (var line in lines)
                {
                    var settings = line.Split('=');
                    if (settings.Length == 2)
                    {
                        switch (settings[0].Trim())
                        {
                            case "Hotkey":
                                _customKeybind = ConvertToAHKSyntax(settings[1]);
                                break;
                        }
                    }
                }
            }
        }

        private string ConvertToAHKSyntax(string keybind)
        {
            var parts = keybind.Split('+');
            string ahkKeybind = "";
            var specialKeys = new HashSet<string>
            {
                "Control", "Ctrl", "Alt", "Shift", "Win", "Tab", "Enter", "Escape", "Esc", "Space", "Backspace",
                "Delete", "Insert", "Home", "End", "PgUp", "PgDn", "Up", "Down", "Left", "Right"
            };
            var functionKeys = Enumerable.Range(1, 24).Select(i => $"F{i}").ToHashSet();
            var mediaKeys = new HashSet<string>
                { "Volume_Up", "Volume_Down", "Media_Play_Pause", "Media_Next", "Media_Prev", "Media_Stop" };
            var numpadKeys = Enumerable.Range(0, 10).Select(i => $"NumPad{i}").ToHashSet();

            foreach (var part in parts)
            {
                var trimmedPart = part.Trim();
                if (specialKeys.Contains(trimmedPart) || functionKeys.Contains(trimmedPart) ||
                    mediaKeys.Contains(trimmedPart) || numpadKeys.Contains(trimmedPart))
                {
                    ahkKeybind += $"{{{trimmedPart}}}";
                }
                else
                {
                    switch (trimmedPart)
                    {
                        case "Control":
                        case "Ctrl":
                            ahkKeybind += "^";
                            break;
                        case "Alt":
                            ahkKeybind += "!";
                            break;
                        case "Shift":
                            ahkKeybind += "+";
                            break;
                        case "Win":
                            ahkKeybind += "#";
                            break;
                        default:
                            ahkKeybind += trimmedPart;
                            break;
                    }
                }
            }

            return ahkKeybind;
        }

        private void ToggleDeafenState()
        {
            _ahk.ExecRaw($"Send, {ConvertToAHKSyntax(_customKeybind)}");
            Console.WriteLine(_hasReachedMinPercent ? $"Sent {ConvertToAHKSyntax(_customKeybind)} (undeafen)" : $"Sent {ConvertToAHKSyntax(_customKeybind)} (deafen)");
            _hasReachedMinPercent = !_hasReachedMinPercent;
        }

        public void Dispose()
        {
            _timer.Dispose();
            _fileCheckTimer.Dispose();
            _screenBlanker?.Dispose();
        }
    }
}