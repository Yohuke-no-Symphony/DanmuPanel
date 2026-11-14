using Xunit;
using Yohuke.DanmuPanel.Models.Translation;
using Yohuke.DanmuPanel.Services.Translation;

namespace Yohuke.DanmuPanel.Tests.Translation;

/// <summary>验证移动宿主暂停翻译时的取消、待办保留和串行恢复。</summary>
public sealed class TranslationSchedulerLifecycleTests
{
    private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(5);

    /// <summary>重复暂停取消一次在途网络，恢复后仅重试原任务且不修改持久开关。</summary>
    [Fact]
    public async Task PausePreservesPendingJobAndConcurrentResumeRunsItOnce()
    {
        var store = new MemoryTranslationJobStore();
        store.Pending.Add(Job());
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var settings = Settings();
        await using var cache = new SqliteTranslationCache(":memory:");
        await using var service = new TranslationService(new DelegateTranslationClient(async (_, token) =>
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
        }), cache);
        await using var scheduler = new TranslationScheduler(store, service);
        scheduler.Completed += (_, _) => completed.TrySetResult();

        await scheduler.StartAsync(settings);
        await firstStarted.Task.WaitAsync(TIMEOUT);
        await Task.WhenAll(scheduler.PauseAsync(), scheduler.PauseAsync()).WaitAsync(TIMEOUT);
        await cancelled.Task.WaitAsync(TIMEOUT);

        Assert.True(settings.Enabled);
        Assert.Single(await store.GetPendingAsync(10));
        Assert.Empty(store.Results);
        Assert.Equal(1, Volatile.Read(ref calls));

        await Task.WhenAll(scheduler.ResumeAsync(), scheduler.ResumeAsync()).WaitAsync(TIMEOUT);
        await completed.Task.WaitAsync(TIMEOUT);

        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.Equal("译文", store.Results["message"]);
        Assert.Empty(await store.GetPendingAsync(10));
    }

    /// <summary>暂停期间的配置更新不能偷偷恢复网络，恢复时使用最新目标语言。</summary>
    [Fact]
    public async Task UpdatingSettingsWhilePausedWaitsForResumeAndUsesLatestRequest()
    {
        var store = new MemoryTranslationJobStore();
        store.Pending.Add(Job());
        var requested = new TaskCompletionSource<TranslationRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cache = new SqliteTranslationCache(":memory:");
        await using var service = new TranslationService(new DelegateTranslationClient((request, _) =>
        {
            requested.TrySetResult(request);
            return Task.FromResult("こんにちは");
        }), cache);
        await using var scheduler = new TranslationScheduler(store, service);
        scheduler.Completed += (_, _) => completed.TrySetResult();
        await scheduler.PauseAsync();
        await scheduler.StartAsync(Settings());
        await scheduler.UpdateSettingsAsync(Settings("ja-JP"));
        scheduler.NotifyWorkAvailable();
        await Task.Delay(100);

        Assert.False(requested.Task.IsCompleted);
        Assert.Single(await store.GetPendingAsync(10));

        await scheduler.ResumeAsync().WaitAsync(TIMEOUT);
        Assert.Contains("ja-JP", (await requested.Task.WaitAsync(TIMEOUT)).SystemPrompt);
        await completed.Task.WaitAsync(TIMEOUT);
        Assert.Equal("こんにちは", store.Results["message"]);
    }

    /// <summary>恢复保持用户关闭的翻译设置，连续释放等待同一次清理结束。</summary>
    [Fact]
    public async Task ResumeDoesNotEnableDisabledTranslationAndDisposeIsConcurrentSafe()
    {
        var store = new MemoryTranslationJobStore();
        store.Pending.Add(Job());
        var calls = 0;
        await using var cache = new SqliteTranslationCache(":memory:");
        await using var service = new TranslationService(new DelegateTranslationClient((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult("unexpected");
        }), cache);
        var scheduler = new TranslationScheduler(store, service);
        try
        {
            await scheduler.StartAsync(new() { Enabled = false, Model = "test" });
            await scheduler.PauseAsync();
            await Task.WhenAll(scheduler.ResumeAsync(), scheduler.ResumeAsync()).WaitAsync(TIMEOUT);
            await scheduler.PauseAsync().WaitAsync(TIMEOUT);
            Assert.Equal(0, Volatile.Read(ref calls));
            Assert.Single(await store.GetPendingAsync(10));
            await Task.WhenAll(scheduler.DisposeAsync().AsTask(), scheduler.DisposeAsync().AsTask()).WaitAsync(TIMEOUT);
        }
        finally
        {
            await scheduler.DisposeAsync();
        }
    }

    private static TranslationSettings Settings(string language = "zh-CN")
    {
        return new() { Enabled = true, Model = "test", TargetLanguage = language, BacklogMode = TranslationBacklogMode.AllMessages };
    }

    private static TranslationJob Job()
    {
        return new() { Id = "message", MessageId = "message", RoomTabId = "room", Text = "hello", CreatedAt = DateTimeOffset.UtcNow };
    }
}
