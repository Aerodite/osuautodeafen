using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using osuautodeafen.cs;
using osuautodeafen.cs.Settings;
using SharpHook;
using SharpHook.Data;
using SharpHook.Native;
using Timer = System.Timers.Timer;

namespace osuautodeafen;

public class Deafen : IDisposable
{
    private readonly object _deafenLock = new();
    private readonly Timer _fileCheckTimer;
    private readonly SimpleGlobalHook _hook;
    private readonly ScreenBlankerForm _screenBlanker;
    private readonly Timer _timer;
    private readonly TosuApi _tosuAPI;
    private string _customKeybind;
    private BreakPeriodCalculator _breakPeriodCalculator;
    private bool _deafened;
    private bool _hasReachedMinPercent;
    private bool _isInBreakPeriod;

    public Deafen(TosuApi tosuAPI, SettingsHandler settingsHandler, BreakPeriodCalculator breakPeriodCalculator,
        SharedViewModel viewModel)
    {
        _tosuAPI = tosuAPI;
        _tosuAPI.StateChanged += TosuAPI_StateChanged;
        _breakPeriodCalculator = breakPeriodCalculator;
    }

    public void Dispose()
    {
        _hook.Dispose();
    }

    private async void TosuAPI_StateChanged(int state)
    {
        //_isPlaying = state == 2;
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