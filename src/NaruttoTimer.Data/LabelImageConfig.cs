using System.Text.Json.Serialization;

namespace NaruttoTimer.Data;

/// <summary>标注识别模式，对齐源项目 label_image_config.json 的 mode 字段。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LabelMode>))]
public enum LabelMode
{
    /// <summary>传统模式：按灰度采样点统计空豆数量（源 EnergyDetector.detectnoAi）。</summary>
    TRADITIONAL,

    /// <summary>AI 模式：使用 best.onnx（YOLOv8 单类「空豆」）推理（源 OnnxModel）。</summary>
    AI,
}

/// <summary>归一化坐标（0~1，相对整帧宽高）。</summary>
public sealed class LabelCoordinate
{
    public double X { get; set; }
    public double Y { get; set; }

    public LabelCoordinate() { }

    public LabelCoordinate(double x, double y)
    {
        X = x;
        Y = y;
    }

    public LabelCoordinate Clone() => new(X, Y);

    public override string ToString() => $"({X:0.#####},{Y:0.#####})";
}

/// <summary>归一化矩形：left_top / right_bottom。</summary>
public sealed class LabelRect
{
    [JsonPropertyName("left_top")]
    public LabelCoordinate LeftTop { get; set; } = new();

    [JsonPropertyName("right_bottom")]
    public LabelCoordinate RightBottom { get; set; } = new();

    public LabelRect() { }

    public LabelRect(double x1, double y1, double x2, double y2)
    {
        LeftTop = new LabelCoordinate(x1, y1);
        RightBottom = new LabelCoordinate(x2, y2);
    }

    /// <summary>源 LabelImageConfig.Rect.isValidate：两点非空且 x1 &lt; x2、y1 &lt; y2。</summary>
    [JsonIgnore]
    public bool IsValid =>
        LeftTop != null && RightBottom != null &&
        LeftTop.X < RightBottom.X && LeftTop.Y < RightBottom.Y;

    [JsonIgnore]
    public double Width => RightBottom.X - LeftTop.X;

    [JsonIgnore]
    public double Height => RightBottom.Y - LeftTop.Y;

    public LabelRect Clone() => new(LeftTop.X, LeftTop.Y, RightBottom.X, RightBottom.Y);

    public override string ToString() =>
        $"[{LeftTop.X:0.#####},{LeftTop.Y:0.#####} -> {RightBottom.X:0.#####},{RightBottom.Y:0.#####}]";
}

/// <summary>左右能量条（源 LabelImageConfig.EnergyBar）。</summary>
public sealed class EnergyBarConfig
{
    public LabelRect Left { get; set; } = new();

    public LabelRect Right { get; set; } = new();

    public EnergyBarConfig Clone() => new() { Left = Left.Clone(), Right = Right.Clone() };
}

/// <summary>
/// 标注配置（对应源项目 assets/label_image_config.json 的 energy_bar / mode）。
/// energy_bar：左右能量条区域（AI 模式垂直拼接后一次推理；传统模式在条内取 4 点采样）。
/// 注：源项目的 battle_area（战斗模板匹配）已随“战斗检测”功能一并移除。
/// </summary>
public sealed class LabelImageConfig
{
    [JsonPropertyName("energy_bar")]
    public EnergyBarConfig EnergyBar { get; set; } = new();

    [JsonPropertyName("mode")]
    public LabelMode Mode { get; set; } = LabelMode.AI;

    /// <summary>源项目 assets/label_image_config.json 的能量条默认值（1920x1080 归一化坐标）。</summary>
    public static LabelImageConfig CreateDefault() => new()
    {
        EnergyBar = new EnergyBarConfig
        {
            Left = new LabelRect(0.09442336112260818, 0.048245493322610855, 0.21467097103595734, 0.18202096223831177),
            Right = new LabelRect(0.7857308387756348, 0.048245493322610855, 0.9059784412384033, 0.18202096223831177),
        },
        Mode = LabelMode.AI,
    };

    public LabelImageConfig Clone() => new()
    {
        EnergyBar = EnergyBar == null ? CreateDefault().EnergyBar : EnergyBar.Clone(),
        Mode = Mode,
    };

    /// <summary>校验（源 LabelImageConfig.Rect.isValidate）；返回 null 表示通过，否则返回错误描述。</summary>
    public string? Validate()
    {
        if (!EnergyBar.Left.IsValid) return "energy_bar.left 无效：需要满足 x1 < x2 且 y1 < y2";
        if (!EnergyBar.Right.IsValid) return "energy_bar.right 无效：需要满足 x1 < x2 且 y1 < y2";
        return null;
    }
}
