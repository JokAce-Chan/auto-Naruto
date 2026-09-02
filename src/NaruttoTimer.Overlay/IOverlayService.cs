using NaruttoTimer.Rules;

namespace NaruttoTimer.Overlay;

/// <summary>置顶框服务：显示左右倒计时，参数可调。</summary>
public interface IOverlayService
{
    bool IsVisible { get; }
    bool IsLocked { get; }

    void Show();
    void Hide();
    void SetLocked(bool locked);
    void SetOpacity(int percent);          // 10~90
    void SetTextOpacity(int percent);       // 0~100（数字透明度）
    void SetColors(string leftHex, string rightHex);
    void Update(CountdownSnapshot snapshot);
}
