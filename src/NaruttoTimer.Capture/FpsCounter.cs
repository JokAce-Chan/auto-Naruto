namespace NaruttoTimer.Capture;

/// <summary>滑动窗口 FPS 计数。</summary>
public sealed class FpsCounter
{
    private readonly Queue<long> _frameTicks = new();
    private readonly object _lock = new();

    public void Push()
    {
        lock (_lock)
        {
            var now = Environment.TickCount64;
            _frameTicks.Enqueue(now);
            while (_frameTicks.Count > 0 && now - _frameTicks.Peek() > 1000)
                _frameTicks.Dequeue();
        }
    }

    /// <summary>最近 1 秒内帧数（即 FPS）。</summary>
    public double Current
    {
        get
        {
            lock (_lock)
            {
                var now = Environment.TickCount64;
                while (_frameTicks.Count > 0 && now - _frameTicks.Peek() > 1000)
                    _frameTicks.Dequeue();
                return _frameTicks.Count;
            }
        }
    }

    public void Reset()
    {
        lock (_lock) _frameTicks.Clear();
    }
}
