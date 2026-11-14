using Avalonia.iOS;
using Foundation;
using UIKit;

namespace Yohuke.DanmuPanel.iOS;

/// <summary>为单个 iPad 场景创建 Avalonia 容器，并转发激活和挂起事件。</summary>
[Register("DanmuPanelSceneDelegate")]
public sealed class SceneDelegate : UIWindowSceneDelegate
{
    private AppDelegate appDelegate = null!;
    private AvaloniaView? nativeView;

    /// <inheritdoc />
    public override UIWindow? Window { get; set; }

    /// <inheritdoc />
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        appDelegate = (AppDelegate)UIApplication.SharedApplication.Delegate;
        var view = new AvaloniaView();
        var controller = new DefaultAvaloniaViewController
        {
            View = view
        };
        view.InitWithController(controller);
        view.Content = appDelegate.IpadApplication.RootView;
        nativeView = view;
        Window = new((UIWindowScene)scene)
        {
            RootViewController = controller
        };
        Window.MakeKeyAndVisible();
    }

    /// <inheritdoc />
    public override async void DidBecomeActive(UIScene scene)
    {
        appDelegate.NotifyActivated();
        try
        {
            await appDelegate.IpadApplication.ResumeAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unable to resume Danmu Panel: {exception}");
        }
    }

    /// <inheritdoc />
    public override void WillResignActive(UIScene scene)
    {
        appDelegate.Platform.SetActive(false);
    }

    /// <inheritdoc />
    public override async void DidEnterBackground(UIScene scene)
    {
        appDelegate.NotifyDeactivated();
        var application = UIApplication.SharedApplication;
        var backgroundTask = UIApplication.BackgroundTaskInvalid;
        backgroundTask = application.BeginBackgroundTask("SaveDanmuPanelState", () =>
        {
            var task = backgroundTask;
            backgroundTask = UIApplication.BackgroundTaskInvalid;
            if (task != UIApplication.BackgroundTaskInvalid)
            {
                application.EndBackgroundTask(task);
            }
        });
        try
        {
            await appDelegate.IpadApplication.SuspendAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unable to suspend Danmu Panel: {exception}");
        }
        finally
        {
            var task = backgroundTask;
            backgroundTask = UIApplication.BackgroundTaskInvalid;
            if (task != UIApplication.BackgroundTaskInvalid)
            {
                application.EndBackgroundTask(task);
            }
        }
    }

    /// <inheritdoc />
    public override void DidDisconnect(UIScene scene)
    {
        appDelegate.NotifyDeactivated();
        if (nativeView is not null)
        {
            nativeView.Content = null;
            nativeView.Dispose();
            nativeView = null;
        }
        Window?.Dispose();
        Window = null;
    }
}
