using System;
using System.IO;
using System.Timers;
using AutoHotkey.Interop;
using Timer = System.Timers.Timer;
using System.Timers;
using Avalonia.Input;
using osuautodeafen.cs;

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
        private double MinCompletionPercentage;
        private Timer _fileCheckTimer;
        public double StarRating;
        public double PerformancePoints;
        private bool _wasFullCombo;

        public Deafen(TosuApi tosuAPI, SettingsPanel settingsPanel)
        {
            _tosuAPI = tosuAPI;
            _ahk = AutoHotkeyEngine.Instance;
            _viewModel = new SharedViewModel();
            _fcCalc = new FCCalc(tosuAPI);
            _timer = new System.Timers.Timer(250);
            _timer.Elapsed += TimerElapsed;
            _timer.AutoReset = true;
            _timer.Start();


            _tosuAPI.StateChanged += TosuAPI_StateChanged;

            _fileCheckTimer = new Timer(5000);
            _fileCheckTimer.Elapsed += FileCheckTimer_Elapsed;
            _fileCheckTimer.Start();

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
                MinCompletionPercentage = 75;
                StarRating = 0;
                PerformancePoints = 0;
            }
        }

        private void FileCheckTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _viewModel.UpdateIsFCRequired();
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
                            case "MinCompletionPercentage" when double.TryParse(settings[1], out double parsedPercentage):
                                MinCompletionPercentage = parsedPercentage;
                                break;
                            case "StarRating" when double.TryParse(settings[1], out double parsedStarRating):
                                StarRating = parsedStarRating;
                                break;
                            case "PerformancePoints" when double.TryParse(settings[1], out double parsedPerformancePoints):
                                PerformancePoints = parsedPerformancePoints;
                                break;
                        }
                    }
                }
            }
            else
            {
                MinCompletionPercentage = 75;
                StarRating = 0;
                PerformancePoints = 0;
            }
        }

        private void TosuAPI_StateChanged(int state)
        {
            _isPlaying = (state == 2);
            if (!_isPlaying && !_wasFullCombo && _deafened)
            {
                ToggleDeafenState();
                _deafened = false;
            }
        }

        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            var completionPercentage = _tosuAPI.GetCompletionPercentage();
            var currentSR = _tosuAPI.GetFullSR();
            var currentPP = _tosuAPI.GetMaxPP();
            var maxCombo = _tosuAPI.GetMaxCombo();
            var rankedStatus = _tosuAPI.GetRankedStatus();
            bool hitOneCircle;
            bool isPracticeDifficulty;

            hitOneCircle = maxCombo != 0;

            isPracticeDifficulty = rankedStatus == 1;

            bool isStarRatingMet = currentSR >= StarRating;
            bool isPerformancePointsMet = currentPP >= PerformancePoints;
            bool isFullCombo = _fcCalc.IsFullCombo();

            if (_viewModel.IsFCRequired)
            {
                if (_isPlaying && isFullCombo && completionPercentage >= MinCompletionPercentage && !_deafened && isStarRatingMet && isPerformancePointsMet && !_deafened && hitOneCircle && !isPracticeDifficulty)
                {
                    ToggleDeafenState();
                    _deafened = true;
                    _wasFullCombo = true;
                    Console.WriteLine("1");
                }
                else if (_wasFullCombo && !isFullCombo && _deafened && !isPracticeDifficulty)
                {
                    ToggleDeafenState();
                    _deafened = false;
                    _wasFullCombo = false;
                    Console.WriteLine("2");
                }
                if (!_isPlaying && _wasFullCombo && _deafened)
                {
                    ToggleDeafenState();
                    _deafened = false;
                    _wasFullCombo = false;
                    Console.WriteLine("6");
                }
            }
            else
            {
                if (_isPlaying && !_deafened && completionPercentage >= MinCompletionPercentage && isStarRatingMet && isPerformancePointsMet && hitOneCircle && !isPracticeDifficulty)
                {
                    ToggleDeafenState();
                    _deafened = true;
                    Console.WriteLine("3");
                }
            }

            if (!_isPlaying && _deafened)
            {
                if (!_viewModel.IsFCRequired)
                {
                    ToggleDeafenState();
                    _deafened = false;
                    Console.WriteLine("4");
                }
                else if (_viewModel.IsFCRequired && _wasFullCombo && !isFullCombo && _deafened)
                {
                    _deafened = false;
                    Console.WriteLine("5");
                }
            }
        }
        //this is for custom hotkeys. not ready yet at all lol
       /* private void ToggleDeafenState()
        {
            string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "osuautodeafen", "settings.txt");
            string hotkeyString = File.ReadAllText(settingsFilePath);

            string[] parts = hotkeyString.Split(new[] { '=' }, 2);
            if (parts.Length != 2 || parts[0].Trim() != "Hotkey")
            {
                throw new FormatException("Invalid hotkey setting format.");
            }

            string[] keys = parts[1].Split('+');
            KeyModifiers modifiers = KeyModifiers.None;
            Key key = Key.None;

            foreach (string k in keys)
            {
                string keyString = k.Trim();
                switch (keyString)
                {
                    case "Control":
                        modifiers |= KeyModifiers.Control;
                        break;
                    case "Alt":
                        modifiers |= KeyModifiers.Alt;
                        break;
                    case "Shift":
                        modifiers |= KeyModifiers.Shift;
                        break;
                    default:
                        key = (Key)Enum.Parse(typeof(Key), keyString);
                        break;
                }
            }

            MainWindow.HotKey hotkey = new MainWindow.HotKey { Key = key, ModifierKeys = modifiers };

            string ahkCommand = "Send, ";
            if (hotkey.ModifierKeys.HasFlag(KeyModifiers.Control))
            {
                ahkCommand += "^";
            }
            if (hotkey.ModifierKeys.HasFlag(KeyModifiers.Alt))
            {
                ahkCommand += "!";
            }
            if (hotkey.ModifierKeys.HasFlag(KeyModifiers.Shift))
            {
                ahkCommand += "+";
            }
            ahkCommand += hotkey.Key.ToString().ToLower(); // Convert the key to lowercase

            _ahk.ExecRaw(ahkCommand);

            _hasReachedMinPercent = !_hasReachedMinPercent;
        }*/
       private void ToggleDeafenState()
       {
           _ahk.ExecRaw("Send, ^p");
           Console.WriteLine(_hasReachedMinPercent ? "Sent Ctrl + P (undeafen)" : "Sent Ctrl + P (deafen)");
           _hasReachedMinPercent = !_hasReachedMinPercent;
       }

        public void Dispose()
        {
            _timer.Dispose();
            _fileCheckTimer.Dispose();
        }
    }
}