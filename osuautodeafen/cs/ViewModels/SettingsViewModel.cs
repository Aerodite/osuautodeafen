namespace osuautodeafen.cs.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private MainWindow.HotKey? _deafenKeybind;
    private string _deafenKeybindDisplay = "LCtrl + D";

    private bool _isKeybindCaptureFlyoutOpen;

    private string _keybindPrompt = "Press any key(s) for the keybind...";

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

    public string KeybindPrompt
    {
        get => _keybindPrompt;
        set
        {
            _keybindPrompt = value;
            OnPropertyChanged();
        }
    }

    private bool _isKeybindCaptureEnabled;

    public bool IsKeybindCaptureEnabled
    {
        get => _isKeybindCaptureEnabled;
        set
        {
            if (_isKeybindCaptureEnabled != value)
            {
                _isKeybindCaptureEnabled = value;
                OnPropertyChanged();
            }
        }
    }
}