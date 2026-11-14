using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Yohuke.DanmuPanel.I18n;

[assembly: AvaloniaTestApplication(typeof(Yohuke.DanmuPanel.iPad.Tests.IpadTestApplication))]

namespace Yohuke.DanmuPanel.iPad.Tests;

/// <summary>只加载共享组件与 iPad 主题的无桌面依赖测试宿主。</summary>
public sealed class IpadTestApplication : Application
{
    /// <summary>为触控页面测试创建可绘制的无头应用。</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<IpadTestApplication>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <inheritdoc />
    public override void Initialize()
    {
        if (!Localization.IsInitialized)
        {
            Localization.Initialize(Path.Combine(AppContext.BaseDirectory, "i18n"));
        }
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Yohuke.DanmuPanel.Shared/"))
        {
            Source = new("avares://Yohuke.DanmuPanel.Shared/Views/Styles/PanelTheme.axaml")
        });
        Styles.Add(new StyleInclude(new Uri("avares://Yohuke.DanmuPanel.iPad/"))
        {
            Source = new("avares://Yohuke.DanmuPanel.iPad/Styles/IpadTheme.axaml")
        });
    }
}
