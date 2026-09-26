using OpenCvSharp;

namespace NaruttoTimer.Recognition.ImageProcess;

/// <summary>
/// 传统模式空豆检测（源 domain/imageprocess/EnergyDetector.detectnoAi）。
/// 灰度化后在能量条水平中线等距取 4 个采样点，灰度 &lt; 110 记为空豆。
/// </summary>
public static class TraditionalEnergyDetector
{
    /// <summary>源项目 EnergyDetector.colorThreshold = 110。</summary>
    public const int ColorThreshold = 110;

    public static int CountEnergyNum(Mat bgr, int grayThreshold = ColorThreshold)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

        int rows = gray.Rows;
        int cols = gray.Cols;
        int empty = 0;
        for (int i = 0; i < 4; i++)
        {
            int y = rows / 2;
            int x = (int)(cols * 0.125d) + (cols / 4) * i;
            if (x < 0 || x >= cols || y < 0 || y >= rows) continue;
            if (gray.At<byte>(y, x) < grayThreshold) empty++;
        }
        return empty;
    }
}
