using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace osuautodeafen.Update;

public class UpdateNotificationBar : UserControl
{
    public UpdateNotificationBar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}