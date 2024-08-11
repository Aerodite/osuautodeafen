using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace osuautodeafen.cs.Screen
{
    public class ScreenBlankerWindow : Window
    {
        public string Name { get; }

        public ScreenBlankerWindow(string name, PixelRect bounds, double scaling)
        {
            Focusable = false;
            Name = name;
            Background = Brushes.Black;
            WindowState = WindowState.FullScreen;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)(bounds.X * scaling), (int)(bounds.Y * scaling));
            Width = bounds.Width * scaling;
            Height = bounds.Height * scaling;
            ShowInTaskbar = false;
            Topmost = true;
            CanResize = false;
            IsVisible = false;
            SystemDecorations = SystemDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Show(); // make sure the window is created

            Opacity = 0; // set to zero so it exists in the background
        }

        public sealed override void Hide()
        {
            base.Hide();
        }

        public sealed override void Show()
        {
            base.Show();
        }

        public void Blank()
        {
            Console.WriteLine($@"Blanking window: {Name}");
            this.Opacity = 1;
        }

        public void Unblank()
        {
            Console.WriteLine($@"Unblanking window: {Name}");
            this.Opacity = 0;
        }
    }
}