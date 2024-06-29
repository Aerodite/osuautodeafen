using System.ComponentModel;
using System.Runtime.CompilerServices;

public class SharedViewModel : INotifyPropertyChanged
{
    private int _minCompletionPercentage;
    public int MinCompletionPercentage
    {
        get { return _minCompletionPercentage; }
        set
        {
            if (_minCompletionPercentage != value)
            {
                _minCompletionPercentage = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}