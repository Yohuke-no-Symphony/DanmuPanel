using Avalonia.Headless.XUnit;
using Yohuke.DanmuPanel.iPad.Services;
using Yohuke.DanmuPanel.iPad.ViewModels;
using Yohuke.DanmuPanel.Models.State;
using Yohuke.DanmuPanel.Services;
using Xunit;

namespace Yohuke.DanmuPanel.iPad.Tests;

/// <summary>验证 iPad 房间选择与空工作区不会意外发起网络连接或生成模拟房间。</summary>
public sealed class IpadShellViewModelTests
{
    /// <summary>空工作区不包含模拟房间，添加真实房间后启用监听入口。</summary>
    [AvaloniaFact]
    public async Task EmptyWorkspaceRequiresAConfiguredRoomBeforeListening()
    {
        using var platform = new TestIpadPlatform();
        var configuration = new PanelConfiguration();
        configuration.Workspaces.Add(new() { Name = "工作区" });
        await using var runtime = new PanelRuntime(new(platform.DataDirectory), configuration);
        using var model = new IpadShellViewModel(runtime, platform, new(platform.DataDirectory), () => { }) { IsReady = true };
        Assert.False(model.HasLiveRoom);
        Assert.True(model.NeedsSetup);
        Assert.False(model.ToggleListeningCommand.CanExecute(null));
        Assert.Null(model.SelectedRoom);
        Assert.Empty(model.Rooms);
        Assert.Empty(configuration.Workspaces[0].Rooms);
        var room = new RoomTabState { RoomId = "42", Name = "真实直播间" };
        configuration.Workspaces[0].Rooms.Add(room);
        model.RefreshRooms();
        Assert.Same(room, Assert.Single(model.Rooms));
        Assert.Same(room, model.SelectedRoom);
        Assert.True(model.HasLiveRoom);
        Assert.False(model.NeedsSetup);
        Assert.True(model.ToggleListeningCommand.CanExecute(null));
        Assert.False(runtime.IsListening(room));
        await runtime.ConfigurationStore.SaveAsync(configuration);
    }

    /// <summary>工作区切换恢复各自选择，房间删除后清空选择并关闭监听入口。</summary>
    [AvaloniaFact]
    public async Task WorkspaceSelectionRestoresRoomAndHandlesRemoval()
    {
        using var platform = new TestIpadPlatform();
        var firstRoom = new RoomTabState { Id = "one", RoomId = "1", Name = "第一个房间" };
        var secondRoom = new RoomTabState { Id = "two", RoomId = "2", Name = "第二个房间" };
        var first = new WorkspaceState { Id = "first", Name = "第一个工作区", SelectedRoomId = firstRoom.Id, Rooms = [firstRoom] };
        var second = new WorkspaceState { Id = "second", Name = "第二个工作区", SelectedRoomId = secondRoom.Id, Rooms = [secondRoom] };
        var configuration = new PanelConfiguration { Workspaces = [first, second] };
        configuration.Settings.SelectedWorkspaceId = second.Id;
        await using var runtime = new PanelRuntime(new(platform.DataDirectory), configuration);
        using var model = new IpadShellViewModel(runtime, platform, new(platform.DataDirectory), () => { }) { IsReady = true };
        Assert.Same(secondRoom, model.SelectedRoom);
        model.SelectedWorkspace = first;
        Assert.Same(firstRoom, model.SelectedRoom);
        Assert.True(model.ToggleListeningCommand.CanExecute(null));
        first.Rooms.Clear();
        model.RefreshRooms();
        Assert.Null(model.SelectedRoom);
        Assert.Empty(model.Rooms);
        Assert.False(model.HasLiveRoom);
        Assert.False(model.ToggleListeningCommand.CanExecute(null));
        model.SelectedWorkspace = second;
        Assert.Same(secondRoom, model.SelectedRoom);
        await runtime.ConfigurationStore.SaveAsync(configuration);
        Assert.Equal(second.Id, new ConfigurationService(platform.DataDirectory).Load().Settings.SelectedWorkspaceId);
    }
}
