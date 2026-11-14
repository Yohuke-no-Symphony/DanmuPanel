using Yohuke.DanmuPanel.Models.Logging;

namespace Yohuke.DanmuPanel.Services;

/// <summary>将启动与运行日志连接到宿主日志服务，并统一隐藏敏感信息。</summary>
public sealed class AppLogRuntimeBridge : IDisposable
{
    private readonly IAppLogService log;
    private readonly Func<IEnumerable<string>> getSensitiveValues;
    private readonly Lock gate = new();
    private bool disposed;

    /// <summary>订阅应用日志，并由宿主提供当前需要隐藏的凭据。</summary>
    public AppLogRuntimeBridge(IAppLogService log, Func<IEnumerable<string>>? getSensitiveValues = null)
    {
        this.log = log;
        this.getSensitiveValues = getSensitiveValues ?? (() => []);
        StartupLog.Written += OnStartupLog;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StartupLog.Written -= OnStartupLog;
        }
    }

    private void OnStartupLog(AppLogLevel level, string source, string message, Exception? error)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            var sensitiveValues = getSensitiveValues().ToArray();
            var safeSource = DiagnosticSanitizer.Sanitize(source, sensitiveValues);
            var safeMessage = DiagnosticSanitizer.Sanitize(PanelErrorText.Render(message), sensitiveValues);
            var details = error is null ? null : DiagnosticSanitizer.Sanitize(error.ToString(), sensitiveValues);
            switch (level)
            {
                case AppLogLevel.ERROR:
                    log.Error(safeSource, safeMessage, details: details);
                    break;
                case AppLogLevel.WARNING:
                    log.Warning(safeSource, safeMessage, details);
                    break;
                case AppLogLevel.INFO:
                    log.Information(safeSource, safeMessage, details);
                    break;
            }
        }
    }
}
