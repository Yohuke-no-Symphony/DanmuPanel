using Avalonia.Headless.XUnit;
using Xunit;
using Yohuke.DanmuCore.Apis.Models.Bilibili;
using Yohuke.DanmuPanel.Models.Messages;
using Yohuke.DanmuPanel.Models.State;
using Yohuke.DanmuPanel.Models.Translation;
using Yohuke.DanmuPanel.Services;
using Yohuke.DanmuPanel.Services.Translation;
using Yohuke.DanmuPanel.Tests.Messages;
using Yohuke.DanmuPanel.Tests.Translation;

namespace Yohuke.DanmuPanel.Tests.Integration;

/// <summary>验证移动前后台切换保留监听意图、持久任务和运行时身份。</summary>
public sealed class PanelRuntimeLifecycleTests
{
    private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(5);

    /// <summary>首次前台通知及重复初始化不会重启已运行的监听或调度器。</summary>
    [AvaloniaFact]
    public async Task InitialResumeAndRepeatedInitializeDoNotRestartListeners()
    {
        using var directory = new MessageStoreTestDirectory();
        var sockets = new LifecycleSocketFactory();
        var configuration = Configuration("101");
        await using var runtime = CreateRuntime(directory, configuration, sockets);
        var workspace = configuration.Workspaces[0];
        var room = workspace.Rooms[0];

        await runtime.InitializeAsync().WaitAsync(TIMEOUT);
        await runtime.ToggleListeningAsync(workspace, room).WaitAsync(TIMEOUT);
        await Task.WhenAll(runtime.ResumeAsync(), runtime.ResumeAsync(), runtime.InitializeAsync()).WaitAsync(TIMEOUT);

        Assert.True(runtime.IsListening(room));
        Assert.Equal(1, sockets.GetSocket(101).Starts);
        Assert.Equal(0, sockets.GetSocket(101).Stops);
    }

    /// <summary>暂停排空已接收消息并保存设置，恢复不会重新连接已移除或明确停止的房间。</summary>
    [AvaloniaFact]
    public async Task SuspendDrainsMessagesAndResumeSkipsRemovedAndStoppedRooms()
    {
        using var directory = new MessageStoreTestDirectory();
        var sockets = new LifecycleSocketFactory();
        var configuration = Configuration("101", "102", "103");
        await using var runtime = CreateRuntime(directory, configuration, sockets);
        var workspace = configuration.Workspaces[0];
        var rooms = workspace.Rooms.ToArray();
        await runtime.InitializeAsync();
        foreach (var room in rooms)
        {
            await runtime.ToggleListeningAsync(workspace, room);
        }
        sockets.GetSocket(103).Emit(new BilibiliDanmuMessage
        {
            RoomId = "103", MessageId = "before-suspend", UserName = "viewer", Uid = 1, Content = "排空消息"
        });

        await Task.WhenAll(runtime.SuspendAsync(), runtime.SuspendAsync()).WaitAsync(TIMEOUT);
        Assert.All(rooms, room => Assert.False(runtime.IsListening(room)));
        Assert.Single(await runtime.Messages.LoadPageAsync(rooms[2].Id, MessageKind.Danmu, 0, 10));
        Assert.Equal(configuration.Settings.Translation.Enabled, runtime.ConfigurationStore.Load().Settings.Translation.Enabled);
        workspace.Rooms.Remove(rooms[0]);
        await runtime.StopRoomAsync(rooms[1]);

        await Task.WhenAll(runtime.ResumeAsync(), runtime.ResumeAsync()).WaitAsync(TIMEOUT);

        Assert.False(runtime.IsListening(rooms[0]));
        Assert.False(runtime.IsListening(rooms[1]));
        Assert.True(runtime.IsListening(rooms[2]));
        Assert.Equal(1, sockets.GetSocket(101).Starts);
        Assert.Equal(1, sockets.GetSocket(102).Starts);
        Assert.Equal(2, sockets.GetSocket(103).Starts);
    }

    /// <summary>恢复一个房间失败仍恢复其他房间，失败意图在下一次前台恢复时可重试。</summary>
    [AvaloniaFact]
    public async Task FailedResumeDoesNotBlockOtherRoomsAndKeepsFailedIntent()
    {
        using var directory = new MessageStoreTestDirectory();
        var sockets = new LifecycleSocketFactory();
        var configuration = Configuration("101", "102");
        await using var runtime = CreateRuntime(directory, configuration, sockets);
        var workspace = configuration.Workspaces[0];
        await runtime.InitializeAsync();
        foreach (var room in workspace.Rooms)
        {
            await runtime.ToggleListeningAsync(workspace, room);
        }
        await runtime.SuspendAsync();
        sockets.FailingRooms.Add(101);

        await Assert.ThrowsAsync<AggregateException>(() => runtime.ResumeAsync().WaitAsync(TIMEOUT));

        Assert.False(runtime.IsListening(workspace.Rooms[0]));
        Assert.True(runtime.IsListening(workspace.Rooms[1]));
        Assert.Equal("Panel.ListenerFailed", runtime.GetStatus(workspace.Rooms[0]));
        await runtime.SuspendAsync();
        sockets.FailingRooms.Clear();
        await runtime.ResumeAsync().WaitAsync(TIMEOUT);
        Assert.All(workspace.Rooms, room => Assert.True(runtime.IsListening(room)));
        Assert.Equal(2, sockets.GetSocket(101).Starts);
        Assert.Equal(3, sockets.GetSocket(102).Starts);
    }

    /// <summary>首启或前台恢复失败后，界面显示开始时单次点击就重试真实连接。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedListeningIntentRetriesWithOneActiveToggle(bool failedDuringResume)
    {
        using var directory = new MessageStoreTestDirectory();
        var sockets = new LifecycleSocketFactory();
        var configuration = Configuration("101");
        await using var runtime = CreateRuntime(directory, configuration, sockets);
        var workspace = configuration.Workspaces[0];
        var room = workspace.Rooms[0];
        await runtime.InitializeAsync().WaitAsync(TIMEOUT);
        if (failedDuringResume)
        {
            await runtime.ToggleListeningAsync(workspace, room).WaitAsync(TIMEOUT);
            await runtime.SuspendAsync().WaitAsync(TIMEOUT);
            sockets.FailingRooms.Add(101);
            await Assert.ThrowsAsync<AggregateException>(() => runtime.ResumeAsync().WaitAsync(TIMEOUT));
        }
        else
        {
            sockets.FailingRooms.Add(101);
            await Assert.ThrowsAsync<IOException>(() => runtime.ToggleListeningAsync(workspace, room).WaitAsync(TIMEOUT));
        }
        Assert.False(runtime.IsListening(room));
        var previousStarts = sockets.GetSocket(101).Starts;
        sockets.FailingRooms.Clear();

        await runtime.ToggleListeningAsync(workspace, room).WaitAsync(TIMEOUT);

        Assert.True(runtime.IsListening(room));
        Assert.Equal(previousStarts + 1, sockets.GetSocket(101).Starts);
        await runtime.ResumeAsync().WaitAsync(TIMEOUT);
        Assert.Equal(previousStarts + 1, sockets.GetSocket(101).Starts);
    }

    /// <summary>暂停期间切换只改变恢复意图并通知界面，再次切换取消该意图。</summary>
    [AvaloniaFact]
    public async Task SuspendedToggleNotifiesAndSecondToggleCancelsListeningIntent()
    {
        using var directory = new MessageStoreTestDirectory();
        var sockets = new LifecycleSocketFactory();
        var configuration = Configuration("101");
        await using var runtime = CreateRuntime(directory, configuration, sockets);
        var workspace = configuration.Workspaces[0];
        var room = workspace.Rooms[0];
        var changes = new List<string>();
        await runtime.InitializeAsync().WaitAsync(TIMEOUT);
        await runtime.SuspendAsync().WaitAsync(TIMEOUT);
        runtime.Changed += changes.Add;

        await runtime.ToggleListeningAsync(workspace, room).WaitAsync(TIMEOUT);

        Assert.Equal(new[] { room.Id }, changes);
        Assert.False(runtime.IsListening(room));
        Assert.Equal(0, sockets.GetSocket(101).Starts);
        await runtime.ToggleListeningAsync(workspace, room).WaitAsync(TIMEOUT);
        Assert.Equal(new[] { room.Id, room.Id }, changes);
        await runtime.ResumeAsync().WaitAsync(TIMEOUT);
        Assert.False(runtime.IsListening(room));
        Assert.Equal(0, sockets.GetSocket(101).Starts);
    }

    /// <summary>前后台切换与释放并发时不重复清理，也不会在释放后重建连接。</summary>
    [AvaloniaFact]
    public async Task ConcurrentLifecycleAndDisposeNeverResurrectListeners()
    {
        using var directory = new MessageStoreTestDirectory();
        var sockets = new LifecycleSocketFactory();
        var configuration = Configuration("101");
        var runtime = CreateRuntime(directory, configuration, sockets);
        try
        {
            await runtime.InitializeAsync();
            await runtime.ToggleListeningAsync(configuration.Workspaces[0], configuration.Workspaces[0].Rooms[0]);
            var suspend = runtime.SuspendAsync();
            var resume = runtime.ResumeAsync();
            var dispose = runtime.DisposeAsync().AsTask();
            await Task.WhenAll(suspend, resume, dispose, runtime.DisposeAsync().AsTask()).WaitAsync(TIMEOUT);
            var starts = sockets.GetSocket(101).Starts;

            await Task.WhenAll(runtime.SuspendAsync(), runtime.ResumeAsync()).WaitAsync(TIMEOUT);

            Assert.Equal(starts, sockets.GetSocket(101).Starts);
            Assert.Equal(starts, sockets.GetSocket(101).Stops);
            Assert.False(runtime.IsListening(configuration.Workspaces[0].Rooms[0]));
        }
        finally
        {
            await runtime.DisposeAsync();
        }
    }

    /// <summary>暂停取消在途翻译且保留 SQLite 待办，恢复后原运行时完成同一条消息。</summary>
    [AvaloniaFact]
    public async Task SuspendPreservesTranslationQueueAndEnabledSetting()
    {
        using var directory = new MessageStoreTestDirectory();
        var configuration = Configuration("101");
        configuration.Settings.Translation = new() { Enabled = true, Model = "test", BacklogMode = TranslationBacklogMode.AllMessages };
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var translated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var client = new DelegateTranslationClient(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                finally
                {
                    cancelled.TrySetResult();
                }
            }
            return "译文";
        });
        await using var runtime = new PanelRuntime(new(directory.Path), configuration, new LifecycleSocketFactory(),
            new ReceptionTestHistory(), null, translationClient: client);
        var originalStore = runtime.Messages;
        await runtime.InitializeAsync();
        var message = new MessageRecord { RoomTabId = "tab", RoomId = "101", Kind = MessageKind.Danmu, Content = "hello" };
        await runtime.Messages.AppendAsync(message, new(), TranslationRequestFactory.GetConfigurationFingerprint(configuration.Settings.Translation));
        runtime.NotifyTranslation();
        await firstStarted.Task.WaitAsync(TIMEOUT);

        await runtime.SuspendAsync().WaitAsync(TIMEOUT);
        await cancelled.Task.WaitAsync(TIMEOUT);

        Assert.True(configuration.Settings.Translation.Enabled);
        Assert.True(runtime.ConfigurationStore.Load().Settings.Translation.Enabled);
        Assert.Single(await runtime.Messages.GetPendingAsync(10));
        Assert.Null((await runtime.Messages.GetByIdAsync(message.Id))!.Translation);
        runtime.MessagesChanged += (_, _) => translated.TrySetResult();
        await runtime.ResumeAsync().WaitAsync(TIMEOUT);
        await translated.Task.WaitAsync(TIMEOUT);

        Assert.Same(originalStore, runtime.Messages);
        Assert.Equal("译文", (await runtime.Messages.GetByIdAsync(message.Id))!.Translation);
        Assert.Empty(await runtime.Messages.GetPendingAsync(10));
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    private static PanelRuntime CreateRuntime(MessageStoreTestDirectory directory, PanelConfiguration configuration,
        LifecycleSocketFactory sockets)
    {
        return new(new(directory.Path), configuration, sockets, new ReceptionTestHistory(), null);
    }

    private static PanelConfiguration Configuration(params string[] roomIds)
    {
        var workspace = new WorkspaceState { Name = "Lifecycle" };
        foreach (var roomId in roomIds)
        {
            workspace.Rooms.Add(new() { RoomId = roomId, Name = roomId });
        }
        return new() { Workspaces = [workspace] };
    }
}
