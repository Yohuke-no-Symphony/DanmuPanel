using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Yohuke.DanmuPanel.iPad.ViewModels;
using Yohuke.DanmuPanel.ViewModels.Workspace;
using Yohuke.DanmuPanel.Views.Dialogs;

namespace Yohuke.DanmuPanel.iPad.Views;

/// <summary>提供适合触控的工作区、直播间及搜索编辑页面。</summary>
public sealed partial class IpadRoomsPage : UserControl, IDisposable
{
    private readonly IpadRoomsViewModel model;

    /// <summary>构造页面并把确认请求关联到当前页面宿主。</summary>
    public IpadRoomsPage(IpadShellViewModel shell, IConfirmationService confirmations)
    {
        AvaloniaXamlLoader.Load(this);
        model = new(shell, (title, message) => confirmations.ConfirmAsync(this, title, message));
        DataContext = model;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        model.Dispose();
    }

    private void SearchResult_OnClick(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: LiveRoomSearchResultViewModel result })
        {
            model.Editor?.AddSearchedRoomCommand.Execute(result);
        }
    }
}
