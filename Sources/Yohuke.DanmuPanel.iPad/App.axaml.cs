using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Yohuke.DanmuPanel.I18n;
using Yohuke.DanmuPanel.iPad.Services;
using Yohuke.DanmuPanel.iPad.ViewModels;
using Yohuke.DanmuPanel.iPad.Views;
using Yohuke.DanmuPanel.Services;

namespace Yohuke.DanmuPanel.iPad;

/// <summary>组装共享业务与 iPad 独立页面，并接收平台前后台生命周期。</summary>
public sealed partial class App : Application
{
    private readonly IIpadPlatform platform;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private PanelRuntime runtime = null!;
    private IpadShellViewModel model = null!;
    private bool foreground = true;

    public IpadShellView RootView { get; private set; } = null!;
    public Task Initialization { get; private set; } = Task.CompletedTask;

    /// <summary>由 iOS 宿主显式提供系统能力。</summary>
    public App(IIpadPlatform platform)
    {
        this.platform = platform;
    }

    /// <inheritdoc />
    public override void Initialize()
    {
        if (!Localization.IsInitialized)
        {
            Localization.Initialize(Path.Combine(AppContext.BaseDirectory, "i18n"));
        }
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        var store = new ConfigurationService(platform.DataDirectory);
        var configuration = store.Load();
        if (configuration.Workspaces.Count == 0)
        {
            configuration.Workspaces.Add(new()
            {
                Name = Localization.Get("Panel.DefaultWorkspace")
            });
        }
        runtime = new(store, configuration);
        model = new(runtime, platform, new(platform.DataDirectory), ApplyAppearance);
        RootView = new(model);
        ApplyAppearance();
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = RootView;
        }
        Initialization = InitializeRuntimeAsync();
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>保存阅读状态、停止前台活动并恢复系统自动锁屏。</summary>
    public async Task SuspendAsync()
    {
        foreground = false;
        platform.SetKeepScreenOn(false);
        await Initialization;
        await lifecycle.WaitAsync();
        try
        {
            if (foreground)
            {
                return;
            }
            RootView.CancelConfirmation();
            RootView.CloseTransientPage();
            await runtime.SuspendAsync();
        }
        catch (Exception exception)
        {
            model.ReportError(exception);
            throw;
        }
        finally
        {
            lifecycle.Release();
        }
    }

    /// <summary>返回前台后恢复用户先前开启的监听和阅读偏好。</summary>
    public async Task ResumeAsync()
    {
        foreground = true;
        await Initialization;
        await lifecycle.WaitAsync();
        try
        {
            if (!model.IsReady || !foreground)
            {
                return;
            }
            await runtime.ResumeAsync();
            await RootView.RefreshFeedsAsync();
            platform.SetKeepScreenOn(foreground && model.KeepScreenOn);
            model.RefreshStatus();
        }
        catch (Exception exception)
        {
            model.ReportError(exception);
            throw;
        }
        finally
        {
            lifecycle.Release();
        }
    }

    private async Task InitializeRuntimeAsync()
    {
        try
        {
            await runtime.InitializeAsync();
            await RootView.RefreshFeedsAsync();
            model.IsReady = true;
            platform.SetKeepScreenOn(foreground && model.KeepScreenOn);
        }
        catch (Exception exception)
        {
            model.ReportError(exception);
        }
    }

    private void ApplyAppearance()
    {
        Localization.SetLanguage(runtime.Configuration.Settings.Language);
        RequestedThemeVariant = runtime.Configuration.Settings.Theme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        runtime.Colors.Apply(this, runtime.Configuration.Settings.ColorScheme);
        platform.SetKeepScreenOn(foreground && model.KeepScreenOn);
    }
}
