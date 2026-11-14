using Yohuke.DanmuPanel.Models.Translation;
using Yohuke.DanmuPanel.Models.Messages;
using Yohuke.DanmuPanel.Services.Messages;

namespace Yohuke.DanmuPanel.Services.Translation;

/// <summary>调度持久翻译任务并控制并发与积压。</summary>
public interface ITranslationScheduler : IAsyncDisposable
{
    event EventHandler<TranslationCompletedEventArgs>? Completed;
    event Action<string>? Error;
    event Action<string, MessageKind?>? PendingChanged;
    Task StartAsync(TranslationSettings settings, CancellationToken cancellationToken = default,
        DanmuRegexFilter? danmuFilter = null);
    Task UpdateSettingsAsync(TranslationSettings settings, CancellationToken cancellationToken = default,
        DanmuRegexFilter? danmuFilter = null);
    /// <summary>暂停调度并取消在途请求，保留持久任务与翻译设置。</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);
    /// <summary>恢复原调度循环；已恢复时不重复启动。</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);
    void NotifyWorkAvailable(bool retryAuthentication = false);
}
