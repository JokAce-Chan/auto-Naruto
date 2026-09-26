using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace NaruttoTimer.Recognition.Ai;

/// <summary>YOLO 原始检出（128 填充输入坐标系，中心点 + 宽高）。</summary>
public readonly record struct RawDetection(float CenterX, float CenterY, float Width, float Height, float Score);

/// <summary>左右一次拼接推理的结果。</summary>
public sealed record CombinedInferenceResult(
    int LeftEmptyCount,
    int RightEmptyCount,
    IReadOnlyList<EnergyDetection> LeftDetections,
    IReadOnlyList<EnergyDetection> RightDetections);

/// <summary>
/// ONNX 空豆模型（源 infra/OnnxModel）：
/// best.onnx，输入 [1,3,128,128]（RGB / 1:1 等比缩放 + pad 114 + /255），
/// 输出 [1,5,N]（4 框 + 1 置信度，单类 = 空豆，emptyClass = 0）。
/// </summary>
public sealed class OnnxEnergyModel : IDisposable
{
    public const int InputSize = 128;
    public const int EmptyClass = 0;
    public const byte PadValue = 114;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string[] _outputNames;
    private readonly int _inputWidth;
    private readonly int _inputHeight;
    private readonly float _confidence;
    private readonly float _nms;

    public OnnxEnergyModel(string modelPath, double confidence = 0.7, double nms = 0.25, int intraOpThreads = 2)
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException($"ONNX 模型不存在：{modelPath}", modelPath);

        var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Max(1, intraOpThreads),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _session = new InferenceSession(modelPath, options);
        _inputName = _session.InputMetadata.Keys.First();
        _outputNames = _session.OutputMetadata.Keys.ToArray();
        var dims = _session.InputMetadata[_inputName].Dimensions;
        _inputHeight = dims.Length > 2 && dims[2] > 0 ? dims[2] : InputSize;
        _inputWidth = dims.Length > 3 && dims[3] > 0 ? dims[3] : InputSize;
        _confidence = (float)confidence;
        _nms = (float)nms;
    }

    public string InputName => _inputName;
    public int InputWidth => _inputWidth;
    public int InputHeight => _inputHeight;
    public double ConfidenceThreshold => _confidence;
    public double NmsThreshold => _nms;

    /// <summary>单条能量条推理，返回空豆数量（源 OnnxModel.infer）。</summary>
    public int InferEmptyCount(Mat bgr) => RunModel(bgr).Count; // 单类模型：所有检出都属于 emptyClass

    /// <summary>左右条垂直拼接后一次推理（源 OnnxModel.inferCombined）。</summary>
    public CombinedInferenceResult InferCombined(Mat leftBgr, Mat rightBgr)
    {
        using var concat = VConcatManual(leftBgr, rightBgr);
        var detections = RunModel(concat);

        int leftRows = leftBgr.Rows;
        int leftCols = leftBgr.Cols;
        int rightRows = rightBgr.Rows;
        int rightCols = rightBgr.Cols;

        float totalRows = concat.Rows;
        float cols = concat.Cols;
        float scale = Math.Min(_inputHeight / totalRows, _inputWidth / cols);
        float padTop = (_inputHeight - totalRows * scale) / 2f;
        float padLeft = (_inputWidth - cols * scale) / 2f;

        int leftEmpty = 0;
        int rightEmpty = 0;
        var leftBoxes = new List<EnergyDetection>();
        var rightBoxes = new List<EnergyDetection>();

        foreach (var d in detections)
        {
            float centerX = (d.CenterX - padLeft) / scale;
            float centerY = (d.CenterY - padTop) / scale;
            float width = d.Width / scale;
            float height = d.Height / scale;

            if (centerY < leftRows)
            {
                leftEmpty++;
                leftBoxes.Add(Normalize(centerX, centerY, width, height, d.Score, leftCols, leftRows));
            }
            else
            {
                rightEmpty++;
                rightBoxes.Add(Normalize(centerX, centerY - leftRows, width, height, d.Score, rightCols, rightRows));
            }
        }

        return new CombinedInferenceResult(leftEmpty, rightEmpty, leftBoxes, rightBoxes);
    }

    private static EnergyDetection Normalize(float centerX, float centerY, float width, float height, float score, int cols, int rows)
    {
        float x = (centerX - width / 2f) / cols;
        float y = (centerY - height / 2f) / rows;
        return new EnergyDetection(x, y, width / cols, height / rows, score);
    }

    /// <summary>源 OnnxModel.vconcatManual：高度相加、宽度取大，不足处补黑。</summary>
    private static Mat VConcatManual(Mat top, Mat bottom)
    {
        int rows = top.Rows + bottom.Rows;
        int cols = Math.Max(top.Cols, bottom.Cols);
        var result = new Mat(rows, cols, top.Type(), new Scalar(0, 0, 0));
        using (var roi = new Mat(result, new Rect(0, 0, top.Cols, top.Rows)))
            top.CopyTo(roi);
        using (var roi = new Mat(result, new Rect(0, top.Rows, bottom.Cols, bottom.Rows)))
            bottom.CopyTo(roi);
        return result;
    }

    private List<RawDetection> RunModel(Mat bgr)
    {
        float[] input = PreprocessToRaw(bgr);
        using var inputValue = OrtValue.CreateTensorValueFromMemory(input, new long[] { 1, 3, _inputHeight, _inputWidth });
        using var runOptions = new RunOptions();
        using var results = _session.Run(runOptions, new[] { _inputName }, new[] { inputValue }, _outputNames);

        var output = results[0];
        long[] shape = output.GetTensorTypeAndShape().Shape;
        return Postprocess(output.GetTensorDataAsSpan<float>(), (int)shape[1], (int)shape[2]);
    }

    /// <summary>源 OnnxModel.preprocessToRaw：BGR→RGB，等比缩放到 128，pad 114，/255，转 CHW。</summary>
    private float[] PreprocessToRaw(Mat bgr)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

        float scale = Math.Min((float)InputSize / rgb.Cols, (float)InputSize / rgb.Rows);
        int width = (int)(rgb.Cols * scale);
        int height = (int)(rgb.Rows * scale);
        if (width < 1 || height < 1) throw new ArgumentException("能量条区域过小，无法推理", nameof(bgr));

        using var resized = new Mat();
        Cv2.Resize(rgb, resized, new Size(width, height), 0, 0, InterpolationFlags.Linear);

        using var padded = new Mat(InputSize, InputSize, MatType.CV_8UC3, new Scalar(PadValue, PadValue, PadValue));
        int top = (InputSize - height) / 2;
        int left = (InputSize - width) / 2;
        using (var roi = new Mat(padded, new Rect(left, top, width, height)))
            resized.CopyTo(roi);

        using var floatMat = new Mat();
        padded.ConvertTo(floatMat, MatType.CV_32FC3, 1.0 / 255.0);

        int plane = InputSize * InputSize;
        // 注意：Mat.GetArray 对多通道 Mat 不适用（按元素而非通道计数），直接按 float 拷出 HWC 数据。
        var hwc = new float[plane * 3];
        Marshal.Copy(floatMat.Data, hwc, 0, hwc.Length);

        var chw = new float[3 * plane];
        for (int i = 0; i < plane; i++)
        {
            chw[i] = hwc[i * 3];
            chw[plane + i] = hwc[i * 3 + 1];
            chw[2 * plane + i] = hwc[i * 3 + 2];
        }
        return chw;
    }

    /// <summary>源 OnnxModel.postprocess：置信度过滤 + NMS(0.25)，框转左上角坐标。</summary>
    /// <remarks>输出为 [1,5,N]（4 框 + 1 置信度）；若导出为 [1,N,5] 则自动适配。</remarks>
    private List<RawDetection> Postprocess(ReadOnlySpan<float> data, int channels, int count)
    {
        bool channelFirst = channels == 5 && count > 5;
        int detections = channelFirst ? count : channels;
        var candidates = new List<(Rect2d Box, float Score, RawDetection Detection)>(detections);

        for (int i = 0; i < detections; i++)
        {
            float score = channelFirst ? data[4 * detections + i] : data[i * 5 + 4];
            if (score < _confidence) continue;
            float cx = channelFirst ? data[i] : data[i * 5];
            float cy = channelFirst ? data[detections + i] : data[i * 5 + 1];
            float w = channelFirst ? data[2 * detections + i] : data[i * 5 + 2];
            float h = channelFirst ? data[3 * detections + i] : data[i * 5 + 3];
            candidates.Add((
                new Rect2d(cx - w / 2.0, cy - h / 2.0, w, h),
                score,
                new RawDetection(cx, cy, w, h, score)));
        }

        if (candidates.Count == 0) return new List<RawDetection>();

        CvDnn.NMSBoxes(
            candidates.Select(c => c.Box),
            candidates.Select(c => c.Score),
            _confidence,
            _nms,
            out int[] keep);

        var kept = new List<RawDetection>(keep.Length);
        foreach (int index in keep) kept.Add(candidates[index].Detection);
        return kept;
    }

    public void Dispose() => _session.Dispose();
}
