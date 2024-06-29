using System;
using System.Threading;
using AutoHotkey.Interop;

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
        private const double MinCompletionPercentage = 75;

        public Deafen(TosuAPI tosuAPI)
        {
            _tosuAPI = tosuAPI;
            _ahk = AutoHotkeyEngine.Instance;
            _timer = new Timer(TimerElapsed, null, 0, 250);
            _tosuAPI.StateChanged += (state) => _isPlaying = (state == 2);
        }

        private void TimerElapsed(object state)
        {
            double completionPercentage = _tosuAPI.GetCompletionPercentage();
            Console.WriteLine($"Completion percentage: {Math.Round(completionPercentage, 2)}%");

            if (_isPlaying && !_hasReachedMinPercent && completionPercentage >= MinCompletionPercentage)
            {
                ToggleDeafenState();
            }
            else if (!_isPlaying && _hasReachedMinPercent && completionPercentage >= 100)
            {
                ToggleDeafenState();
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
        }
    }
}