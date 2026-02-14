namespace TEngine
{
    public sealed partial class Debugger
    {
        private sealed class FpsCounter
        {
            private float _updateInterval;
            private float _currentFps;
            private int _frames;
            private float _accumulator;
            private float _timeLeft;

            public FpsCounter(float updateInterval)
            {
                if (updateInterval <= 0f)
                {
                    Log.Error("Update interval is invalid.");
                    return;
                }

                _updateInterval = updateInterval;
                Reset();
            }

            public float UpdateInterval
            {
                get => _updateInterval;
                set
                {
                    if (value <= 0f)
                    {
                        Log.Error("Update interval is invalid.");
                        return;
                    }

                    _updateInterval = value;
                    Reset();
                }
            }

            public float CurrentFps => _currentFps;

            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                _frames++;
                _accumulator += realElapseSeconds;
                _timeLeft -= realElapseSeconds;

                if (_timeLeft <= 0f)
                {
                    _currentFps = _accumulator > 0f ? _frames / _accumulator : 0f;
                    _frames = 0;
                    _accumulator = 0f;
                    _timeLeft += _updateInterval;
                }
            }

            private void Reset()
            {
                _currentFps = 0f;
                _frames = 0;
                _accumulator = 0f;
                _timeLeft = 0f;
            }
        }
    }
}
