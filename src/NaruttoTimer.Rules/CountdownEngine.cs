namespace NaruttoTimer.Rules;

/// <summary>实现 PRD §3.5 判定优先级的状态机，左右两侧完全独立并行。</summary>
public sealed class CountdownEngine : ICountdownEngine
{
    private sealed class SideState
    {
        public GridCount? Grid;
        public int? LastValue;
        public bool LoadingActive;
        public double Seconds;
        public SideStatus Status = SideStatus.Normal;
    }

    private readonly SideState[] _states = { new(), new() };
    private readonly object _lock = new();

    public CountdownEngine(double countdownSeconds = 15.0)
    {
        if (countdownSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(countdownSeconds), "倒计时时长必须大于 0");
        CountdownSeconds = countdownSeconds;
    }

    public double CountdownSeconds { get; }

    public event EventHandler<CountdownChangedEventArgs>? CountdownChanged;
    public event EventHandler<TriggerEvent>? Triggered;

    public double GetSeconds(Side side) => _states[(int)side].Seconds;
    public SideStatus GetStatus(Side side) => _states[(int)side].Status;

    public CountdownSnapshot GetSnapshot() => new(
        _states[0].Seconds, _states[1].Seconds, _states[0].Status, _states[1].Status);

    public void Update(Side side, RecognitionReading reading)
    {
        lock (_lock)
        {
            var st = _states[(int)side];

            // 加载中：不触发、不重置；颜色突变不计
            if (reading.InLoading)
            {
                if (!st.LoadingActive) st.LoadingActive = true;
                SetStatus(side, st, SideStatus.Loading);
                RaiseChanged(side, st);
                return;
            }

            // 加载结束（重新稳定识别）：该侧立即置 0.00，建立新基线，不触发
            if (st.LoadingActive)
            {
                st.LoadingActive = false;
                st.Seconds = 0;
                st.Grid = reading.GridCount;
                st.LastValue = reading.Value;
                SetStatus(side, st, SideStatus.Normal);
                RaiseChanged(side, st);
                return;
            }

            // 首次读数：建立基线，不触发
            if (st.Grid == null)
            {
                st.Grid = reading.GridCount;
                st.LastValue = reading.Value;
                RaiseChanged(side, st);
                return;
            }

            // 格数变化 4↔6：不触发；重置上一值；新值继承但不超新格数上限
            if (st.Grid != reading.GridCount)
            {
                st.Grid = reading.GridCount;
                st.LastValue = Math.Min(reading.Value, (int)reading.GridCount);
                RaiseChanged(side, st);
                return;
            }

            int? oldValue = st.LastValue;
            if (oldValue.HasValue && reading.Value == oldValue.Value - 1)
            {
                // 值恰好减 1（正常对局）：启动/重置 15.00s
                st.LastValue = reading.Value;
                st.Seconds = CountdownSeconds;
                SetStatus(side, st, SideStatus.Counting);
                Triggered?.Invoke(this, new TriggerEvent(DateTime.Now, side, reading.GridCount, oldValue.Value, reading.Value, "start"));
                RaiseChanged(side, st);
                return;
            }

            // 值增、减 ≥2、不变：不动作，进行中倒计时不打断
            st.LastValue = reading.Value;
            RaiseChanged(side, st);
        }
    }

    public void Tick(double deltaSeconds)
    {
        if (deltaSeconds <= 0) return;
        lock (_lock)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                var st = _states[i];
                // 加载中只“不触发不重置”，倒计时照常走秒
                if (st.Seconds <= 0) continue;
                st.Seconds -= deltaSeconds;
                if (st.Seconds <= 0)
                {
                    // 倒计时归零：保持显示 0.00
                    st.Seconds = 0;
                    if (st.Status == SideStatus.Counting) SetStatus((Side)i, st, SideStatus.Normal);
                }
                RaiseChanged((Side)i, st);
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                _states[i] = new SideState();
                RaiseChanged((Side)i, _states[i]);
            }
        }
    }

    private void SetStatus(Side side, SideState st, SideStatus status)
    {
        if (st.Status == status) return;
        st.Status = status;
    }

    private void RaiseChanged(Side side, SideState st)
    {
        CountdownChanged?.Invoke(this, new CountdownChangedEventArgs
        {
            Side = side,
            Seconds = st.Seconds,
            Status = st.Status,
        });
    }
}