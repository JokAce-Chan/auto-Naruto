namespace NaruttoTimer.Rules;

/// <summary>
/// 规则引擎（源 domain/engine/Engine + service/ScreenCaptureService）。
/// 输入：每侧识别值（0~4）。判定：稳定 3 帧后若「值恰好减 1」→ 该侧使用技能，
/// 倒计时从 <see cref="CountdownSeconds"/>(14.5s) 重新起算；归零后保持显示 0.00。
/// </summary>
public sealed class EnergyRuleEngine
{
    private readonly EnergyStableProcessor _leftProcessor;
    private readonly EnergyStableProcessor _rightProcessor;
    private readonly Func<DateTime> _clock;
    private DateTime? _leftUsedAt;
    private DateTime? _rightUsedAt;

    public EnergyRuleEngine(double countdownSeconds = 14.5, int stableFrames = 3, Func<DateTime>? clock = null)
    {
        CountdownSeconds = countdownSeconds;
        _leftProcessor = new EnergyStableProcessor(stableFrames);
        _rightProcessor = new EnergyStableProcessor(stableFrames);
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>替换身冷却时长（秒）。源项目 cdSeconds = 14.5。</summary>
    public double CountdownSeconds { get; set; }

    /// <summary>最近一次传入的左/右识别值。</summary>
    public int LeftValue { get; private set; }
    public int RightValue { get; private set; }

    /// <summary>左/右最近一次确认的稳定值。</summary>
    public int LeftStableValue => _leftProcessor.StableValue;
    public int RightStableValue => _rightProcessor.StableValue;

    public event EventHandler<SkillUsedEventArgs>? SkillUsed;

    /// <summary>喂入单侧识别值；返回本次是否判定为使用技能。</summary>
    public bool Update(Side side, int value)
    {
        value = Math.Clamp(value, 0, EnergyRules.MaxValue);
        bool used;
        if (side == Side.Left)
        {
            LeftValue = value;
            used = _leftProcessor.IsUseSkill(value);
        }
        else
        {
            RightValue = value;
            used = _rightProcessor.IsUseSkill(value);
        }

        if (used)
        {
            var now = _clock();
            if (side == Side.Left) _leftUsedAt = now;
            else _rightUsedAt = now;
            SkillUsed?.Invoke(this, new SkillUsedEventArgs
            {
                Side = side,
                OldValue = value + 1,
                NewValue = value,
                Timestamp = now,
            });
        }
        return used;
    }

    /// <summary>按当前时间计算左右倒计时（未使用过则显示 0.00）。</summary>
    public CountdownSnapshot GetSnapshot()
    {
        var now = _clock();
        return new CountdownSnapshot(Remaining(_leftUsedAt, now), Remaining(_rightUsedAt, now));
    }

    private double Remaining(DateTime? usedAt, DateTime now)
    {
        if (usedAt == null) return 0;
        double seconds = CountdownSeconds - (now - usedAt.Value).TotalSeconds;
        return seconds <= 0 ? 0 : seconds;
    }

    public void Reset()
    {
        _leftProcessor.Reset();
        _rightProcessor.Reset();
        _leftUsedAt = null;
        _rightUsedAt = null;
        LeftValue = 0;
        RightValue = 0;
    }
}
