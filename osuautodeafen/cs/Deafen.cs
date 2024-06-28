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
        public bool _isPlaying = false;
        private bool _hasReachedMinPercent = false;
        private bool _hasReached100Percent = false;
        private double _minCompletionPercentage = 0;

        public void UpdateMinCompletionPercentage(double newPercentage)
        {
            _minCompletionPercentage = newPercentage;
        }

        public Deafen(TosuAPI tosuAPI)
        {
            _tosuAPI = tosuAPI;
            _ahk = AutoHotkeyEngine.Instance;
            _timer = new Timer(TimerElapsed, null, 0, 250);

            _tosuAPI.StateChanged += TosuAPI_StateChanged;
        }

        public void TosuAPI_StateChanged(int state)
        {
            //Tosu sets the playing state to 2, and that can be used to determine whether to deafen or not
            _isPlaying = (state == 2);
        }

        private void TimerElapsed(object state)
        {
            double completionPercentage = _tosuAPI.GetCompletionPercentage();

            if(completionPercentage >= 100)
            {
                _hasReached100Percent = true;
            }

            if (_isPlaying)
            {
                Console.WriteLine($"Completion percentage: {Math.Round(completionPercentage) + "%"}");

                if (!_hasReachedMinPercent && completionPercentage >= _minCompletionPercentage)
                {
                    _ahk.ExecRaw("Send, ^p");
                    Console.WriteLine("Sent Ctrl + P (deafen)");
                    _hasReachedMinPercent = true;
                }

                if ((_hasReachedMinPercent || _hasReached100Percent) && !_isPlaying)
                {
                    _ahk.ExecRaw("Send, ^p");
                    Console.WriteLine("Sent Ctrl + P (undeafen)");
                    _hasReachedMinPercent = false;
                    _hasReached100Percent = false;
                }
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}