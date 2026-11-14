using Yohuke.DanmuPanel.I18n;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Yohuke.DanmuPanel.iPad.Views;
using Yohuke.DanmuPanel.Views.Dialogs;

namespace Yohuke.DanmuPanel.iPad.Services;

/// <summary>将共享控件的确认请求路由到当前 iPad 页面宿主。</summary>
public sealed class IpadConfirmationService : IConfirmationService
{
    /// <inheritdoc />
    public Task<bool> ConfirmAsync(Control source, string title, string message)
    {
        var shell = source as IpadShellView ?? source.GetVisualAncestors().OfType<IpadShellView>().FirstOrDefault();
        return shell is null
            ? throw new InvalidOperationException(Localization.Get("iPad.ConfirmationSourceInvalid"))
            : shell.ConfirmAsync(title, message);
    }
}
