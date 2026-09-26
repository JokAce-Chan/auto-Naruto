namespace NaruttoTimer.Rules;

/// <summary>
/// 规则引擎（源 domain/engine/Engine + service/ScreenCaptureService）。
/// 输入：每侧「值」与「空豆数」。判定：稳定 3 帧后按 <see cref="Judgement"/> 取输入——
/// 值判定看「值恰好减 1」，空豆判定看「空豆数恰好加 1」→ 该侧使用技能，
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

    /// <summary>判定方式：值判定（默认，源项目行为）/ 空豆判定（直接看空豆数变化）。</summary>
    public EnergyJudgement Judgement { get; set; } = EnergyJudgement.Value;

    /// <summary>最近一次传入的左/右识别值（值上限 − 空豆数，clamp 0~值上限）。</summary>
    public int LeftValue { get; private set; }
    public int RightValue { get; private set; }

    /// <summary>最近一次传入的左/右空豆数。</summary>
    public int LeftEmptyCount { get; private set; }
    public int RightEmptyCount { get; private set; }

    public event EventHandler<SkillUsedEventArgs>? SkillUsed;

    /// <summary>喂入单侧识别结果；返回本次是否判定为使用技能。</summary>
    /// <param name="value">值（值上限 − 空豆数）。</param>
    /// <param name="emptyCount">空豆数。</param>
    public bool Update(Side side, int value, int emptyCount)
    {
        value = Math.Clamp(value, 0, EnergyRules.MaxValue);
        emptyCount = Math.Max(0, emptyCount);
        // 空豆判定直接看空豆数（不做上限夹断）；值判定看值。
        bool emptyMode = Judgement == EnergyJudgement.EmptyCount;
        int input = emptyMode ? emptyCount : value;
        int step = emptyMode ? 1 : -1;

        bool used;
        if (side == Side.Left)
        {
            LeftValue = value;
            LeftEmptyCount = emptyCount;
            used = _leftProcessor.IsUseSkill(input, step);
        }
        else
        {
            RightValue = value;
            RightEmptyCount = emptyCount;
            used = _rightProcessor.IsUseSkill(input, step);
        }

        if (used)
        {
            var now = _clock();
            if (side == Side.Left) _leftUsedAt = now;
            else _rightUsedAt = now;
            SkillUsed?.Invoke(this, new SkillUsedEventArgs
            {
                Side = side,
                OldValue = input - step,
                NewValue = input,
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
        LeftEmptyCount = 0;
        RightEmptyCount = 0;
    }
}
