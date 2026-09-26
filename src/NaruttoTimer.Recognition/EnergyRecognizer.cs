using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Recognition.Ai;
using NaruttoTimer.Recognition.ImageProcess;
using NaruttoTimer.Rules;
using OpenCvSharp;

namespace NaruttoTimer.Recognition;

/// <summary>
/// 能量识别器（源 domain/engine/Engine.onFrame），AI 模式（best.onnx）。
/// 左右能量条裁剪 → 垂直拼接一次推理 → 得到空豆数；
/// 值 = 值上限 − 空豆数（clamp 0~值上限）。
/// 注：传统（灰度采样）模式已移除；模型不可用时不再降级，识别会明确抛错。
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
            LoadWarning = $"AI 模型加载失败：{ex.Message}（传统模式已移除，识别不可用）";
        }

        _cropper = new ImageCropper(options.Label);
    }

    /// <summary>模型加载失败时的提示（null 表示正常）。</summary>
    public string? LoadWarning { get; }

    /// <summary>模型是否可用（false 表示识别不可用，不应开始识别）。</summary>
    public bool IsModelReady => _model != null;

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
        var model = _model ?? throw new InvalidOperationException(LoadWarning ?? "AI 模型不可用");

        using var leftBar = cropper.CropLeftEnergyBar(bgra);
        using var rightBar = cropper.CropRightEnergyBar(bgra);
        using var leftBgr = new Mat();
        using var rightBgr = new Mat();
        Cv2.CvtColor(leftBar, leftBgr, ColorConversionCodes.BGRA2BGR);
        Cv2.CvtColor(rightBar, rightBgr, ColorConversionCodes.BGRA2BGR);

        var combined = model.InferCombined(leftBgr, rightBgr);

        return new EnergyReading(
            Math.Clamp(EnergyRules.MaxValue - combined.LeftEmptyCount, 0, EnergyRules.MaxValue),
            Math.Clamp(EnergyRules.MaxValue - combined.RightEmptyCount, 0, EnergyRules.MaxValue),
            combined.LeftEmptyCount,
            combined.RightEmptyCount,
            combined.LeftDetections,
            combined.RightDetections);
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
