using Avalonia.Controls;
using Yohuke.DanmuPanel.Views;
using Yohuke.DanmuPanel.Views.Dialogs;

namespace Yohuke.DanmuPanel.Windowing;

/// <summary>在发起控件当前所属的桌面窗口中显示确认对话框。</summary>
public sealed class WindowConfirmationService : IConfirmationService
{
    /// <inheritdoc />
    public Task<bool> ConfirmAsync(Control source, string title, string message)
    {
        var owner = TopLevel.GetTopLevel(source) as Window
            ?? throw new InvalidOperationException("A desktop confirmation requires an attached window.");
        return new ConfirmationWindow(title, message).ShowDialog<bool>(owner);
    }
}
