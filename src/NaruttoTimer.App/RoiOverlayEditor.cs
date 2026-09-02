using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NaruttoTimer.Data;

namespace NaruttoTimer.App;

/// <summary>
/// 预览图上可拖拽移动 / 右下角缩放的 ROI 编辑框。
/// 支持“屏幕坐标 ↔ 帧坐标”双向换算，拖拽/缩放时通过回调把新的帧坐标 ROI 写回。
/// 主预览与区域配置弹窗共用。
/// </summary>
public sealed class RoiOverlayEditor
{
    private readonly Grid _group;
    private readonly Rectangle _rect;
    private readonly Thumb _resize;
    private readonly TextBlock _label;
    private readonly Action<RoiConfig> _onChanged;
    private double _scale = 1;
    private double _ox;
    private double _oy;

    public RoiConfig Roi { get; private set; }

    public RoiOverlayEditor(RoiConfig roi, Color border, string label, Action<RoiConfig> onChanged)
    {
        Roi = roi;
        _onChanged = onChanged;
        var brush = new SolidColorBrush(border);

        _group = new Grid { Background = Brushes.Transparent };
        _rect = new Rectangle
        {
            Stroke = brush,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        _group.Children.Add(_rect);

        _label = new TextBlock
        {
            Text = label,
            Foreground = brush,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(6, 4, 0, 0),
            IsHitTestVisible = false,
        };
        _group.Children.Add(_label);

        var move = new Thumb { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        move.DragDelta += (_, e) =>
        {
            int nx = Roi.X + (int)Math.Round(e.HorizontalChange / _scale);
            int ny = Roi.Y + (int)Math.Round(e.VerticalChange / _scale);
            Apply(Roi with { X = Math.Max(0, nx), Y = Math.Max(0, ny) });
        };
        _group.Children.Add(move);

        _resize = new Thumb
        {
            Width = 14,
            Height = 14,
            Cursor = Cursors.SizeNWSE,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = brush,
        };
        _resize.DragDelta += (_, e) =>
        {
            int w = Roi.Width + (int)Math.Round(e.HorizontalChange / _scale);
            int h = Roi.Height + (int)Math.Round(e.VerticalChange / _scale);
            Apply(Roi with { Width = Math.Max(6, w), Height = Math.Max(6, h) });
        };
        _group.Children.Add(_resize);
    }

    public UIElement Element => _group;

    /// <summary>按新的帧坐标 + 缩放/偏移重新定位编辑框。</summary>
    public void Reposition(double scale, double ox, double oy, RoiConfig roi)
    {
        _scale = scale;
        _ox = ox;
        _oy = oy;
        Roi = roi;
        Position();
    }

    private void Apply(RoiConfig r)
    {
        Roi = r;
        _onChanged(r);
        Position();
    }

    public void SetLabel(string text)
    {
        _label.Text = text;
    }

    private void Position()
    {
        Canvas.SetLeft(_group, _ox + Roi.X * _scale);
        Canvas.SetTop(_group, _oy + Roi.Y * _scale);
        _group.Width = Math.Max(6, Roi.Width * _scale);
        _group.Height = Math.Max(6, Roi.Height * _scale);
        Canvas.SetZIndex(_group, 10);
    }
}