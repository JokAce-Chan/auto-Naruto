using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Recognition.Ai;
using NaruttoTimer.Recognition.ImageProcess;
using NaruttoTimer.Rules;
using OpenCvSharp;

namespace NaruttoTimer.Recognition;

/// <summary>
/// 能量识别器（源 domain/engine/Engine.onFrame）：
/// 左右能量条裁剪 → (AI 拼接推理 | 传统 4 点采样) → 值 = 4 - 空豆数（clamp 0~4）。
/// </summary>
public sealed class EnergyRecognizer : IEnergyRecognizer
{
    private readonly OnnxEnergyModel? _model;
    private readonly object _sync = new();
    private RecognizerOptions _options;
    private ImageCropper _cropper;
    private bool _disposed;

    public EnergyRecognizer(RecognizerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        try
        {
            _model = new OnnxEnergyModel(options.ModelPath, options.ConfidenceThreshold, options.NmsThreshold);
            LoadWarning = null;
        }
        catch (Exception ex)
        {
            _model = null;
            LoadWarning = $"AI 模型加载失败，已回退传统模式：{ex.Message}";
        }

        _cropper = new ImageCropper(options.Label);
    }

    /// <summary>模型加载失败时的提示（null 表示正常）。</summary>
    public string? LoadWarning { get; }

    /// <summary>实际生效的模式（AI 模型不可用时回退为传统模式）。</summary>
    public LabelMode EffectiveMode => ResolveMode(_options);

    /// <summary>按给定参数解析实际生效的模式（模型不可用时回退传统）。</summary>
    private LabelMode ResolveMode(RecognizerOptions options) =>
        options.Label.Mode == LabelMode.AI && _model != null ? LabelMode.AI : LabelMode.TRADITIONAL;

    public RecognizerOptions Options
    {
        get { lock (_sync) return _options; }
    }

    /// <summary>更新参数（标注/阈值）；不重新加载 ONNX 会话。</summary>
    public void UpdateOptions(RecognizerOptions options)
    {
        lock (_sync)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _cropper = new ImageCropper(_options.Label);
        }
    }

    public EnergyReading Recognize(CapturedFrame frame)
    {
        using var mat = MatUtil.FromBgra(frame);
        return Recognize(mat);
    }

    /// <summary>直接以 BGRA Mat 识别（便于测试与离线帧）。</summary>
    public EnergyReading Recognize(Mat bgra)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        RecognizerOptions options;
        ImageCropper cropper;
        lock (_sync)
        {
            options = _options;
            cropper = _cropper;
        }

        return Recognize(bgra, options, cropper);
    }

    /// <summary>用指定参数识别一帧（用于「区域标注」弹窗的即时测试），不改变当前设置。</summary>
    public EnergyReading TestRecognize(RecognizerOptions options, Mat bgra)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Recognize(bgra, options, new ImageCropper(options.Label));
    }

    private EnergyReading Recognize(Mat bgra, RecognizerOptions options, ImageCropper cropper)
    {
        using var leftBar = cropper.CropLeftEnergyBar(bgra);
        using var rightBar = cropper.CropRightEnergyBar(bgra);
        using var leftBgr = new Mat();
        using var rightBgr = new Mat();
        Cv2.CvtColor(leftBar, leftBgr, ColorConversionCodes.BGRA2BGR);
        Cv2.CvtColor(rightBar, rightBgr, ColorConversionCodes.BGRA2BGR);

        int leftEmpty;
        int rightEmpty;
        IReadOnlyList<EnergyDetection> leftDetections;
        IReadOnlyList<EnergyDetection> rightDetections;

        if (ResolveMode(options) == LabelMode.AI)
        {
            var combined = _model!.InferCombined(leftBgr, rightBgr);
            leftEmpty = combined.LeftEmptyCount;
            rightEmpty = combined.RightEmptyCount;
            leftDetections = combined.LeftDetections;
            rightDetections = combined.RightDetections;
        }
        else
        {
            leftEmpty = TraditionalEnergyDetector.CountEnergyNum(leftBgr, options.TraditionalGrayThreshold);
            rightEmpty = TraditionalEnergyDetector.CountEnergyNum(rightBgr, options.TraditionalGrayThreshold);
            leftDetections = Array.Empty<EnergyDetection>();
            rightDetections = Array.Empty<EnergyDetection>();
        }

        return new EnergyReading(
            Math.Clamp(EnergyRules.MaxValue - leftEmpty, 0, EnergyRules.MaxValue),
            Math.Clamp(EnergyRules.MaxValue - rightEmpty, 0, EnergyRules.MaxValue),
            leftEmpty,
            rightEmpty,
            leftDetections,
            rightDetections);
    }

    public void Reset()
    {
        // 识别器本身无跨帧状态（稳定/倒计时由规则层维护）。
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _model?.Dispose();
    }
}
