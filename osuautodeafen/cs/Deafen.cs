using System;
using System.Threading;
using AutoHotkey.Interop;

namespace osuautodeafen
{
    public class Deafen : IDisposable
    {
        private readonly TosuAPI _tosuAPI;
        private readonly AutoHotkeyEngine _ahk;
        private readonly Timer _timer;
        private readonly SettingsPanel _settingsPanel;
        public bool _isPlaying = false;
        private bool _hasReachedMinPercent = false;
        private double _minCompletionPercentage = 75;

        public Deafen(TosuAPI tosuAPI, SettingsPanel settingsPanel)
        {
            _tosuAPI = tosuAPI;
            _ahk = AutoHotkeyEngine.Instance;
            _timer = new Timer(TimerElapsed, null, 0, 500);
            _settingsPanel = settingsPanel;

            _tosuAPI.StateChanged += TosuAPI_StateChanged;
        }

        public void TosuAPI_StateChanged(int state)
        {
            _isPlaying = (state == 2);
        }

        private void TimerElapsed(object state)
        {
            double completionPercentage = _tosuAPI.GetCompletionPercentage();

            if (_isPlaying)
            {
                Console.WriteLine($"Completion percentage: {Math.Round(completionPercentage) + "%"}");

                if (!_hasReachedMinPercent && completionPercentage >= _minCompletionPercentage)
                {
                    _ahk.ExecRaw("Send, ^p");
                    Console.WriteLine("Sent Ctrl + P (deafen)");
                    _hasReachedMinPercent = true;
                }
            }

            if (_hasReachedMinPercent && !_isPlaying)
            {
                _ahk.ExecRaw("Send, ^p");
                Console.WriteLine("Sent Ctrl + P (undeafen)");
                _hasReachedMinPercent = false;
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}