using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Input;
using osuautodeafen.cs.Settings;
using osuautodeafen.cs.Settings.Keybinds;
using osuautodeafen.cs.Settings.Presets;
using osuautodeafen.cs.Tosu;
using SharpHook;
using SharpHook.Data;
using Timer = System.Timers.Timer;

namespace osuautodeafen.cs.Deafen;

public class Deafen : IDisposable
{
    private readonly Lock _deafenLock = new();
    private readonly EventSimulator _eventSimulator = new();
    private readonly SimpleGlobalHook _hook;
    private readonly SettingsHandler _settingsHandler;
    private readonly SharedViewModel _sharedViewModel;
    private readonly Timer _timer;
    private readonly TosuApi _tosuAPI;
    public bool _deafened = false;
    private bool _isInBreakPeriod;

    public Action? Deafened;
    public Action? Undeafened;
    
    private DateTime _lastToggle = DateTime.MinValue;
    
    public Deafen(TosuApi tosuAPI, SettingsHandler settingsHandler, SharedViewModel sharedViewModel)
    {
        _deafened = false;
        _tosuAPI = tosuAPI;
        _hook = new SimpleGlobalHook();
        _sharedViewModel = sharedViewModel;
        _settingsHandler = settingsHandler;

        // handled seperately from the main timer due to importance
        _timer = new Timer(16);
        _timer.Elapsed += (_, _) => CheckAndDeafen();
        _timer.Start();
    }

    private bool IsUndeafenAfterMissEnabled => _sharedViewModel.UndeafenAfterMiss;
    private bool IsBreakUndeafenToggleEnabled => _sharedViewModel.IsBreakUndeafenToggleEnabled;

    /// <summary>
    ///     Cleans up resources used by the Deafen class
    /// </summary>
    public void Dispose()
    {
        Console.WriteLine("Disposing Deafen resources.");
        _hook.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Attempts to simulate the press(ing) of keybinds to deafen/undeafen in Discord.
    /// </summary>
    private void SimulateDeafenKey()
    {
        try
        {
            IEnumerable<Key> osuKeybinds = _tosuAPI.GetOsuKeybinds();
            Key[] keys = osuKeybinds as Key[] ?? osuKeybinds.ToArray();
            
            Key osuKey = (Key)_settingsHandler.DeafenKeybindKey;
            if (keys.Contains(osuKey))
            {
                Console.WriteLine($"Nice try.");
                return;
            }

            List<ModifierWithSide> modifiers = [];

            KeyCode key = MapAvaloniaKeyToSharpHook((Key)_settingsHandler.DeafenKeybindKey);

            int controlSide = _settingsHandler.DeafenKeybindControlSide;
            int altSide = _settingsHandler.DeafenKeybindAltSide;
            int shiftSide = _settingsHandler.DeafenKeybindShiftSide;

            Console.WriteLine(
                $"[SimulateDeafenKey] Retrieved keybind from settings: Key={key}, ControlSide={controlSide}, AltSide={altSide}, ShiftSide={shiftSide}");


            if (controlSide != 0)
                modifiers.Add(new ModifierWithSide
                {
                    Modifier = controlSide == 2 ? KeyCode.VcRightControl : KeyCode.VcLeftControl,
                    Side = controlSide == 2 ? Modifiers.ModifierSide.Right : Modifiers.ModifierSide.Left
                });
            if (altSide != 0)
                modifiers.Add(new ModifierWithSide
                {
                    Modifier = altSide == 2 ? KeyCode.VcRightAlt : KeyCode.VcLeftAlt,
                    Side = altSide == 2 ? Modifiers.ModifierSide.Right : Modifiers.ModifierSide.Left
                });
            if (shiftSide != 0)
                modifiers.Add(new ModifierWithSide
                {
                    Modifier = shiftSide == 2 ? KeyCode.VcRightShift : KeyCode.VcLeftShift,
                    Side = shiftSide == 2 ? Modifiers.ModifierSide.Right : Modifiers.ModifierSide.Left
                });


            Console.WriteLine(
                $"[SimulateDeafenKey] Pressing modifiers: {string.Join(", ", modifiers.Select(m => m.Modifier + (m.Side != Modifiers.ModifierSide.None ? $"({m.Side})" : "")))}");
            foreach (ModifierWithSide mod in modifiers) _eventSimulator.SimulateKeyPress(mod.Modifier);

            Console.WriteLine($"[SimulateDeafenKey] Pressing main key: {key}");
            _eventSimulator.SimulateKeyPress(key);

            _eventSimulator.SimulateKeyRelease(key);
            Console.WriteLine($"[SimulateDeafenKey] Released main key: {key}");

            foreach (ModifierWithSide mod in modifiers.AsEnumerable().Reverse())
                _eventSimulator.SimulateKeyRelease(mod.Modifier);
            Console.WriteLine(
                $"[SimulateDeafenKey] Released modifiers: {string.Join(", ", modifiers.AsEnumerable().Reverse().Select(m => m.Modifier + (m.Side != Modifiers.ModifierSide.None ? $"({m.Side})" : "")))}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SimulateDeafenKey] Exception: {ex}");
        }
    }

    /// <summary>
    ///     Determines whether the conditions to deafen are met.
    /// </summary>
    /// <returns>
    ///     True if the conditions to deafen are met, otherwise, false.
    /// </returns>
    private bool ShouldDeafen()
    {
        double completionPercentage = Math.Round(_tosuAPI.GetCompletionPercentage(), 2);
        double currentStarRating = _tosuAPI.GetFullSR();
        double currentPerformancePoints = _tosuAPI.GetMaxPP();
        double requiredCompletion = _sharedViewModel.MinCompletionPercentage;
        double requiredStarRating = _sharedViewModel.StarRating;
        double requiredPerformancePoints = _sharedViewModel.PerformancePoints;
        bool isFCRequired = _sharedViewModel.IsFCRequired;
        bool isPlaying = _tosuAPI.GetRawBanchoStatus() == 2;
        bool notAlreadyDeafened = !_deafened;
        // 100% will basically act as the deafening part disabled
        bool completionMet = requiredCompletion < 100 && completionPercentage >= requiredCompletion;
        bool starRatingMet = currentStarRating >= requiredStarRating;
        bool performancePointsMet = currentPerformancePoints >= requiredPerformancePoints;
        bool hasHitObjects = _tosuAPI.GetMaxCombo() != 0;
        bool notPracticeDifficulty = (int)_tosuAPI.GetRankedStatus() != 1;
        bool isFullCombo = _tosuAPI.IsFullCombo();
        _isInBreakPeriod = _tosuAPI.IsBreakPeriod();

        bool fcRequirementMet = !isFCRequired || isFullCombo;
        bool breakConditionMet = !IsBreakUndeafenToggleEnabled || !_isInBreakPeriod;
        bool missConditionMet = !IsUndeafenAfterMissEnabled || isFullCombo;
        
        // i prefer this condition boolean compared to that nightmare return statement we had before personally
        bool[] conditions = [
            isPlaying,
            notAlreadyDeafened,
            completionMet,
            starRatingMet,
            performancePointsMet,
            hasHitObjects,
            notPracticeDifficulty,
            fcRequirementMet,
            breakConditionMet,
            missConditionMet
        ];
        
        return conditions.All(x => x);
    }

    /// <summary>
    ///     Determines whether the conditions to undeafen are met.
    /// </summary>
    /// <returns>
    ///     True if the conditions to undeafen are met, otherwise, false.
    /// </returns>
    private bool ShouldUndeafen()
    {
        double completionPercentage = Math.Round(_tosuAPI.GetCompletionPercentage(), 2);
        bool isPlaying = _tosuAPI.GetRawBanchoStatus() == 2;
        bool isFullCombo = _tosuAPI.IsFullCombo();
        bool isFCRequired = _sharedViewModel.IsFCRequired;
        
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

    /// <summary>
    ///     Checks the conditions to deafen or undeafen and toggles the deafen state accordingly.
    /// </summary>
    private void CheckAndDeafen()
    {

        //this should seal any edge debounce cases, hopefully.
        bool hasHitObjects = _tosuAPI.GetMaxPlayCombo() != 0;
         /*
         so tldr basically without that boolean, if you start a map and the preview time is further than your deafen point,
         then you would be deafened then immediately undeafened.
         this just prevents ANYTHING from happening until at least one circle is hit.
         */
        

        if (ShouldDeafen() && !_deafened && hasHitObjects)
        {
            Console.WriteLine("[CheckAndDeafen] Should deafen. Toggling deafen state.");
            ToggleDeafenState();
        }
        else if (ShouldUndeafen() && _deafened)
        {
            Console.WriteLine("[CheckAndDeafen] Should undeafen. Toggling deafen state.");
            ToggleDeafenState();
        }
    }

    /// <summary>
    ///     Toggles the deafen state by simulating the deafen key press.
    /// </summary>
    private void ToggleDeafenState()
    {
        lock (_deafenLock)
        {
            if ((DateTime.Now - _lastToggle).TotalMilliseconds < 50) return; 
            _lastToggle = DateTime.Now;
            SimulateDeafenKey();
            _deafened = !_deafened;
        }
    }

    private KeyCode MapAvaloniaKeyToSharpHook(Key avaloniaKey)
    {
        return avaloniaKey switch
        {
            Key.LeftCtrl => KeyCode.VcLeftControl,
            Key.RightCtrl => KeyCode.VcRightControl,
            Key.LeftAlt => KeyCode.VcLeftAlt,
            Key.RightAlt => KeyCode.VcRightAlt,
            Key.LeftShift => KeyCode.VcLeftShift,
            Key.RightShift => KeyCode.VcRightShift,

            Key.Tab => KeyCode.VcTab,
            Key.Enter => KeyCode.VcEnter,
            Key.Space => KeyCode.VcSpace,
            Key.Back => KeyCode.VcBackspace,
            Key.Delete => KeyCode.VcDelete,
            Key.Insert => KeyCode.VcInsert,
            Key.Home => KeyCode.VcHome,
            Key.End => KeyCode.VcEnd,
            Key.PageUp => KeyCode.VcPageUp,
            Key.PageDown => KeyCode.VcPageDown,
            Key.Up => KeyCode.VcUp,
            Key.Down => KeyCode.VcDown,
            Key.Left => KeyCode.VcLeft,
            Key.Right => KeyCode.VcRight,
            Key.Escape => KeyCode.VcEscape,

            Key.OemMinus => KeyCode.VcMinus,
            Key.OemPlus => KeyCode.VcEquals,
            Key.OemComma => KeyCode.VcComma,
            Key.OemPeriod => KeyCode.VcPeriod,
            Key.Oem2 => KeyCode.VcSlash,
            Key.Oem5 => KeyCode.VcBackslash,
            Key.Oem1 => KeyCode.VcSemicolon,
            Key.Oem7 => KeyCode.VcQuote,
            Key.Oem3 => KeyCode.VcBackQuote,
            Key.Oem4 => KeyCode.VcOpenBracket,
            Key.Oem6 => KeyCode.VcCloseBracket,

            Key.CapsLock => KeyCode.VcCapsLock,
            Key.NumLock => KeyCode.VcNumLock,
            Key.Scroll => KeyCode.VcScrollLock,

            Key.PrintScreen => KeyCode.VcPrintScreen,
            Key.Pause => KeyCode.VcPause,

            Key.F1 => KeyCode.VcF1,
            Key.F2 => KeyCode.VcF2,
            Key.F3 => KeyCode.VcF3,
            Key.F4 => KeyCode.VcF4,
            Key.F5 => KeyCode.VcF5,
            Key.F6 => KeyCode.VcF6,
            Key.F7 => KeyCode.VcF7,
            Key.F8 => KeyCode.VcF8,
            Key.F9 => KeyCode.VcF9,
            Key.F10 => KeyCode.VcF10,
            Key.F11 => KeyCode.VcF11,
            Key.F12 => KeyCode.VcF12,
            Key.F13 => KeyCode.VcF13,
            Key.F14 => KeyCode.VcF14,
            Key.F15 => KeyCode.VcF15,
            Key.F16 => KeyCode.VcF16,
            Key.F17 => KeyCode.VcF17,
            Key.F18 => KeyCode.VcF18,
            Key.F19 => KeyCode.VcF19,
            Key.F20 => KeyCode.VcF20,
            Key.F21 => KeyCode.VcF21,
            Key.F22 => KeyCode.VcF22,
            Key.F23 => KeyCode.VcF23,
            Key.F24 => KeyCode.VcF24,

            Key.NumPad0 => KeyCode.VcNumPad0,
            Key.NumPad1 => KeyCode.VcNumPad1,
            Key.NumPad2 => KeyCode.VcNumPad2,
            Key.NumPad3 => KeyCode.VcNumPad3,
            Key.NumPad4 => KeyCode.VcNumPad4,
            Key.NumPad5 => KeyCode.VcNumPad5,
            Key.NumPad6 => KeyCode.VcNumPad6,
            Key.NumPad7 => KeyCode.VcNumPad7,
            Key.NumPad8 => KeyCode.VcNumPad8,
            Key.NumPad9 => KeyCode.VcNumPad9,
            Key.Add => KeyCode.VcNumPadAdd,
            Key.Subtract => KeyCode.VcNumPadSubtract,
            Key.Multiply => KeyCode.VcNumPadMultiply,
            Key.Divide => KeyCode.VcNumPadDivide,
            Key.Decimal => KeyCode.VcNumPadDecimal,

            Key.A => KeyCode.VcA,
            Key.B => KeyCode.VcB,
            Key.C => KeyCode.VcC,
            Key.D => KeyCode.VcD,
            Key.E => KeyCode.VcE,
            Key.F => KeyCode.VcF,
            Key.G => KeyCode.VcG,
            Key.H => KeyCode.VcH,
            Key.I => KeyCode.VcI,
            Key.J => KeyCode.VcJ,
            Key.K => KeyCode.VcK,
            Key.L => KeyCode.VcL,
            Key.M => KeyCode.VcM,
            Key.N => KeyCode.VcN,
            Key.O => KeyCode.VcO,
            Key.P => KeyCode.VcP,
            Key.Q => KeyCode.VcQ,
            Key.R => KeyCode.VcR,
            Key.S => KeyCode.VcS,
            Key.T => KeyCode.VcT,
            Key.U => KeyCode.VcU,
            Key.V => KeyCode.VcV,
            Key.W => KeyCode.VcW,
            Key.X => KeyCode.VcX,
            Key.Y => KeyCode.VcY,
            Key.Z => KeyCode.VcZ,

            Key.D0 => KeyCode.Vc0,
            Key.D1 => KeyCode.Vc1,
            Key.D2 => KeyCode.Vc2,
            Key.D3 => KeyCode.Vc3,
            Key.D4 => KeyCode.Vc4,
            Key.D5 => KeyCode.Vc5,
            Key.D6 => KeyCode.Vc6,
            Key.D7 => KeyCode.Vc7,
            Key.D8 => KeyCode.Vc8,
            Key.D9 => KeyCode.Vc9,

            _ => KeyCode.VcUndefined
        };
    }
    
    private Key MapSharpHookKeyToAvaloniaKey(KeyCode keyCode)
    {
        return keyCode switch
        {
            KeyCode.VcLeftControl => Key.LeftCtrl,
            KeyCode.VcRightControl => Key.RightCtrl,
            KeyCode.VcLeftAlt => Key.LeftAlt,
            KeyCode.VcRightAlt => Key.RightAlt,
            KeyCode.VcLeftShift => Key.LeftShift,
            KeyCode.VcRightShift => Key.RightShift,

            KeyCode.VcTab => Key.Tab,
            KeyCode.VcEnter => Key.Enter,
            KeyCode.VcSpace => Key.Space,
            KeyCode.VcBackspace => Key.Back,
            KeyCode.VcDelete => Key.Delete,
            KeyCode.VcInsert => Key.Insert,
            KeyCode.VcHome => Key.Home,
            KeyCode.VcEnd => Key.End,
            KeyCode.VcPageUp => Key.PageUp,
            KeyCode.VcPageDown => Key.PageDown,
            KeyCode.VcUp => Key.Up,
            KeyCode.VcDown => Key.Down,
            KeyCode.VcLeft => Key.Left,
            KeyCode.VcRight => Key.Right,
            KeyCode.VcEscape => Key.Escape,

            KeyCode.VcMinus => Key.OemMinus,
            KeyCode.VcEquals => Key.OemPlus,
            KeyCode.VcComma => Key.OemComma,
            KeyCode.VcPeriod => Key.OemPeriod,
            KeyCode.VcSlash => Key.Oem2,
            KeyCode.VcBackslash => Key.Oem5,
            KeyCode.VcSemicolon => Key.Oem1,
            KeyCode.VcQuote => Key.Oem7,
            KeyCode.VcBackQuote => Key.Oem3,
            KeyCode.VcOpenBracket => Key.Oem4,
            KeyCode.VcCloseBracket => Key.Oem6,

            KeyCode.VcCapsLock => Key.CapsLock,
            KeyCode.VcNumLock => Key.NumLock,
            KeyCode.VcScrollLock => Key.Scroll,
            KeyCode.VcPrintScreen => Key.PrintScreen,
            KeyCode.VcPause => Key.Pause,

            KeyCode.VcF1 => Key.F1,
            KeyCode.VcF2 => Key.F2,
            KeyCode.VcF3 => Key.F3,
            KeyCode.VcF4 => Key.F4,
            KeyCode.VcF5 => Key.F5,
            KeyCode.VcF6 => Key.F6,
            KeyCode.VcF7 => Key.F7,
            KeyCode.VcF8 => Key.F8,
            KeyCode.VcF9 => Key.F9,
            KeyCode.VcF10 => Key.F10,
            KeyCode.VcF11 => Key.F11,
            KeyCode.VcF12 => Key.F12,
            KeyCode.VcF13 => Key.F13,
            KeyCode.VcF14 => Key.F14,
            KeyCode.VcF15 => Key.F15,
            KeyCode.VcF16 => Key.F16,
            KeyCode.VcF17 => Key.F17,
            KeyCode.VcF18 => Key.F18,
            KeyCode.VcF19 => Key.F19,
            KeyCode.VcF20 => Key.F20,
            KeyCode.VcF21 => Key.F21,
            KeyCode.VcF22 => Key.F22,
            KeyCode.VcF23 => Key.F23,
            KeyCode.VcF24 => Key.F24,

            KeyCode.VcNumPad0 => Key.NumPad0,
            KeyCode.VcNumPad1 => Key.NumPad1,
            KeyCode.VcNumPad2 => Key.NumPad2,
            KeyCode.VcNumPad3 => Key.NumPad3,
            KeyCode.VcNumPad4 => Key.NumPad4,
            KeyCode.VcNumPad5 => Key.NumPad5,
            KeyCode.VcNumPad6 => Key.NumPad6,
            KeyCode.VcNumPad7 => Key.NumPad7,
            KeyCode.VcNumPad8 => Key.NumPad8,
            KeyCode.VcNumPad9 => Key.NumPad9,
            KeyCode.VcNumPadAdd => Key.Add,
            KeyCode.VcNumPadSubtract => Key.Subtract,
            KeyCode.VcNumPadMultiply => Key.Multiply,
            KeyCode.VcNumPadDivide => Key.Divide,
            KeyCode.VcNumPadDecimal => Key.Decimal,

            KeyCode.VcA => Key.A,
            KeyCode.VcB => Key.B,
            KeyCode.VcC => Key.C,
            KeyCode.VcD => Key.D,
            KeyCode.VcE => Key.E,
            KeyCode.VcF => Key.F,
            KeyCode.VcG => Key.G,
            KeyCode.VcH => Key.H,
            KeyCode.VcI => Key.I,
            KeyCode.VcJ => Key.J,
            KeyCode.VcK => Key.K,
            KeyCode.VcL => Key.L,
            KeyCode.VcM => Key.M,
            KeyCode.VcN => Key.N,
            KeyCode.VcO => Key.O,
            KeyCode.VcP => Key.P,
            KeyCode.VcQ => Key.Q,
            KeyCode.VcR => Key.R,
            KeyCode.VcS => Key.S,
            KeyCode.VcT => Key.T,
            KeyCode.VcU => Key.U,
            KeyCode.VcV => Key.V,
            KeyCode.VcW => Key.W,
            KeyCode.VcX => Key.X,
            KeyCode.VcY => Key.Y,
            KeyCode.VcZ => Key.Z,

            KeyCode.Vc0 => Key.D0,
            KeyCode.Vc1 => Key.D1,
            KeyCode.Vc2 => Key.D2,
            KeyCode.Vc3 => Key.D3,
            KeyCode.Vc4 => Key.D4,
            KeyCode.Vc5 => Key.D5,
            KeyCode.Vc6 => Key.D6,
            KeyCode.Vc7 => Key.D7,
            KeyCode.Vc8 => Key.D8,
            KeyCode.Vc9 => Key.D9,

            _ => Key.None
        };
    }


    private struct ModifierWithSide
    {
        public KeyCode Modifier;
        public Modifiers.ModifierSide Side;
    }
}