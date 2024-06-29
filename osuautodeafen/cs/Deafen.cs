using System;
using System.IO;
using System.Timers;
using AutoHotkey.Interop;
using Timer = System.Timers.Timer;
using System.Timers;

/*
 *
 * This logic can get kinda finnicky if you're moving around menu's alot
 * so maybe some more checks in the future would be nice
 *
 */

namespace osuautodeafen
{
    public class Deafen : IDisposable
    {
        private readonly TosuAPI _tosuAPI;
        private readonly AutoHotkeyEngine _ahk;
        private readonly Timer _timer;
        private bool _isPlaying = false;
        private bool _hasReachedMinPercent = false;
        private bool _deafened = false;
        private double MinCompletionPercentage;
        private Timer _fileCheckTimer;


        public Deafen(TosuAPI tosuAPI, SettingsPanel settingsPanel)
        {
            _tosuAPI = tosuAPI;
            _ahk = AutoHotkeyEngine.Instance;
            _timer = new System.Timers.Timer(250);
            _timer.Elapsed += TimerElapsed;
            _timer.AutoReset = true;
            _timer.Start();


            _tosuAPI.StateChanged += TosuAPI_StateChanged;

            _fileCheckTimer = new Timer(5000);
            _fileCheckTimer.Elapsed += FileCheckTimer_Elapsed;
            _fileCheckTimer.Start();

            string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "osuautodeafen", "settings.txt");
            if (File.Exists(settingsFilePath))
            {
                string text = File.ReadAllText(settingsFilePath);
                var settings = text.Split('=');
                if (settings.Length == 2 && settings[0].Trim() == "MinCompletionPercentage" && double.TryParse(settings[1], out double parsedPercentage))
                {
                    MinCompletionPercentage = parsedPercentage;
                }
            }
            else
            {
                MinCompletionPercentage = 75;
            }
        }

        private void FileCheckTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "osuautodeafen", "settings.txt");
            if (File.Exists(settingsFilePath))
            {
                string text = File.ReadAllText(settingsFilePath);
                var settings = text.Split('=');
                if (settings.Length == 2 && settings[0].Trim() == "MinCompletionPercentage" && double.TryParse(settings[1], out double parsedPercentage))
                {
                    MinCompletionPercentage = parsedPercentage;
                }
            }
            else
            {
                MinCompletionPercentage = 75;
            }
        }

        private void TosuAPI_StateChanged(int state)
        {
            _isPlaying = (state == 2);
        }

        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            var completionPercentage = _tosuAPI.GetCompletionPercentage();
            Console.WriteLine($"Completion percentage: {Math.Round(completionPercentage, 2)}%");

            if (_isPlaying && (!_hasReachedMinPercent && completionPercentage >= MinCompletionPercentage)){
                ToggleDeafenState();
                _hasReachedMinPercent = true;
                _deafened = true;
            }
            else if (!_isPlaying && (_hasReachedMinPercent && completionPercentage >= 100)) {
                ToggleDeafenState();
                _hasReachedMinPercent = false;
                _deafened = false;
            }

            else if (_deafened && !_isPlaying)
            {
                ToggleDeafenState();
                _hasReachedMinPercent = false;
                _deafened = false;
            }
        }

        private void ToggleDeafenState()
        {
            _ahk.ExecRaw("Send, ^p");
            Console.WriteLine(_hasReachedMinPercent ? "Sent Ctrl + P (undeafen)" : "Sent Ctrl + P (deafen)");
            _hasReachedMinPercent = !_hasReachedMinPercent;
        }

        public void Dispose()
        {
            _timer.Dispose();
            _fileCheckTimer.Dispose();
        }
    }
}