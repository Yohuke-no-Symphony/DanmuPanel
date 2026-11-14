using Avalonia.Controls;

namespace Yohuke.DanmuPanel.Views.Dialogs;

/// <summary>由应用宿主呈现与当前控件关联的确认交互。</summary>
public interface IConfirmationService
{
    /// <summary>向用户显示确认内容，仅在明确确认后返回成功。</summary>
    Task<bool> ConfirmAsync(Control source, string title, string message);
}
