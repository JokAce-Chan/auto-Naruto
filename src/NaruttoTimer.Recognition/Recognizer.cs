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
    double LoadingSeconds = 0.3,
    double GridJudgeSeconds = 1.0)
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
        public GridCount? Grid;
        public bool ReJudgeArmed;
        public GridCount? ReJudgeNewGrid;
        public int ReJudgeCount;
    }

    private RecognizerOptions _options;
    private readonly SideState[] _states = { new(), new() };

    public RecognizerOptions Options => _options;

    public Recognizer(RecognizerOptions options) => _options = options;

    public RecognitionOutput Recognize(CapturedFrame frame)
    {
        var left = RecognizeSide(Side.Left, frame, _options.LeftRoi, _states[0]);
        var right = RecognizeSide(Side.Right, frame, _options.RightRoi, _states[1]);
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

    private SideRecognition RecognizeSide(Side side, CapturedFrame frame, RoiConfig? roi, SideState st)
    {
        if (roi == null)
        {
            return MarkUnstable(st, frame.Timestamp);
        }

        var cells = CellDetector.Detect(frame, roi, _options);
        var detectedGrid = cells.Count switch
        {
            4 => GridCount.G4,
            6 => GridCount.G6,
            _ => (GridCount?)null,
        };

        if (detectedGrid == null)
        {
            // 菱形缺失：先维持当前 G 值/倒计时；缺失持续超过复判间隔后，允许下一次有效读数重新判定 G4/G6。
            var res = MarkUnstable(st, frame.Timestamp);
            if (st.UnstableSince.HasValue &&
                (frame.Timestamp - st.UnstableSince.Value).TotalSeconds >= _options.GridJudgeSeconds)
            {
                st.ReJudgeArmed = true;
            }
            return res;
        }

        st.UnstableSince = null;

        GridCount grid = detectedGrid.Value;

        // —— G4/G6 复判门槛：仅在“菱形缺失”后允许切换格数；稳定后停止，缺失再次触发 ——
        if (st.ReJudgeArmed)
        {
            // 复判时要求新格数连续若干帧，避免恢复瞬间的单帧闪断锁错格数。
            if (st.ReJudgeNewGrid == grid) st.ReJudgeCount++;
            else { st.ReJudgeNewGrid = grid; st.ReJudgeCount = 1; }
            if (st.ReJudgeCount >= _options.DebounceFrames)
            {
                st.ReJudgeArmed = false;
                st.Grid = grid;
                st.ReJudgeNewGrid = null;
                st.ReJudgeCount = 0;
            }
            else
            {
                // 尚未稳定：保持当前 G，本次仍按上一稳定值处理
                return HoldGrid(st, side, cells);
            }
        }
        else if (st.Grid == null)
        {
            // 首个有效读数：立下当前格数基线
            st.Grid = grid;
        }
        else if (st.Grid.Value != grid)
        {
            // 未缺失但格数变化：视为噪声/短闪，保持当前 G，不改值、不累加防抖
            return HoldGrid(st, side, cells);
        }

        // 逻辑1：亮格必须连成一块。左侧从最左连续（◆◇◇◇ / ◆◆◇◇），右侧从最右连续（◇◇◇◆ / ◇◇◆◆）。
        // 违反此规则的读取视为噪声，不提交、不累加防抖，保持上一稳定值，避免背景/阈值波动导致 G 值跳变。
        var states = cells.Select(c => c.State).ToList();
        int totalBright = states.Count(s => s == CellState.Bright);
        int continuous = side == Side.Left ? LeadingBright(states) : TrailingBright(states);
        if (continuous != totalBright)
        {
            // 本帧违反连续规则：不改变值、不累加防抖；预览小点改为“跟随稳定值”，与数值一致、不闪烁。
            st.Pending = null;
            st.PendingCount = 0;
            var centers = cells.Select(c => c.CenterX).ToList();
            if (st.Stable == null)
                return new SideRecognition(grid, 0, SnapStates(cells.Count, 0, side), false, centers);
            int snapValue = st.Stable.GridCount == grid ? st.Stable.Value : Math.Min(st.Stable.Value, (int)grid);
            return st.Stable with { Cells = SnapStates(cells.Count, snapValue, side), Centers = centers };
        }

        // 合法时值 = 连续亮块长度（左侧前连续 / 右侧后连续）
        int value = totalBright;
        var candidate = new SideRecognition(grid, value, states, false, cells.Select(c => c.CenterX).ToList());

        if (st.Pending != null && st.Pending.GridCount == candidate.GridCount && st.Pending.Value == candidate.Value)
        {
            st.PendingCount++;
        }
        else
        {
            st.Pending = candidate;
            st.PendingCount = 1;
        }

        // 防抖确认：首个合理读数立即立为基线（防抖保护的是帧间变化，不是首次获取）
        if (st.Stable == null || st.PendingCount >= _options.DebounceFrames)
        {
            st.Stable = candidate;
        }

        // 规则层只消费防抖后的稳定值；当前帧分类仅用于预览显示
        return st.Stable with { Cells = candidate.Cells };
    }

    /// <summary>未发生缺失但格数变化，或复判尚未稳定时：保持当前 G 与稳定值，按稳定值合成预览图案。</summary>
    private SideRecognition HoldGrid(SideState st, Side side, IReadOnlyList<DetectedCell> cells)
    {
        GridCount grid = st.Grid!.Value;
        st.Pending = null;
        st.PendingCount = 0;
        var centers = cells.Select(c => c.CenterX).ToList();
        if (st.Stable == null)
            return new SideRecognition(grid, 0, SnapStates((int)grid, 0, side), false, centers);
        int snapValue = st.Stable.GridCount == grid ? st.Stable.Value : Math.Min(st.Stable.Value, (int)grid);
        return st.Stable with { Cells = SnapStates((int)grid, snapValue, side), Centers = centers };
    }

    private static int LeadingBright(IReadOnlyList<CellState> cells)
    {
        int n = 0;
        while (n < cells.Count && cells[n] == CellState.Bright) n++;
        return n;
    }

    private static int TrailingBright(IReadOnlyList<CellState> cells)
    {
        int n = cells.Count - 1;
        int count = 0;
        while (n >= 0 && cells[n] == CellState.Bright) { n--; count++; }
        return count;
    }

    /// <summary>按稳定值合成连续亮块图案（左侧前缀 / 右侧后缀），用于预览显示。</summary>
    private static IReadOnlyList<CellState> SnapStates(int count, int value, Side side)
    {
        value = Math.Clamp(value, 0, count);
        var list = new List<CellState>(count);
        for (int i = 0; i < count; i++)
        {
            bool b = side == Side.Left ? i < value : i >= count - value;
            list.Add(b ? CellState.Bright : CellState.Dark);
        }
        return list;
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