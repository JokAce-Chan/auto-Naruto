using NaruttoTimer.Capture;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Recognition;

/// <summary>单侧识别输出：格数、值、各格分类（含中心 x 供预览绘制）、是否加载中。</summary>
public sealed record SideRecognition(
    GridCount GridCount,
    int Value,
    IReadOnlyList<CellState> Cells,
    bool InLoading,
    IReadOnlyList<int>? Centers = null)
{
    public static SideRecognition Loading() => new(GridCount.G4, 0, Array.Empty<CellState>(), true);
}

/// <summary>一次识别周期输出。</summary>
public sealed record RecognitionOutput(SideRecognition Left, SideRecognition Right);

/// <summary>识别器：输入一帧，输出两侧读数。</summary>
public interface IRecognizer
{
    RecognitionOutput Recognize(CapturedFrame frame);
    void Reset();
}