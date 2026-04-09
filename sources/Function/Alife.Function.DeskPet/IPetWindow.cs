namespace Alife.Function.DeskPet;

/// <summary>
/// 窗口底层服务接口，解耦 UI 与业务逻辑
/// </summary>
public interface IPetWindow
{
    (double ScaleX, double ScaleY) GetDpi();
    (double Left, double Top, double Width, double Height) GetLayout();
    void ProgrammaticMove(double targetLeft, double targetTop, int duration);
}
