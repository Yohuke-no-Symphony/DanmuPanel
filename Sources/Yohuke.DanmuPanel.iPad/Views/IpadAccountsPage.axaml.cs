using Yohuke.DanmuPanel.I18n;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Yohuke.DanmuPanel.iPad.ViewModels;
using Yohuke.DanmuPanel.Models.BrowserLogin;
using Yohuke.DanmuPanel.Models.State;
using Yohuke.DanmuPanel.Services;
using Yohuke.DanmuPanel.Views.Dialogs;

namespace Yohuke.DanmuPanel.iPad.Views;

/// <summary>在移动页面托管 WKWebView 登录，离开时立即释放网页资源。</summary>
public sealed partial class IpadAccountsPage : UserControl, IDisposable
{
    private readonly IpadAccountsViewModel model;
    private readonly BrowserCookieLoginService login = new();
    private NativeWebView? browser;
    private BilibiliAccount? browserAccount;

    /// <summary>建立账号页面和共享账号状态的绑定。</summary>
    public IpadAccountsPage(IpadShellViewModel shell, IConfirmationService confirmations)
    {
        AvaloniaXamlLoader.Load(this);
        model = new(shell, OpenBrowser, (title, message) => confirmations.ConfirmAsync(this, title, message));
        DataContext = model;
    }

    /// <summary>停止网页导航、移除原生控件并恢复账号列表。</summary>
    public void ReleaseBrowser()
    {
        var current = browser;
        browser = null;
        this.FindControl<Border>("BrowserHost")!.Child = null;
        model.IsBrowserOpen = false;
        if (current is not null)
        {
            current.EnvironmentRequested -= Browser_OnEnvironmentRequested;
            current.NavigationCompleted -= Browser_OnNavigationCompleted;
            try
            {
                current.Stop();
            }
            catch (Exception exception)
            {
                model.Shell.ReportError(exception);
            }
            (current as IDisposable)?.Dispose();
        }
        browserAccount = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ReleaseBrowser();
        model.Dispose();
    }

    private void OpenBrowser(BilibiliAccount? account)
    {
        ReleaseBrowser();
        browserAccount = account;
        try
        {
            browser = new();
            browser.EnvironmentRequested += Browser_OnEnvironmentRequested;
            browser.NavigationCompleted += Browser_OnNavigationCompleted;
            model.IsBrowserOpen = true;
            model.BrowserStatus = Localization.Get("iPad.BrowserHelp");
            this.FindControl<Border>("BrowserHost")!.Child = browser;
            browser.Source = login.GetLoginUri(BrowserLoginPlatform.Bilibili);
        }
        catch (Exception exception)
        {
            ReleaseBrowser();
            model.Shell.ReportError(exception);
        }
    }

    private void Browser_OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = false;
        if (args is AppleWKWebViewEnvironmentRequestedEventArgs apple)
        {
            apple.NonPersistentDataStore = true;
        }
    }

    private void Browser_OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        model.BrowserStatus = Localization.Get("iPad.BrowserHelp");
    }

    private async void Capture_OnClick(object? sender, RoutedEventArgs args)
    {
        var current = browser;
        if (current is null)
        {
            return;
        }
        await model.Shell.RunAsync(async () =>
        {
            try
            {
                var cookies = current.TryGetCookieManager()
                    ?? throw new InvalidOperationException(Localization.Get("iPad.CookieUnavailable"));
                var result = login.CreateResult(BrowserLoginPlatform.Bilibili, await cookies.GetCookiesAsync());
                if (!ReferenceEquals(current, browser))
                {
                    return;
                }
                await model.SaveCookieAsync(browserAccount, result.Cookie);
                ReleaseBrowser();
            }
            catch (Exception exception)
            {
                model.Shell.ReportError(exception);
                model.BrowserStatus = model.Shell.Error;
                throw;
            }
        });
    }

    private void CloseBrowser_OnClick(object? sender, RoutedEventArgs args)
    {
        ReleaseBrowser();
    }

    private void BrowserBack_OnClick(object? sender, RoutedEventArgs args)
    {
        browser?.GoBack();
    }

    private void BrowserRefresh_OnClick(object? sender, RoutedEventArgs args)
    {
        browser?.Refresh();
    }
}
