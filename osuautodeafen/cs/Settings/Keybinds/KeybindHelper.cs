using Avalonia.Input;

namespace osuautodeafen.cs.Settings.Keybinds
{
    public static class KeybindHelper
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
    }
}