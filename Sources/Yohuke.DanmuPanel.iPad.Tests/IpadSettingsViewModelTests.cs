using System.Text;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuPanel.iPad.Services;
using Yohuke.DanmuPanel.iPad.ViewModels;
using Yohuke.DanmuPanel.Models.State;
using Yohuke.DanmuPanel.Services;
using Yohuke.DanmuPanel.Services.Settings;
using Xunit;

namespace Yohuke.DanmuPanel.iPad.Tests;

/// <summary>验证移动设置的草稿隔离、保存及分享文件对本地配置的影响。</summary>
public sealed class IpadSettingsViewModelTests
{
    /// <summary>非法输入不会改变运行配置或常亮偏好，修正后保存并重新加载可恢复全部修改。</summary>
    [AvaloniaFact]
    public async Task SettingsStayIsolatedUntilValidatedAndPersisted()
    {
        using var platform = new TestIpadPlatform();
        var configuration = new PanelConfiguration();
        var store = new ConfigurationService(platform.DataDirectory);
        await using var runtime = new PanelRuntime(store, configuration);
        await runtime.InitializeAsync();
        var applied = 0;
        var preferences = new IpadPreferencesStore(platform.DataDirectory);
        using var shell = new IpadShellViewModel(runtime, platform, preferences, () => applied++);
        using var model = new IpadSettingsViewModel(shell);
        var originalScale = configuration.Settings.MessageTextScale;
        var originalAvatars = configuration.Settings.ShowUserAvatars;
        model.Reading.MessageTextScale = 250;
        model.Reading.ShowUserAvatars = !originalAvatars;
        model.KeepScreenOn = false;
        Assert.Equal(originalScale, configuration.Settings.MessageTextScale);
        Assert.Equal(originalAvatars, configuration.Settings.ShowUserAvatars);
        await Assert.ThrowsAsync<InvalidDataException>(model.SaveAsync);
        Assert.Equal(originalScale, configuration.Settings.MessageTextScale);
        Assert.Equal(originalAvatars, configuration.Settings.ShowUserAvatars);
        Assert.True(preferences.LoadKeepScreenOn());
        Assert.Equal(0, applied);
        model.Reading.MessageTextScale = 140;
        await model.SaveAsync();
        var reloaded = store.Load();
        Assert.Equal(1.4, reloaded.Settings.MessageTextScale);
        Assert.Equal(!originalAvatars, reloaded.Settings.ShowUserAvatars);
        Assert.False(shell.KeepScreenOn);
        Assert.False(preferences.LoadKeepScreenOn());
        Assert.Equal(1, applied);
    }

    /// <summary>移动端导入使用共享格式且保留本地账号、房间和工作区选择。</summary>
    [AvaloniaFact]
    public async Task ImportPreservesLocalBindingsAndRejectsMalformedFiles()
    {
        using var platform = new TestIpadPlatform();
        var room = new RoomTabState { Id = "local-room", RoomId = "42", Name = "本机直播间" };
        var workspace = new WorkspaceState { Id = "local-workspace", Rooms = [room], SelectedRoomId = room.Id };
        var configuration = new PanelConfiguration { Workspaces = [workspace] };
        configuration.Settings.DefaultAccountId = "local-account";
        configuration.Settings.SelectedWorkspaceId = workspace.Id;
        var store = new ConfigurationService(platform.DataDirectory);
        await using var runtime = new PanelRuntime(store, configuration);
        await runtime.InitializeAsync();
        using var shell = new IpadShellViewModel(runtime, platform, new(platform.DataDirectory), () => { });
        using var model = new IpadSettingsViewModel(shell);
        var imported = new PanelSettings { MessageTextScale = 1.6, DefaultAccountId = "other-account", SelectedWorkspaceId = "other-workspace" };
        using var source = new MemoryStream();
        await new SettingsShareService().ExportAsync(imported, source);
        source.Position = 0;
        await model.ImportAsync(source);
        Assert.Equal(1.6, configuration.Settings.MessageTextScale);
        Assert.Equal("local-account", configuration.Settings.DefaultAccountId);
        Assert.Equal(workspace.Id, configuration.Settings.SelectedWorkspaceId);
        Assert.Same(room, shell.SelectedRoom);
        Assert.Same(workspace, Assert.Single(configuration.Workspaces));
        using var malformed = new MemoryStream(Encoding.UTF8.GetBytes("{invalid}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => model.ImportAsync(malformed));
        Assert.Equal(1.6, configuration.Settings.MessageTextScale);
        Assert.Equal("local-account", store.Load().Settings.DefaultAccountId);
        using var exported = new MemoryStream();
        await model.ExportAsync(exported);
        exported.Position = 0;
        var roundTrip = await new SettingsShareService().ImportAsync(exported);
        Assert.Equal(1.6, roundTrip.MessageTextScale);
        Assert.Null(roundTrip.DefaultAccountId);
        Assert.Null(roundTrip.SelectedWorkspaceId);
    }
}
