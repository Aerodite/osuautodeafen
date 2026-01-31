using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Input;
using osuautodeafen.cs.Settings;
using osuautodeafen.cs.Settings.Keybinds;
using osuautodeafen.cs.Tosu;
using osuautodeafen.cs.ViewModels;
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

    private readonly DateTime _startedAt = DateTime.Now;

    private DateTime _deafenEnteredAt = DateTime.MinValue;
    private bool _desiredDeafenState;
    private bool _hasAppliedFirstDeafen = false;
    private bool _isDeafened;
    private bool _isInBreakPeriod;
    private DateTime _lastToggleAt = DateTime.MinValue;

    private DateTime _nextStateChangedAt = DateTime.MinValue;
    public bool Deafened;

    public Deafen(TosuApi tosuAPI, SettingsHandler settingsHandler, SharedViewModel sharedViewModel)
    {
        Deafened = false;
        _tosuAPI = tosuAPI;
        _hook = new SimpleGlobalHook();
        _sharedViewModel = sharedViewModel;
        _settingsHandler = settingsHandler;

        _timer = new Timer(64);
        _timer.Elapsed += (_, _) => EvaluateDeafenState();
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

    // this is kinda just for me (and anyone else on hyprland not using the official discord client)
    private bool IsHyprland()
    {
        return Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE") != null;
    }

    private bool IsWayland()
    {
        return Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null;
    }

    private bool TryHyprlandSendShortcut()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_settingsHandler.DiscordClient))
                return false;

            ProcessStartInfo psi = new()
            {
                FileName = "hyprctl",
                Arguments = "dispatch sendshortcut CTRL+SHIFT,D,class:" + _settingsHandler.DiscordClient,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(psi);
            if (process == null)
                return false;

            process.WaitForExit(600);

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hyprland] sendshortcut failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Attempts to simulate the press(ing) of keybinds to deafen/undeafen in Discord.
    /// </summary>
    private void SimulateDeafenKey()
    {
        try
        {
            var osuKeybinds = _tosuAPI.GetOsuKeybinds();
            var keys = osuKeybinds as Key[] ?? osuKeybinds.ToArray();

            Key configuredKey = (Key)_settingsHandler.DeafenKeybindKey;

            bool hasAnyModifier =
                _settingsHandler.DeafenKeybindControlSide != 0 ||
                _settingsHandler.DeafenKeybindAltSide != 0 ||
                _settingsHandler.DeafenKeybindShiftSide != 0;

            if (!hasAnyModifier && keys.Contains(configuredKey))
            {
                Console.WriteLine("Nice try.");
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

    private bool ComputeDeafenState()
    {
        bool isPlaying = _tosuAPI.GetRawBanchoStatus() == 2;
        bool isSpectating = _tosuAPI.GetRawBanchoStatus() == 6;
        bool hasHitObjects = _tosuAPI.GetMaxPlayCombo() != 0;

        if (isSpectating)
            return false;
        
        // prevents deafening on like first tick (just makes sure you've actually hit atleast 1 circle)
        if (!isPlaying || !hasHitObjects)
            return false;

        // undeafen if paused and the toggle is enabled
        if (_sharedViewModel.IsPauseUndeafenToggleEnabled && _tosuAPI.IsPaused())
            return false;

        // undeafen if in break period and the toggle is enabled
        _isInBreakPeriod = _tosuAPI.IsBreakPeriod();
        if (IsBreakUndeafenToggleEnabled && _isInBreakPeriod)
            return false;

        // fc logic or something
        if (_sharedViewModel.IsFCRequired && !_tosuAPI.IsFullCombo())
            if (IsUndeafenAfterMissEnabled)
                return false;

        // the main conditions
        double completion = Math.Round(_tosuAPI.GetCompletionPercentage(), 2);
        if (completion < _sharedViewModel.MinCompletionPercentage)
            return false;

        if (_tosuAPI.GetFullSR() < _sharedViewModel.StarRating)
            return false;

        if (_tosuAPI.GetMaxPP() < _sharedViewModel.PerformancePoints)
            return false;

        return true;
    }

    /// <summary>
    ///     Checks the conditions to deafen or undeafen and toggles the deafen state accordingly.
    /// </summary>
    private void EvaluateDeafenState()
    {
        // bunch of annoying stuff that allows spectating to not be annoying
        if ((DateTime.Now - _startedAt).TotalMilliseconds < 1000)
            return;

        bool isSpectating = _tosuAPI.GetRawBanchoStatus() == 6;
        
        if (!_hasAppliedFirstDeafen)
        {
            _hasAppliedFirstDeafen = true;
            _desiredDeafenState = false;
            return;
        }
        
        if (isSpectating)
        {
            _desiredDeafenState = false;

            if (_isDeafened)
            {
                Console.WriteLine("Undeafening due to spectating");
                ApplyDeafenToggle();
            }

            return;
        }
        
        // real deafen shi

        bool nextState = ComputeDeafenState();

        if (nextState != _desiredDeafenState)
        {
            if (_isDeafened && !nextState)
                if ((DateTime.Now - _deafenEnteredAt).TotalMilliseconds < 500)
                {
                    return;
                }

            _desiredDeafenState = nextState;
            _nextStateChangedAt = DateTime.Now;
            return;
        }

        if ((DateTime.Now - _nextStateChangedAt).TotalMilliseconds < 250)
            return;

        if (nextState == _isDeafened)
            return;

        Console.WriteLine(
            nextState
                ? "[Deafen] deafening"
                : "[Deafen] undeafening"
        );

        ApplyDeafenToggle();
    }

    /// <summary>
    ///     Toggles the deafen state by simulating the deafen key press.
    /// </summary>
    private void ApplyDeafenToggle()
    {
        lock (_deafenLock)
        {
            if ((DateTime.Now - _lastToggleAt).TotalMilliseconds < 50)
                return;

            _lastToggleAt = DateTime.Now;

            if (IsWayland() && IsHyprland() && !string.IsNullOrWhiteSpace(_settingsHandler.DiscordClient))
                if (TryHyprlandSendShortcut())
                {
                    _isDeafened = !_isDeafened;

                    if (_isDeafened)
                        _deafenEnteredAt = DateTime.Now;

                    return;
                }

            SimulateDeafenKey();
            _isDeafened = !_isDeafened;

            if (_isDeafened)
                _deafenEnteredAt = DateTime.Now;
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

    // just in case its ever needed
    // ReSharper disable once UnusedMember.Local
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