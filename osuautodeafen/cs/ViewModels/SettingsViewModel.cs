namespace osuautodeafen.cs.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private string _deafenKeybindDisplay = "LCtrl + D";
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
        
        private bool _isKeybindCaptureFlyoutOpen;
        public bool IsKeybindCaptureFlyoutOpen
        {
            get => _isKeybindCaptureFlyoutOpen;
            set
            {
                if (_isKeybindCaptureFlyoutOpen != value)
                {
                    _isKeybindCaptureFlyoutOpen = value;
                    OnPropertyChanged();
                }
            }
        }
        
        private MainWindow.HotKey? _deafenKeybind;
        public MainWindow.HotKey? DeafenKeybind
        {
            get => _deafenKeybind;
            set
            {
                if (_deafenKeybind != value)
                {
                    _deafenKeybind = value;
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