using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;

namespace osuautodeafen
{
    public partial class UpdateNotificationWindow : Window
    {


        public UpdateNotificationWindow() : this(string.Empty, string.Empty)
        {
        }

        public UpdateNotificationWindow(string latestVersion, string latestReleaseUrl)
        {
            InitializeComponent();
            SetupWindow(latestVersion, latestReleaseUrl);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void SetupWindow(string latestVersion, string latestReleaseUrl)
        {
            var textBlock = this.FindControl<TextBlock>("TextBlock");
            textBlock.Text = $"A new version of osu!autodeafen is available! ({latestVersion})";
            textBlock.FontSize = 16;
            textBlock.FontWeight = FontWeight.Bold;
            textBlock.TextWrapping = TextWrapping.Wrap;
            textBlock.MaxWidth = 300;
            textBlock.Margin = new Thickness(10, 10, 10, 0);

            var button = this.FindControl<Button>("Button");
            button.Content = "Download";
            button.Click += (sender, e) => OpenUrl(latestReleaseUrl);
            button.Margin = new Thickness(10, 10, 10, 10);

        }

        private void OpenUrl(string url)
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
    }
}