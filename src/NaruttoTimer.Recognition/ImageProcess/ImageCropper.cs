using NaruttoTimer.Data;
using OpenCvSharp;

namespace NaruttoTimer.Recognition.ImageProcess;

/// <summary>
/// 归一化坐标裁剪（源 domain/imageprocess/ImageCropper）：
/// 坐标 × 帧宽高，带 clamp，保证矩形至少 1x1。
/// </summary>
public sealed class ImageCropper
{
    private readonly LabelImageConfig _config;

    public ImageCropper(LabelImageConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public LabelImageConfig Config => _config;

    public Mat CropLeftEnergyBar(Mat frame) => CropLabelArea(frame, _config.EnergyBar.Left);

    public Mat CropRightEnergyBar(Mat frame) => CropLabelArea(frame, _config.EnergyBar.Right);

    /// <summary>归一化区域 → 像素区域（源 ImageCropper.cropLabelArea 的 clamp 逻辑）。</summary>
    public static Rect ToPixelRect(Mat frame, LabelRect rect)
    {
        if (rect == null) throw new ArgumentNullException(nameof(rect));
        int width = frame.Cols;
        int height = frame.Rows;
        int x = (int)(rect.LeftTop.X * width);
        int y = (int)(rect.LeftTop.Y * height);
        int x2 = (int)(rect.RightBottom.X * width);
        int y2 = (int)(rect.RightBottom.Y * height);

        int left = Clamp(x, 0, Math.Max(0, width - 1));
        int right = Clamp(x2, left + 1, width);
        int top = Clamp(y, 0, Math.Max(0, height - 1));
        int bottom = Clamp(y2, top + 1, height);
        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>裁剪并拷贝出独立 Mat（调用方可安全释放源帧）。</summary>
    public static Mat CropLabelArea(Mat frame, LabelRect rect)
    {
        var roi = ToPixelRect(frame, rect);
        using var view = new Mat(frame, roi);
        return view.Clone();
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
}
