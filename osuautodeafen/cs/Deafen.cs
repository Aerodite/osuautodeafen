using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using osuautodeafen.cs;
using SharpHook;
using SharpHook.Data;
using SharpHook.Native;
using Timer = System.Timers.Timer;

namespace osuautodeafen;

public class Deafen : IDisposable
{
    private readonly BreakPeriodCalculator _breakPeriodCalculator;
    private readonly object _deafenLock = new();
    private readonly FCCalc _fcCalc;
    private readonly Timer _fileCheckTimer;
    private readonly IGlobalHook _hook = new SimpleGlobalHook();
    private readonly ScreenBlankerForm _screenBlanker;
    private readonly Timer _timer;

    private readonly SemaphoreSlim _timerSemaphore = new(1, 1);
    private readonly SemaphoreSlim _toggleDeafenLock = new(1, 1);
    private readonly TosuApi _tosuAPI;
    private readonly SharedViewModel _viewModel;

    private string _customKeybind;
    private bool _deafened;
    private bool _hasReachedMinPercent;
    private bool _isFileCheckTimerRunning;
    private bool _isInBreakPeriod;

    private bool _isInBreakPeriodUndeafened;
    private bool _isPlaying;
    private bool _wasFullCombo;
    private bool breakUndeafenEnabled;
    private bool isScreenBlanked;
    public double MinCompletionPercentage; // User Set Minimum Completion Percentage
    public double PerformancePoints; // User Set Minimum Performance Points
    public bool screenBlankEnabled;
    public double StarRating; // User Set Minimum Star Rating

    public Deafen(TosuApi tosuAPI, SettingsPanel settingsPanel, BreakPeriodCalculator breakPeriodCalculator,
        SharedViewModel viewModel)
    {
        _tosuAPI = tosuAPI;
        _fcCalc = new FCCalc(tosuAPI);
        _timer = new Timer(250);
        _timer.Elapsed += TimerElapsed;
        _timer.Start();
        _tosuAPI.StateChanged += TosuAPI_StateChanged;

        _fileCheckTimer = new Timer(1000);
        _fileCheckTimer.Elapsed += FileCheckTimer_Elapsed;
        _fileCheckTimer.Start();

        _breakPeriodCalculator = breakPeriodCalculator;
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        LoadSettings();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _fileCheckTimer.Dispose();
        _screenBlanker?.Dispose();
        _hook.Dispose();
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
            _viewModel.UpdateIsBreakUndeafenToggleEnabled();
            screenBlankEnabled = _viewModel.IsBlankScreenEnabled;
            LoadSettings();
        }
        finally
        {
            _isFileCheckTimerRunning = false;
        }
    }

    private void LoadSettings()
    {
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");
        if (File.Exists(settingsFilePath))
        {
            var lines = File.ReadAllLines(settingsFilePath);
            foreach (var line in lines)
            {
                var settings = line.Split('=');
                if (settings.Length == 2)
                    switch (settings[0].Trim())
                    {
                        case "MinCompletionPercentage" when double.TryParse(settings[1], out var parsedPercentage):
                            MinCompletionPercentage = parsedPercentage;
                            break;
                        case "StarRating" when double.TryParse(settings[1], out var parsedStarRating):
                            StarRating = parsedStarRating;
                            break;
                        case "PerformancePoints" when double.TryParse(settings[1], out var parsedPerformancePoints):
                            PerformancePoints = parsedPerformancePoints;
                            break;
                        case "Hotkey":
                            _customKeybind = settings[1].Trim();
                            break;
                        case "IsScreenBlankEnabled" when bool.TryParse(settings[1], out var parsedScreenBlankEnabled):
                            screenBlankEnabled = parsedScreenBlankEnabled;
                            break;
                        case "BreakUndeafenEnabled" when bool.TryParse(settings[1], out var parsedBreakUndeafenEnabled):
                            breakUndeafenEnabled = parsedBreakUndeafenEnabled;
                            break;
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

    private async void TosuAPI_StateChanged(int state)
    {
        _isPlaying = state == 2;
    }

    private async void TimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!await _timerSemaphore.WaitAsync(250)) return;

        try
        {
            LoadSettings();

            var completionPercentage = Math.Round(_tosuAPI.GetCompletionPercentage(), 2);
            var currentSR = _tosuAPI.GetFullSR();
            var currentPP = _tosuAPI.GetMaxPP();
            var minCompletionPercentage = MinCompletionPercentage;
            var maxCombo = _tosuAPI.GetMaxCombo();
            var rankedStatus = _tosuAPI.GetRankedStatus();
            var isFullCombo = _fcCalc.IsFullCombo();
            var isStarRatingMet = currentSR >= StarRating;
            var isPerformancePointsMet = currentPP >= PerformancePoints;
            var hitOneCircle = maxCombo != 0;
            var isPracticeDifficulty = rankedStatus == 1;
            var isPlaying = _tosuAPI.GetRawBanchoStatus() == 2;

            if (_viewModel.IsFCRequired)
            {
                // Deafen after a full combo
                if (isPlaying && isFullCombo && completionPercentage >= minCompletionPercentage && !_deafened &&
                    isStarRatingMet && isPerformancePointsMet && hitOneCircle && !isPracticeDifficulty)
                {
                    _deafened = true;
                    ToggleDeafenState();
                    _wasFullCombo = true;
                    Console.WriteLine("1");
                    if (screenBlankEnabled) await ToggleScreenBlankAsync();
                }
                // Undeafen after a combo break
                else if (_viewModel.UndeafenAfterMiss && _wasFullCombo && !isFullCombo && _deafened &&
                         !isPracticeDifficulty)
                {
                    _deafened = false;
                    ToggleDeafenState();
                    _wasFullCombo = false;
                    Console.WriteLine("2");
                    if (screenBlankEnabled) await ToggleScreenDeBlankAsync();
                }
                // Undeafen if playing state was exited during a full combo run
                else if (!isPlaying && _wasFullCombo && _deafened)
                {
                    _deafened = false;
                    ToggleDeafenState();
                    _wasFullCombo = false;
                    Console.WriteLine("6");
                    if (screenBlankEnabled) await ToggleScreenDeBlankAsync();
                }
            }
            else
            {
                // Deafen after a certain percentage
                if (isPlaying && !_deafened && completionPercentage >= minCompletionPercentage && isStarRatingMet &&
                    isPerformancePointsMet && hitOneCircle && !isPracticeDifficulty && !_isInBreakPeriod)
                {
                    _deafened = true;
                    ToggleDeafenState();
                    Console.WriteLine("3");
                    if (screenBlankEnabled) await ToggleScreenBlankAsync();
                }
            }

            // Undeafen if not playing
            if (!isPlaying && _deafened)
            {
                if (!_viewModel.IsFCRequired)
                {
                    _deafened = false;
                    ToggleDeafenState();
                    Console.WriteLine("4");
                    if (screenBlankEnabled) await ToggleScreenDeBlankAsync();
                }
                else if (_viewModel.IsFCRequired && _wasFullCombo && !isFullCombo && _deafened)
                {
                    _deafened = false;
                    Console.WriteLine("5");
                    if (screenBlankEnabled) await ToggleScreenDeBlankAsync();
                }
            }

            // Undeafen if a retry occurred
            if (completionPercentage <= 0 && _deafened)
            {
                _deafened = false;
                ToggleDeafenState();
                Console.WriteLine("7");
                if (screenBlankEnabled) await ToggleScreenDeBlankAsync();
            }

            // Undeafen during break periods if the toggle is enabled
            if (breakUndeafenEnabled)
            {
                completionPercentage = Math.Round(_tosuAPI.GetCompletionPercentage(), 2);
                var isInBreakPeriod = _breakPeriodCalculator.IsBreakPeriod(completionPercentage);

                //Console.WriteLine($"BreakUndeafenEnabled: {breakUndeafenEnabled}, CompletionPercentage: {completionPercentage}, IsInBreakPeriod: {isInBreakPeriod}, IsInBreakPeriodFlag: {_isInBreakPeriod}, Deafened: {_deafened}");

                if (isInBreakPeriod && !_isInBreakPeriod)
                {
                    _isInBreakPeriod = true;
                    if (_deafened && isPlaying)
                    {
                        _deafened = false;
                        _isInBreakPeriodUndeafened = true;
                        ToggleDeafenState();
                        if (screenBlankEnabled) await ToggleScreenDeBlankAsync();
                        await Task.Delay(500); // Add a delay to prevent immediate re-evaluation
                    }
                }
                else if (!isInBreakPeriod && _isInBreakPeriod)
                {
                    _isInBreakPeriod = false;
                    if (_isInBreakPeriodUndeafened && isPlaying)
                    {
                        _deafened = true;
                        _isInBreakPeriodUndeafened = false;
                        ToggleDeafenState();
                        if (screenBlankEnabled) await ToggleScreenBlankAsync();
                        await Task.Delay(500); // Add a delay to prevent immediate re-evaluation
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred in the Deafen Timer: {ex.Message}");
        }
        finally
        {
            _timerSemaphore.Release();
        }
    }

    private List<(IEnumerable<KeyCode> Modifiers, KeyCode Key)> ConvertToInputSimulatorSyntax(string keybind)
    {
        var parts = keybind.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
        var keybinds = new List<(IEnumerable<KeyCode> Modifiers, KeyCode Key)>();
        var modifiers = new List<KeyCode>();
        var key = KeyCode.Vc0;

        var specialKeys = new Dictionary<string, KeyCode>
        {
            { "Control", KeyCode.VcLeftControl },
            { "Ctrl", KeyCode.VcLeftControl },
            { "LeftControl", KeyCode.VcLeftControl },
            { "RightControl", KeyCode.VcRightControl },
            { "Alt", KeyCode.VcLeftAlt },
            { "LeftAlt", KeyCode.VcLeftAlt },
            { "RightAlt", KeyCode.VcRightAlt },
            { "Shift", KeyCode.VcLeftShift },
            { "LeftShift", KeyCode.VcLeftShift },
            { "RightShift", KeyCode.VcRightShift },
            { "Win", KeyCode.VcLeftMeta },
            { "LeftWin", KeyCode.VcLeftMeta },
            { "RightWin", KeyCode.VcRightMeta },
            { "Tab", KeyCode.VcTab },
            { "Enter", KeyCode.VcEnter },
            { "Escape", KeyCode.VcEscape },
            { "Esc", KeyCode.VcEscape },
            { "Space", KeyCode.VcSpace },
            { "Backspace", KeyCode.VcBackspace },
            { "Delete", KeyCode.VcDelete },
            { "Insert", KeyCode.VcInsert },
            { "Home", KeyCode.VcHome },
            { "End", KeyCode.VcEnd },
            { "PgUp", KeyCode.VcPageUp },
            { "PgDn", KeyCode.VcPageDown },
            { "Up", KeyCode.VcUp },
            { "Down", KeyCode.VcDown },
            { "Left", KeyCode.VcLeft },
            { "Right", KeyCode.VcRight },
            { "Minus", KeyCode.VcMinus },
            { "Plus", KeyCode.VcEquals },
            { "Comma", KeyCode.VcComma },
            { "Period", KeyCode.VcPeriod },
            { "Slash", KeyCode.VcSlash },
            { "Backslash", KeyCode.VcBackslash },
            { "Semicolon", KeyCode.VcSemicolon },
            { "Quote", KeyCode.VcQuote },
            { "OpenBracket", KeyCode.VcOpenBracket },
            { "CloseBracket", KeyCode.VcCloseBracket },
            { "CapsLock", KeyCode.VcCapsLock },
            { "NumLock", KeyCode.VcNumLock },
            { "ScrollLock", KeyCode.VcScrollLock },
            { "PrintScreen", KeyCode.VcPrintScreen },
            { "Pause", KeyCode.VcPause },
            { "F1", KeyCode.VcF1 },
            { "F2", KeyCode.VcF2 },
            { "F3", KeyCode.VcF3 },
            { "F4", KeyCode.VcF4 },
            { "F5", KeyCode.VcF5 },
            { "F6", KeyCode.VcF6 },
            { "F7", KeyCode.VcF7 },
            { "F8", KeyCode.VcF8 },
            { "F9", KeyCode.VcF9 },
            { "F10", KeyCode.VcF10 },
            { "F11", KeyCode.VcF11 },
            { "F12", KeyCode.VcF12 },
            { "Equals", KeyCode.VcEquals }
        };

        for (var i = 0; i < parts.Length; i++)
        {
            var trimmedPart = parts[i].Trim();
            if (specialKeys.ContainsKey(trimmedPart))
            {
                if (trimmedPart == "Shift" || trimmedPart == "Ctrl" || trimmedPart == "Alt" || trimmedPart == "Win" ||
                    trimmedPart == "LeftShift" || trimmedPart == "RightShift" || trimmedPart == "LeftControl" ||
                    trimmedPart == "RightControl" || trimmedPart == "LeftAlt" || trimmedPart == "RightAlt" ||
                    trimmedPart == "LeftWin" || trimmedPart == "RightWin")
                    modifiers.Add(specialKeys[trimmedPart]);
                else
                    key = specialKeys[trimmedPart];
            }
            else
            {
                if (trimmedPart.Length == 1 || (i == parts.Length - 1 && trimmedPart == "+"))
                    //should be noted that the only key that doesnt work is +, sorry to people that use + 💀
                {
                    if (trimmedPart == "-")
                        key = KeyCode.VcMinus;
                    else
                        key = (KeyCode)Enum.Parse(typeof(KeyCode), "Vc" + trimmedPart.ToUpper());
                }
                else
                {
                    throw new ArgumentException($"Invalid key: {trimmedPart}");
                }
            }
        }

        if (key == KeyCode.Vc0) throw new ArgumentException("Invalid keybind: no key specified.");

        keybinds.Add((modifiers, key));
        Console.WriteLine(
            $"Converted keybind: {keybind} to {string.Join(", ", keybinds.Select(k => $"{string.Join("+", k.Modifiers)}+{k.Key}"))}");
        return keybinds;
    }

    private void ToggleDeafenState()
    {
        // The key release events and delays are required for discord to recognize them as keystrokes
        lock (_deafenLock)
        {
            if (_deafened)
            {
                if (!string.IsNullOrEmpty(_customKeybind))
                {
                    var keybinds = ConvertToInputSimulatorSyntax(_customKeybind);
                    foreach (var keybind in keybinds)
                    {
                        foreach (var modifier in keybind.Modifiers)
                        {
                            var modifierPressEvent = new UioHookEvent
                            {
                                Type = EventType.KeyPressed,
                                Keyboard = new KeyboardEventData { KeyCode = modifier }
                            };
                            Console.WriteLine($"Sending key press for modifier: {modifier}");
                            UioHook.PostEvent(ref modifierPressEvent);
                            Thread.Sleep(50);
                        }

                        var keyPressEvent = new UioHookEvent
                        {
                            Type = EventType.KeyPressed,
                            Keyboard = new KeyboardEventData { KeyCode = keybind.Key }
                        };
                        Console.WriteLine($"Sending key press for key: {keybind.Key}");
                        UioHook.PostEvent(ref keyPressEvent);
                        Thread.Sleep(50);

                        var keyReleaseEvent = new UioHookEvent
                        {
                            Type = EventType.KeyReleased,
                            Keyboard = new KeyboardEventData { KeyCode = keybind.Key }
                        };
                        Console.WriteLine($"Sending key release for key: {keybind.Key}");
                        UioHook.PostEvent(ref keyReleaseEvent);
                        Thread.Sleep(50);

                        foreach (var modifier in keybind.Modifiers)
                        {
                            var modifierReleaseEvent = new UioHookEvent
                            {
                                Type = EventType.KeyReleased,
                                Keyboard = new KeyboardEventData { KeyCode = modifier }
                            };
                            Console.WriteLine($"Sending key release for modifier: {modifier}");
                            UioHook.PostEvent(ref modifierReleaseEvent);
                            Thread.Sleep(50);
                        }
                    }

                    Console.WriteLine("Undeafened");
                }
            }
            else
            {
                var keybinds = ConvertToInputSimulatorSyntax(_customKeybind);
                foreach (var keybind in keybinds)
                {
                    foreach (var modifier in keybind.Modifiers)
                    {
                        var modifierPressEvent = new UioHookEvent
                        {
                            Type = EventType.KeyPressed,
                            Keyboard = new KeyboardEventData { KeyCode = modifier }
                        };
                        Console.WriteLine($"Sending key press for modifier: {modifier}");
                        UioHook.PostEvent(ref modifierPressEvent);
                        Thread.Sleep(50);
                    }

                    var keyPressEvent = new UioHookEvent
                    {
                        Type = EventType.KeyPressed,
                        Keyboard = new KeyboardEventData { KeyCode = keybind.Key }
                    };
                    Console.WriteLine($"Sending key press for key: {keybind.Key}");
                    UioHook.PostEvent(ref keyPressEvent);
                    Thread.Sleep(50);

                    var keyReleaseEvent = new UioHookEvent
                    {
                        Type = EventType.KeyReleased,
                        Keyboard = new KeyboardEventData { KeyCode = keybind.Key }
                    };
                    Console.WriteLine($"Sending key release for key: {keybind.Key}");
                    UioHook.PostEvent(ref keyReleaseEvent);
                    Thread.Sleep(50);

                    foreach (var modifier in keybind.Modifiers)
                    {
                        var modifierReleaseEvent = new UioHookEvent
                        {
                            Type = EventType.KeyReleased,
                            Keyboard = new KeyboardEventData { KeyCode = modifier }
                        };
                        Console.WriteLine($"Sending key release for modifier: {modifier}");
                        UioHook.PostEvent(ref modifierReleaseEvent);
                        Thread.Sleep(50);
                    }
                }

                Console.WriteLine("Deafened");
            }
        }
    }
}