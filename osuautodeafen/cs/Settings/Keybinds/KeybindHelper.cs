using Avalonia.Input;

namespace osuautodeafen.cs.Settings.Keybinds;

public class KeybindHelper
{
    public static bool ShouldIgnoreKeyForKeybind(Key key)
    {
        return key == Key.NumLock || IsModifierKey(key);
    }

    private static bool IsModifierKey(Key key)
    {
        return key == Key.LeftCtrl || key == Key.RightCtrl ||
               key == Key.LeftAlt || key == Key.RightAlt ||
               key == Key.LeftShift || key == Key.RightShift ||
               key == Key.LWin || key == Key.RWin;
    }

    /// <summary>
    ///     Converts certain keys to more user-friendly names for display purposes
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public string GetFriendlyKeyName(Key key)
    {
        return key switch
        {
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemMinus => "-",
            Key.OemPlus => "+",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemBackslash => "\\",
            Key.OemPipe => "|",
            Key.OemTilde => "`",
            Key.Oem8 => "`",
            _ => key.ToString()
        };
    }
}