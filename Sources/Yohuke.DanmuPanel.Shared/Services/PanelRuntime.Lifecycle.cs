using Yohuke.DanmuPanel.I18n;

namespace Yohuke.DanmuPanel.Services;

public sealed partial class PanelRuntime
{
    /// <summary>暂停翻译与房间监听，排空已接收消息并保存状态，保留恢复意图。</summary>
    public async Task SuspendAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (disposed || suspended)
            {
                return;
            }
            suspended = true;
            var failures = new List<Exception>();
            await CaptureFailureAsync(() => scheduler.PauseAsync(), failures);
            foreach (var id in sessions.Keys.ToArray())
            {
                await CaptureFailureAsync(() => StopAsync(id), failures);
            }
            await CaptureFailureAsync(() => ConfigurationStore.SaveAsync(Configuration), failures);
            if (failures.Count > 0)
            {
                throw new AggregateException("The panel could not complete every suspension operation.", failures);
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    /// <summary>恢复翻译调度及仍然存在的房间监听；已恢复或已释放时不重复启动。</summary>
    public async Task ResumeAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (disposed || !suspended)
            {
                return;
            }
            await scheduler.ResumeAsync();
            suspended = false;
            var failures = new List<Exception>();
            foreach (var (roomId, workspaceId) in listeningIntent.ToArray())
            {
                var workspace = Configuration.Workspaces.FirstOrDefault(value => value.Id == workspaceId);
                var room = workspace?.Rooms.FirstOrDefault(value => value.Id == roomId);
                if (workspace is null || room is null)
                {
                    listeningIntent.Remove(roomId);
                    continue;
                }
                if (sessions.ContainsKey(roomId))
                {
                    continue;
                }
                try
                {
                    await StartAsync(workspace, room);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                    statuses[roomId] = "Panel.ListenerFailed";
                    ReportError(error, Localization.Format("Panel.Log.Reception", room.Name, room.RoomId));
                    Changed?.Invoke(roomId);
                }
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("Some room listeners could not be resumed.", failures);
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            suspended = true;
            listeningIntent.Clear();
            var failures = new List<Exception>();
            await CaptureFailureAsync(() => scheduler.PauseAsync(), failures);
            foreach (var id in sessions.Keys.ToArray())
            {
                await CaptureFailureAsync(() => StopAsync(id), failures);
            }
            await CaptureFailureAsync(() => scheduler.DisposeAsync().AsTask(), failures);
            await CaptureFailureAsync(() => Presentations.DisposeAsync().AsTask(), failures);
            await CaptureFailureAsync(() => translationService.DisposeAsync().AsTask(), failures);
            await CaptureFailureAsync(() => translationCache.DisposeAsync().AsTask(), failures);
            await CaptureFailureAsync(() => ConfigurationStore.SaveAsync(Configuration), failures);
            Emoticons.Dispose();
            await CaptureFailureAsync(() => UserAvatars.DisposeAsync().AsTask(), failures);
            Avatars.Dispose();
            Colors.Dispose();
            Configuration.Accounts.CollectionChanged -= OnAccountsChanged;
            accountBinding.Dispose();
            AccountSessions.Dispose();
            await CaptureFailureAsync(() => Messages.DisposeAsync().AsTask(), failures);
            translationHttp.Dispose();
            if (failures.Count > 0)
            {
                throw new AggregateException("The panel could not complete every disposal operation.", failures);
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    private static async Task CaptureFailureAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        {
            await operation();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
    }
}
