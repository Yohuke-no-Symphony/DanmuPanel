using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Yohuke.DanmuPanel.iPad.ViewModels;

namespace Yohuke.DanmuPanel.iPad.Views;

/// <summary>以桌面式工作区树呈现房间选择，管理操作仍由已有移动页面提供。</summary>
public sealed partial class IpadWorkspaceSidebar : UserControl
{
    public event Action? ManageRequested;
    public event Action? CloseRequested;

    /// <summary>绑定复用共享节点的侧栏模型。</summary>
    public IpadWorkspaceSidebar(IpadWorkspaceSidebarViewModel model)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = model;
    }

    private void Manage_OnClick(object? sender, RoutedEventArgs args)
    {
        ManageRequested?.Invoke();
    }

    private void Close_OnClick(object? sender, RoutedEventArgs args)
    {
        CloseRequested?.Invoke();
    }
}
