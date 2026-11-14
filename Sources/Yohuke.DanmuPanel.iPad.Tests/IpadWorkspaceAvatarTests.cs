using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuPanel.iPad.ViewModels;
using Yohuke.DanmuPanel.Models.State;
using Yohuke.DanmuPanel.Models.Workspace;
using Yohuke.DanmuPanel.Services;
using Xunit;

namespace Yohuke.DanmuPanel.iPad.Tests;

/// <summary>验证移动侧栏的主播头像加载、缓存恢复与房间生命周期。</summary>
public sealed class IpadWorkspaceAvatarTests
{
    private static readonly TimeSpan operationTimeout = TimeSpan.FromSeconds(10);

    /// <summary>首次加载使用房间账号并保存主播身份，重复刷新不重复请求，重新打开立即使用磁盘缓存。</summary>
    [AvaloniaFact]
    public async Task FirstLoadPersistsIdentityAndReopenedSidebarUsesCache()
    {
        using var platform = new TestIpadPlatform();
        var imagePath = WriteImage(platform.DataDirectory, "avatar.png");
        var globalAccount = new BilibiliAccount { Cookie = "global-cookie" };
        var workspaceAccount = new BilibiliAccount { Cookie = "workspace-cookie" };
        var roomAccount = new BilibiliAccount { Cookie = "room-cookie" };
        var room = new RoomTabState { RoomId = "123", Name = "123", AccountOverrideId = roomAccount.Id };
        var workspace = new WorkspaceState { Rooms = [room], AccountOverrideId = workspaceAccount.Id };
        var configuration = new PanelConfiguration
        {
            Accounts = [globalAccount, workspaceAccount, roomAccount], Workspaces = [workspace]
        };
        configuration.Settings.DefaultAccountId = globalAccount.Id;
        var store = new ConfigurationService(platform.DataDirectory);
        await using (var runtime = new PanelRuntime(store, configuration))
        {
            using var shell = new IpadShellViewModel(runtime, platform, new(platform.DataDirectory), () => { });
            var completion = new TaskCompletionSource<RoomAvatarResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var avatars = new TestRoomAvatarService { Resolve = (_, _, _, _) => completion.Task };
            using var sidebar = new IpadWorkspaceSidebarViewModel(shell, avatars);
            var node = Assert.Single(Assert.Single(sidebar.Workspaces).Rooms);
            Assert.True(node.HasNoAvatar);
            var request = Assert.Single(avatars.Requests);
            Assert.Equal("123", request.RoomId);
            Assert.Equal("", request.OwnerUid);
            Assert.Equal(roomAccount.Cookie, request.Cookie);
            shell.RefreshRooms();
            shell.RefreshStatus();
            Assert.Single(avatars.Requests);

            completion.SetResult(new() { OwnerUid = "456", OwnerName = "主播", AvatarPath = imagePath });
            await WaitUntilAsync(() => store.Load().Workspaces.FirstOrDefault()?.Rooms.FirstOrDefault()?.AvatarPath == imagePath);
            Assert.True(node.HasAvatar);
            Assert.Equal("456", room.OwnerUid);
            Assert.Equal("主播", room.Name);
            shell.RefreshRooms();
            Assert.Single(avatars.Requests);
            Assert.Same(node, Assert.Single(Assert.Single(sidebar.Workspaces).Rooms));
            Assert.False(shell.HasError);
        }

        var restored = store.Load();
        Assert.Equal("456", Assert.Single(Assert.Single(restored.Workspaces).Rooms).OwnerUid);
        await using var restoredRuntime = new PanelRuntime(store, restored);
        using var restoredShell = new IpadShellViewModel(restoredRuntime, platform, new(platform.DataDirectory), () => { });
        var unavailableAvatars = new TestRoomAvatarService();
        using var reopened = new IpadWorkspaceSidebarViewModel(restoredShell, unavailableAvatars);
        var cachedNode = Assert.Single(Assert.Single(reopened.Workspaces).Rooms);
        Assert.True(cachedNode.HasAvatar);
        Assert.Equal(imagePath, cachedNode.State.AvatarPath);
        Assert.Equal("456", Assert.Single(unavailableAvatars.Requests).OwnerUid);
        restoredShell.RefreshRooms();
        Assert.Single(unavailableAvatars.Requests);
        Assert.True(cachedNode.HasAvatar);
        Assert.False(restoredShell.HasError);
        await store.SaveAsync(restored).WaitAsync(operationTimeout);
    }

    /// <summary>房间号或主播身份改变时取消旧请求，清除旧图片，并拒绝旧请求的迟到结果。</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RoomIdentityChangeCancelsPreviousAvatar(bool changeRoomId)
    {
        using var platform = new TestIpadPlatform();
        var oldPath = WriteImage(platform.DataDirectory, "old.png");
        var currentPath = WriteImage(platform.DataDirectory, "current.png");
        var room = new RoomTabState { RoomId = "123", OwnerUid = "1", AvatarPath = oldPath };
        var workspace = new WorkspaceState { Rooms = [room] };
        var configuration = new PanelConfiguration { Workspaces = [workspace] };
        var store = new ConfigurationService(platform.DataDirectory);
        await using var runtime = new PanelRuntime(store, configuration);
        using var shell = new IpadShellViewModel(runtime, platform, new(platform.DataDirectory), () => { });
        var previous = new TaskCompletionSource<RoomAvatarResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = new TaskCompletionSource<RoomAvatarResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var avatars = new TestRoomAvatarService
        {
            Resolve = (roomId, ownerUid, _, _) => roomId == "123" && ownerUid == "1" ? previous.Task : current.Task
        };
        using var sidebar = new IpadWorkspaceSidebarViewModel(shell, avatars);
        var oldNode = Assert.Single(Assert.Single(sidebar.Workspaces).Rooms);
        var previousLoad = oldNode.LoadAvatarAsync(avatars);
        Assert.True(oldNode.HasAvatar);
        if (changeRoomId)
        {
            room.RoomId = "124";
        }
        else
        {
            room.OwnerUid = "2";
        }
        shell.RefreshRooms();
        var currentNode = Assert.Single(Assert.Single(sidebar.Workspaces).Rooms);
        var currentLoad = currentNode.LoadAvatarAsync(avatars);
        Assert.NotSame(oldNode, currentNode);
        Assert.Equal(2, avatars.Requests.Count);
        Assert.True(avatars.Requests[0].CancellationToken.IsCancellationRequested);
        Assert.False(avatars.Requests[1].CancellationToken.IsCancellationRequested);
        Assert.False(oldNode.HasAvatar);
        Assert.True(currentNode.HasNoAvatar);
        Assert.Empty(room.AvatarPath);

        previous.SetResult(new() { OwnerUid = "1", AvatarPath = oldPath });
        Assert.False(await previousLoad.WaitAsync(operationTimeout));
        Assert.Empty(room.AvatarPath);
        var expectedOwner = changeRoomId ? "1" : "2";
        current.SetResult(new() { OwnerUid = expectedOwner, AvatarPath = currentPath });
        Assert.True(await currentLoad.WaitAsync(operationTimeout));
        await WaitUntilAsync(() => store.Load().Workspaces.FirstOrDefault()?.Rooms.FirstOrDefault()?.AvatarPath == currentPath);
        Assert.True(currentNode.HasAvatar);
        Assert.Equal(expectedOwner, room.OwnerUid);
        Assert.Equal(currentPath, room.AvatarPath);
        Assert.False(shell.HasError);
    }

    /// <summary>移除房间、移除工作区或释放侧栏都会取消请求，迟到头像不能再写回状态。</summary>
    [AvaloniaTheory]
    [InlineData("room")]
    [InlineData("workspace")]
    [InlineData("sidebar")]
    public async Task RemovedOrDisposedSidebarCancelsAvatarRequest(string removal)
    {
        using var platform = new TestIpadPlatform();
        var room = new RoomTabState { RoomId = "123" };
        var workspace = new WorkspaceState { Rooms = [room] };
        var configuration = new PanelConfiguration { Workspaces = [workspace] };
        var store = new ConfigurationService(platform.DataDirectory);
        await using var runtime = new PanelRuntime(store, configuration);
        using var shell = new IpadShellViewModel(runtime, platform, new(platform.DataDirectory), () => { });
        var completion = new TaskCompletionSource<RoomAvatarResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var avatars = new TestRoomAvatarService { Resolve = (_, _, _, _) => completion.Task };
        using var sidebar = new IpadWorkspaceSidebarViewModel(shell, avatars);
        var node = Assert.Single(Assert.Single(sidebar.Workspaces).Rooms);
        var loading = node.LoadAvatarAsync(avatars);
        switch (removal)
        {
            case "room":
                workspace.Rooms.Remove(room);
                break;
            case "workspace":
                configuration.Workspaces.Remove(workspace);
                break;
            default:
                sidebar.Dispose();
                break;
        }
        Assert.True(Assert.Single(avatars.Requests).CancellationToken.IsCancellationRequested);
        completion.SetResult(new() { OwnerUid = "456", AvatarPath = WriteImage(platform.DataDirectory, "late.png") });
        Assert.False(await loading.WaitAsync(operationTimeout));
        Assert.Empty(room.OwnerUid);
        Assert.Empty(room.AvatarPath);
        Assert.False(node.HasAvatar);
        Assert.Empty(sidebar.Workspaces.SelectMany(item => item.Rooms));
        Assert.False(shell.HasError);
        await store.SaveAsync(configuration).WaitAsync(operationTimeout);
    }

    private static string WriteImage(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAEUlEQVR4nGMI6LnzH4QZYAwAXRQK3dFXl4QAAAAASUVORK5CYII="));
        return path;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(operationTimeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
