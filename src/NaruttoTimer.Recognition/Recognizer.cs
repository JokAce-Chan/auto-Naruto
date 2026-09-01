using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Recognition;

/// <summary>识别配置：阈值区间、ROI、防抖帧数、加载判定时长。</summary>
public sealed record RecognizerOptions(
    ColorRange BrightRange,
    ColorRange DarkRange,
    RoiConfig? LeftRoi = null,
    RoiConfig? RightRoi = null,
    ColorRange? OrangeRange = null,
    int DebounceFrames = 2,
    double LoadingSeconds = 0.3)
{
    /// <summary>值=4 突变橙红默认区间（天蓝亮格之外的警示色，归入 Bright）。</summary>
    public static ColorRange DefaultOrangeRange { get; } = new(180, 255, 40, 150, 0, 130);

    public ColorRange EffectiveOrangeRange => OrangeRange ?? DefaultOrangeRange;

    public static RecognizerOptions Default(RoiConfig? left = null, RoiConfig? right = null) =>
        new(new ColorRange(180, 255, 120, 255, 120, 255), new ColorRange(0, 90, 70, 165, 85, 180), left, right);
}

/// <summary>
/// 识别器：每帧输入 → 两侧输出。含格数自动判定（4/6）、值提取、防抖提交、加载判定。
/// </summary>
public sealed class Recognizer : IRecognizer
{
    private sealed class SideState
    {
        public SideRecognition? Stable;
        public SideRecognition? Pending;
        public int PendingCount;
        public DateTime? UnstableSince;
    }

    private RecognizerOptions _options;
    private readonly SideState[] _states = { new(), new() };

    public RecognizerOptions Options => _options;

    public Recognizer(RecognizerOptions options) => _options = options;

    public RecognitionOutput Recognize(CapturedFrame frame)
    {
        var left = RecognizeSide(frame, _options.LeftRoi, _states[0]);
        var right = RecognizeSide(frame, _options.RightRoi, _states[1]);
        return new RecognitionOutput(left, right);
    }

    public void Reset()
    {
        _states[0] = new SideState();
        _states[1] = new SideState();
    }

    /// <summary>应用新配置（阈值/ROI/防抖/加载时长），供校准与区域配置实时更新。</summary>
    public void UpdateOptions(RecognizerOptions options)
    {
        _options = options;
    }

    private SideRecognition RecognizeSide(CapturedFrame frame, RoiConfig? roi, SideState st)
    {
        if (roi == null)
        {
            return MarkUnstable(st, frame.Timestamp);
        }

        var cells = CellDetector.Detect(frame, roi, _options);
        var grid = cells.Count switch
        {
            4 => GridCount.G4,
            6 => GridCount.G6,
            _ => (GridCount?)null,
        };
        if (grid == null)
        {
            return MarkUnstable(st, frame.Timestamp);
        }

        st.UnstableSince = null;
        int value = cells.Count(c => c.State == CellState.Bright);
        var candidate = new SideRecognition(grid.Value, value, cells.Select(c => c.State).ToList(), false, cells.Select(c => c.CenterX).ToList());

        if (st.Pending != null && st.Pending.GridCount == candidate.GridCount && st.Pending.Value == candidate.Value)
        {
            st.PendingCount++;
        }
        else
        {
            st.Pending = candidate;
            st.PendingCount = 1;
        }

        // 防抖确认；首个合理读数立即立为基线（防抖保护的是帧间变化，不是首次获取）
        if (st.Stable == null || st.PendingCount >= _options.DebounceFrames)
        {
            st.Stable = candidate;
        }

        // 规则层只消费防抖后的稳定值；当前帧格分类仅用于预览显示
        return st.Stable with { Cells = candidate.Cells };
    }

    private SideRecognition MarkUnstable(SideState st, DateTime ts)
    {
        st.UnstableSince ??= ts;
        st.Pending = null;
        st.PendingCount = 0;
        bool loading = (ts - st.UnstableSince.Value).TotalSeconds >= _options.LoadingSeconds;
        if (loading)
        {
            st.Stable = SideRecognition.Loading();
            return st.Stable;
        }
        if (st.Stable == null)
        {
            // 尚无稳定值且未达加载时长：输出占位读数（值 0，非加载），规则层建立基线不触发
            return new SideRecognition(GridCount.G4, 0, Array.Empty<CellState>(), false);
        }
        // 未达加载时长：返回上一稳定结果（值不变，规则层不动作）
        return st.Stable with { Cells = Array.Empty<CellState>(), InLoading = false };
    }
}