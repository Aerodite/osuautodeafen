using System;
using System.Collections.Generic;
using System.Linq;
using osuautodeafen.cs.Settings;
using SharpHook;
using SharpHook.Data;
using Timer = System.Timers.Timer;

namespace osuautodeafen.cs.Deafen;

public class Deafen : IDisposable
{
    private readonly object _deafenLock = new();
    private readonly EventSimulator _eventSimulator = new();
    private readonly SimpleGlobalHook _hook;
    private readonly SettingsHandler _settingsHandler;
    private readonly SharedViewModel _sharedViewModel;
    private readonly Timer _timer;
    private readonly TosuApi _tosuAPI;
    public bool _deafened;
    private bool _isInBreakPeriod;

    public Action? Deafened;
    public Action? Undeafened;

    public Deafen(TosuApi tosuAPI, SettingsHandler settingsHandler, SharedViewModel sharedViewModel)
    {
        _tosuAPI = tosuAPI;
        _hook = new SimpleGlobalHook();
        _sharedViewModel = sharedViewModel;
        _settingsHandler = settingsHandler;

        // handled separately from maintimer just in case
        _timer = new Timer(100);
        _timer.Elapsed += (_, _) => CheckAndDeafen();
        _timer.Start();
    }

    private bool IsUndeafenAfterMissEnabled => _sharedViewModel.UndeafenAfterMiss;
    private bool IsBreakUndeafenToggleEnabled => _sharedViewModel.IsBreakUndeafenToggleEnabled;

    public void Dispose()
    {
        Console.WriteLine("[Dispose] Disposing Deafen resources.");
        _hook.Dispose();
    }

    private void SimulateDeafenKey()
    {
        try
        {
            KeyCode key;
            List<KeyCode> modifiers = new();

            var keybindObj = _sharedViewModel.DeafenKeybind;
            if (keybindObj == null)
            {
                var keybindParts = _settingsHandler.DeafenKeybind?.Split(',');
                if (keybindParts == null || keybindParts.Length < 2)
                    throw new ArgumentException("Invalid DeafenKeybind format in settings.");

                key = (KeyCode)ushort.Parse(keybindParts[0]);
                var modValue = ushort.Parse(keybindParts[1]);
                if (modValue != 0)
                {
                    if ((modValue & 1) != 0) modifiers.Add(KeyCode.VcLeftAlt);
                    if ((modValue & 2) != 0) modifiers.Add(KeyCode.VcLeftControl);
                    if ((modValue & 4) != 0) modifiers.Add(KeyCode.VcLeftShift);
                    if ((modValue & 8) != 0) modifiers.Add(KeyCode.VcLeftMeta);
                }

                Console.WriteLine("[SimulateDeafenKey] DeafenKeybind was null, using settings handler value.");
            }
            else
            {
                // Fallback to string parsing if needed
                var keybinds = ConvertToInputSimulatorSyntax(keybindObj.ToString());
                modifiers = keybinds[0].Modifiers.ToList();
                key = keybinds[0].Key;
            }

            Console.WriteLine($"[SimulateDeafenKey] Pressing modifiers: {string.Join(", ", modifiers)}");
            foreach (var mod in modifiers)
                _eventSimulator.SimulateKeyPress(mod);

            Console.WriteLine($"[SimulateDeafenKey] Pressing main key: {key}");
            _eventSimulator.SimulateKeyPress(key);

            _eventSimulator.SimulateKeyRelease(key);
            Console.WriteLine($"[SimulateDeafenKey] Released main key: {key}");

            foreach (var mod in modifiers.AsEnumerable().Reverse())
                _eventSimulator.SimulateKeyRelease(mod);
            Console.WriteLine(
                $"[SimulateDeafenKey] Released modifiers: {string.Join(", ", modifiers.AsEnumerable().Reverse())}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SimulateDeafenKey] Exception: {ex}");
        }
    }

    private bool ShouldDeafen()
    {
        var completionPercentage = Math.Round(_tosuAPI.GetCompletionPercentage(), 2);
        var currentStarRating = _tosuAPI.GetFullSR();
        var currentPerformancePoints = _tosuAPI.GetMaxPP();
        double requiredCompletion = _sharedViewModel.MinCompletionPercentage;
        var requiredStarRating = _sharedViewModel.StarRating;
        double requiredPerformancePoints = _sharedViewModel.PerformancePoints;
        var isFCRequired = _sharedViewModel.IsFCRequired;
        var isPlaying = _tosuAPI.GetRawBanchoStatus() == 2;
        var notAlreadyDeafened = !_deafened;
        var completionMet = completionPercentage >= requiredCompletion;
        var starRatingMet = currentStarRating >= requiredStarRating;
        var performancePointsMet = currentPerformancePoints >= requiredPerformancePoints;
        var hasHitObjects = _tosuAPI.GetMaxCombo() != 0;
        var notPracticeDifficulty = (int)_tosuAPI.GetRankedStatus() != 1;
        var isFullCombo = _tosuAPI.IsFullCombo();
        _isInBreakPeriod = _tosuAPI.IsBreakPeriod();

        var fcRequirementMet = !isFCRequired || isFullCombo;
        var breakConditionMet = !IsBreakUndeafenToggleEnabled || !_isInBreakPeriod;
        var missConditionMet = !IsUndeafenAfterMissEnabled || isFullCombo;

        //Console.WriteLine($"[ShouldDeafen] isPlaying={isPlaying}, notAlreadyDeafened={notAlreadyDeafened}, completionMet={completionMet}, starRatingMet={starRatingMet}, performancePointsMet={performancePointsMet}, hasHitObjects={hasHitObjects}, notPracticeDifficulty={notPracticeDifficulty}, fcRequirementMet={fcRequirementMet}, breakConditionMet={breakConditionMet}, missConditionMet={missConditionMet}");

        return isPlaying
               && notAlreadyDeafened
               && completionMet
               && starRatingMet
               && performancePointsMet
               && hasHitObjects
               && notPracticeDifficulty
               && fcRequirementMet
               && breakConditionMet
               && missConditionMet;
    }

    private bool ShouldUndeafen()
    {
        var completionPercentage = Math.Round(_tosuAPI.GetCompletionPercentage(), 2);
        var isPlaying = _tosuAPI.GetRawBanchoStatus() == 2;
        var isFullCombo = _tosuAPI.IsFullCombo();
        var isFCRequired = _sharedViewModel.IsFCRequired;

        //Console.WriteLine($"[ShouldUndeafen] isPlaying={isPlaying}, completionPercentage={completionPercentage}, isFullCombo={isFullCombo}, _deafened={_deafened}, IsUndeafenAfterMissEnabled={IsUndeafenAfterMissEnabled}, IsBreakUndeafenToggleEnabled={IsBreakUndeafenToggleEnabled}, _isInBreakPeriod={_isInBreakPeriod}");

        if ((!isPlaying && _deafened) || (completionPercentage <= 0 && _deafened))
        {
            Console.WriteLine("[ShouldUndeafen] Undeafen: not playing or retried");
            return true;
        }

        //thanks jurme
        if (IsUndeafenAfterMissEnabled && !isFullCombo && isFCRequired && _deafened)
        {
            Console.WriteLine("[ShouldUndeafen] Undeafen: lost FC and setting enabled");
            return true;
        }

        if (IsBreakUndeafenToggleEnabled && _isInBreakPeriod && _deafened)
        {
            Console.WriteLine("[ShouldUndeafen] Undeafen: in break period and setting enabled");
            return true;
        }

        return false;
    }

    private void CheckAndDeafen()
    {
        if (ShouldDeafen())
        {
            Console.WriteLine("[CheckAndDeafen] Should deafen. Toggling deafen state.");
            ToggleDeafenState();
        }
        else if (ShouldUndeafen())
        {
            Console.WriteLine("[CheckAndDeafen] Should undeafen. Toggling deafen state.");
            ToggleDeafenState();
        }
    }

    private void ToggleDeafenState()
    {
        lock (_deafenLock)
        {
            Console.WriteLine($"[ToggleDeafenState] Toggling deafen state. Current: {_deafened}");
            SimulateDeafenKey();
            _deafened = !_deafened;
            Console.WriteLine($"[ToggleDeafenState] New deafen state: {_deafened}");
        }
    }

    private List<(IEnumerable<KeyCode> Modifiers, KeyCode Key)> ConvertToInputSimulatorSyntax(string? keybind)
    {
        Console.WriteLine($"[ConvertToInputSimulatorSyntax] Converting keybind: {keybind}");
        var parts = keybind?.Split(new[] { '+', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var keybinds = new List<(IEnumerable<KeyCode> Modifiers, KeyCode Key)>();
        var modifiers = new List<KeyCode>();
        var key = KeyCode.Vc0;

        var specialKeys = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase)
        {
            { "Control", KeyCode.VcLeftControl }, { "Ctrl", KeyCode.VcLeftControl },
            { "LeftControl", KeyCode.VcLeftControl }, { "RightControl", KeyCode.VcRightControl },
            { "Alt", KeyCode.VcLeftAlt }, { "LeftAlt", KeyCode.VcLeftAlt }, { "RightAlt", KeyCode.VcRightAlt },
            { "Shift", KeyCode.VcLeftShift }, { "LeftShift", KeyCode.VcLeftShift },
            { "RightShift", KeyCode.VcRightShift },
            { "Win", KeyCode.VcLeftMeta }, { "LeftWin", KeyCode.VcLeftMeta }, { "RightWin", KeyCode.VcRightMeta },
            { "Meta", KeyCode.VcLeftMeta },

            { "Tab", KeyCode.VcTab }, { "Enter", KeyCode.VcEnter }, { "Return", KeyCode.VcEnter },
            { "Space", KeyCode.VcSpace }, { "Spacebar", KeyCode.VcSpace },
            { "Backspace", KeyCode.VcBackspace }, { "Delete", KeyCode.VcDelete },
            { "Insert", KeyCode.VcInsert }, { "Home", KeyCode.VcHome }, { "End", KeyCode.VcEnd },
            { "PgUp", KeyCode.VcPageUp }, { "PageUp", KeyCode.VcPageUp },
            { "PgDn", KeyCode.VcPageDown }, { "PageDown", KeyCode.VcPageDown },
            { "Up", KeyCode.VcUp }, { "Down", KeyCode.VcDown }, { "Left", KeyCode.VcLeft },
            { "Right", KeyCode.VcRight },

            { "Minus", KeyCode.VcMinus }, { "-", KeyCode.VcMinus },
            { "Plus", KeyCode.VcEquals }, { "Equals", KeyCode.VcEquals }, { "=", KeyCode.VcEquals },
            { "Comma", KeyCode.VcComma }, { ",", KeyCode.VcComma },
            { "Period", KeyCode.VcPeriod }, { ".", KeyCode.VcPeriod },
            { "Slash", KeyCode.VcSlash }, { "/", KeyCode.VcSlash },
            { "Backslash", KeyCode.VcBackslash }, { "\\", KeyCode.VcBackslash },
            { "Semicolon", KeyCode.VcSemicolon }, { ";", KeyCode.VcSemicolon },
            { "Quote", KeyCode.VcQuote }, { "'", KeyCode.VcQuote },
            { "BackQuote", KeyCode.VcBackQuote }, { "`", KeyCode.VcBackQuote },
            { "OpenBracket", KeyCode.VcOpenBracket }, { "[", KeyCode.VcOpenBracket },
            { "CloseBracket", KeyCode.VcCloseBracket }, { "]", KeyCode.VcCloseBracket },

            { "OemOpenBrackets", KeyCode.VcOpenBracket }, { "OemCloseBrackets", KeyCode.VcCloseBracket },
            { "Oem1", KeyCode.VcSemicolon }, { "OemPlus", KeyCode.VcEquals }, { "OemComma", KeyCode.VcComma },
            { "OemMinus", KeyCode.VcMinus }, { "OemPeriod", KeyCode.VcPeriod }, { "Oem2", KeyCode.VcSlash },
            { "Oem3", KeyCode.VcBackQuote }, { "Oem4", KeyCode.VcOpenBracket }, { "Oem5", KeyCode.VcBackslash },
            { "Oem6", KeyCode.VcCloseBracket }, { "Oem7", KeyCode.VcQuote },

            { "CapsLock", KeyCode.VcCapsLock }, { "NumLock", KeyCode.VcNumLock },
            { "ScrollLock", KeyCode.VcScrollLock },
            { "PrintScreen", KeyCode.VcPrintScreen }, { "Pause", KeyCode.VcPause },

            { "F1", KeyCode.VcF1 }, { "F2", KeyCode.VcF2 }, { "F3", KeyCode.VcF3 }, { "F4", KeyCode.VcF4 },
            { "F5", KeyCode.VcF5 }, { "F6", KeyCode.VcF6 }, { "F7", KeyCode.VcF7 }, { "F8", KeyCode.VcF8 },
            { "F9", KeyCode.VcF9 }, { "F10", KeyCode.VcF10 }, { "F11", KeyCode.VcF11 }, { "F12", KeyCode.VcF12 },
            { "F13", KeyCode.VcF13 }, { "F14", KeyCode.VcF14 }, { "F15", KeyCode.VcF15 }, { "F16", KeyCode.VcF16 },
            { "F17", KeyCode.VcF17 }, { "F18", KeyCode.VcF18 }, { "F19", KeyCode.VcF19 }, { "F20", KeyCode.VcF20 },
            { "F21", KeyCode.VcF21 }, { "F22", KeyCode.VcF22 }, { "F23", KeyCode.VcF23 }, { "F24", KeyCode.VcF24 },

            { "NumPad0", KeyCode.VcNumPad0 }, { "NumPad1", KeyCode.VcNumPad1 }, { "NumPad2", KeyCode.VcNumPad2 },
            { "NumPad3", KeyCode.VcNumPad3 }, { "NumPad4", KeyCode.VcNumPad4 }, { "NumPad5", KeyCode.VcNumPad5 },
            { "NumPad6", KeyCode.VcNumPad6 }, { "NumPad7", KeyCode.VcNumPad7 }, { "NumPad8", KeyCode.VcNumPad8 },
            { "NumPad9", KeyCode.VcNumPad9 }, { "NumPadAdd", KeyCode.VcNumPadAdd },
            { "NumPadSubtract", KeyCode.VcNumPadSubtract },
            { "NumPadMultiply", KeyCode.VcNumPadMultiply }, { "NumPadDivide", KeyCode.VcNumPadDivide },
            { "NumPadDecimal", KeyCode.VcNumPadDecimal }, { "NumPadEnter", KeyCode.VcNumPadEnter },

            { "A", KeyCode.VcA }, { "B", KeyCode.VcB }, { "C", KeyCode.VcC }, { "D", KeyCode.VcD },
            { "E", KeyCode.VcE },
            { "F", KeyCode.VcF }, { "G", KeyCode.VcG }, { "H", KeyCode.VcH }, { "I", KeyCode.VcI },
            { "J", KeyCode.VcJ },
            { "K", KeyCode.VcK }, { "L", KeyCode.VcL }, { "M", KeyCode.VcM }, { "N", KeyCode.VcN },
            { "O", KeyCode.VcO },
            { "P", KeyCode.VcP }, { "Q", KeyCode.VcQ }, { "R", KeyCode.VcR }, { "S", KeyCode.VcS },
            { "T", KeyCode.VcT },
            { "U", KeyCode.VcU }, { "V", KeyCode.VcV }, { "W", KeyCode.VcW }, { "X", KeyCode.VcX },
            { "Y", KeyCode.VcY },
            { "Z", KeyCode.VcZ },
            { "0", KeyCode.Vc0 }, { "1", KeyCode.Vc1 }, { "2", KeyCode.Vc2 }, { "3", KeyCode.Vc3 },
            { "4", KeyCode.Vc4 },
            { "5", KeyCode.Vc5 }, { "6", KeyCode.Vc6 }, { "7", KeyCode.Vc7 }, { "8", KeyCode.Vc8 }, { "9", KeyCode.Vc9 }
        };

        foreach (var t in parts!)
        {
            var trimmedPart = t.Trim();
            if (specialKeys.TryGetValue(trimmedPart, out var foundKey))
            {
                if (trimmedPart is "Shift" or "Ctrl" or "Alt" or "Win" or "LeftShift" or "RightShift" or "LeftControl"
                    or "RightControl" or "LeftAlt" or "RightAlt" or "LeftWin" or "RightWin" or "Control")
                    modifiers.Add(foundKey);
                else
                    key = foundKey;
            }
            else
            {
                if (ushort.TryParse(trimmedPart, out var keyCodeValue) && Enum.IsDefined(typeof(KeyCode), keyCodeValue))
                {
                    key = (KeyCode)keyCodeValue;
                }
                else
                {
                    Console.WriteLine($"[ConvertToInputSimulatorSyntax] Unknown key: {trimmedPart}");
                    throw new ArgumentException($"Unknown key: {trimmedPart}");
                }
            }
        }

        if (key == KeyCode.Vc0)
        {
            Console.WriteLine("[ConvertToInputSimulatorSyntax] Invalid keybind: no key specified.");
            throw new ArgumentException("Invalid keybind: no key specified.");
        }

        keybinds.Add((modifiers, key));
        Console.WriteLine(
            $"[ConvertToInputSimulatorSyntax] Converted keybind: {keybind} to {string.Join(", ", keybinds.Select(k => $"{string.Join("+", k.Modifiers)}+{k.Key}"))}");
        return keybinds;
    }
}