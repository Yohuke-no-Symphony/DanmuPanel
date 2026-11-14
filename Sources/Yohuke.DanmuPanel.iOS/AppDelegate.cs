using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.iOS;
using Foundation;
using UIKit;
using IpadApp = Yohuke.DanmuPanel.iPad.App;

namespace Yohuke.DanmuPanel.iOS;

/// <summary>初始化跨平台应用，并将窗口创建交由 iPad 场景生命周期。</summary>
[Register("DanmuPanelAppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate, IAvaloniaAppDelegate
{
    private event EventHandler<ActivatedEventArgs>? activated;
    private event EventHandler<ActivatedEventArgs>? deactivated;

    internal IosPlatform Platform { get; } = new();
    internal IpadApp IpadApplication { get; private set; } = null!;

    event EventHandler<ActivatedEventArgs> IAvaloniaAppDelegate.Activated
    {
        add => activated += value;
        remove => activated -= value;
    }

    event EventHandler<ActivatedEventArgs> IAvaloniaAppDelegate.Deactivated
    {
        add => deactivated += value;
        remove => deactivated -= value;
    }

    /// <inheritdoc />
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        AppBuilder.Configure(() => new IpadApp(Platform))
            .UseiOS(this)
            .WithInterFont()
            .LogToTrace()
            .SetupWithoutStarting();
        IpadApplication = (IpadApp)Application.Current!;
        return true;
    }

    internal void NotifyActivated()
    {
        Platform.SetActive(true);
        activated?.Invoke(this, new(ActivationKind.Background));
    }

    internal void NotifyDeactivated()
    {
        Platform.SetActive(false);
        deactivated?.Invoke(this, new(ActivationKind.Background));
    }
}
