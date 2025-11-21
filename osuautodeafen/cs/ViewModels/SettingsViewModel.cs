namespace osuautodeafen.cs.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private string _deafenKeybindDisplay;
        public string DeafenKeybindDisplay
        {
            get => _deafenKeybindDisplay;
            set
            {
                if (_deafenKeybindDisplay != value)
                {
                    _deafenKeybindDisplay = value;
                    OnPropertyChanged();
                }
            }
        }
        
        private string _keybindPrompt = "Press any key(s) for the keybind...";
        public string KeybindPrompt
        {
            get => _keybindPrompt;
            set
            {
                _keybindPrompt = value;
                OnPropertyChanged();
            }
        }
    }
}