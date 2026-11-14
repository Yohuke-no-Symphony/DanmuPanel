using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucide.Avalonia;
using Yohuke.DanmuCore.Diagnostics;
using Yohuke.DanmuPanel.I18n;
using Yohuke.DanmuPanel.iPad.Services;
using Yohuke.DanmuPanel.Models.State;
using Yohuke.DanmuPanel.Services;

namespace Yohuke.DanmuPanel.iPad.ViewModels;

/// <summary>管理当前工作区、阅读房间及 iPad 页面共享的状态。</summary>
public sealed partial class IpadShellViewModel : ObservableObject, IDisposable
{
    private readonly IIpadPlatform platform;
    private readonly IpadPreferencesStore preferences;
    private readonly Action applyAppearance;
    private readonly SemaphoreSlim settingsTransactions = new(1, 1);
    private readonly AppLogService log = new(200);
    private readonly AppLogRuntimeBridge logBridge;
    private readonly HashSet<BilibiliAccount> observedAccounts = [];
    private string[] accountSecrets = [];
    private long lastErrorSequence;
    private bool selecting;
    private int activeOperations;
    private bool disposed;

    [ObservableProperty] private WorkspaceState? selectedWorkspace;
    [ObservableProperty] private RoomTabState? selectedRoom;
    [ObservableProperty] private string status = Localization.Get("iPad.Preparing");
    [ObservableProperty] private string error = "";
    [ObservableProperty] private string notice = "";
    [ObservableProperty] private bool isReady;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool keepScreenOn;

    public PanelRuntime Runtime { get; }
    public ObservableCollection<WorkspaceState> Workspaces => Runtime.Configuration.Workspaces;
    public ObservableCollection<RoomTabState> Rooms { get; } = [];
    public Version Version => platform.Version;
    public bool HasLiveRoom => SelectedRoom is not null;
    public bool NeedsSetup => !Workspaces.Any(workspace => workspace.Rooms.Count > 0);
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool HasNotice => !string.IsNullOrEmpty(Notice);
    public bool IsListening => HasLiveRoom && Runtime.IsListening(SelectedRoom!);
    public string RoomTitle => SelectedRoom?.Name ?? Localization.Get("Panel.SelectRoom");
    public string RoomNumber => HasLiveRoom ? SelectedRoom!.RoomId : "";
    public LucideIconKind ListenIcon => IsListening ? LucideIconKind.Square : LucideIconKind.Play;
    public string ListeningText => HasLiveRoom && Runtime.IsListening(SelectedRoom!) ? Localization.Get("Panel.StopListening") : Localization.Get("Panel.StartListening");
    public IAsyncRelayCommand ToggleListeningCommand { get; }
    public IRelayCommand DismissErrorCommand { get; }
    public event Action? RoomChanged;
    public event Action? StatusChanged;

    /// <summary>关联现有运行时与平台设置，不在构造时发起网络操作。</summary>
    public IpadShellViewModel(PanelRuntime runtime, IIpadPlatform platform, IpadPreferencesStore preferences,
        Action applyAppearance)
    {
        Runtime = runtime;
        this.platform = platform;
        this.preferences = preferences;
        this.applyAppearance = applyAppearance;
        RefreshAccountSubscriptions();
        Runtime.Configuration.Accounts.CollectionChanged += OnAccountsChanged;
        log.Changed += OnLogChanged;
        logBridge = new(log, GetSensitiveValues);
        keepScreenOn = preferences.LoadKeepScreenOn();
        ToggleListeningCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            await Runtime.ToggleListeningAsync(SelectedWorkspace!, SelectedRoom!);
            RefreshStatus();
        }), () => IsReady && HasLiveRoom && !IsBusy);
        DismissErrorCommand = new RelayCommand(() => Error = "");
        runtime.Changed += OnRuntimeChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        SelectedWorkspace = Workspaces.FirstOrDefault(value => value.Id == runtime.Configuration.Settings.SelectedWorkspaceId)
            ?? Workspaces.FirstOrDefault();
        if (SelectedWorkspace is null)
        {
            RefreshRooms();
        }
    }

    /// <summary>更新已配置的房间选项，保留有效选择，空工作区保持未选择状态。</summary>
    public void RefreshRooms()
    {
        selecting = true;
        var previousId = SelectedRoom?.Id;
        Rooms.Clear();
        foreach (var room in SelectedWorkspace?.Rooms ?? [])
        {
            Rooms.Add(room);
        }
        SelectedRoom = Rooms.FirstOrDefault(value => value.Id == previousId)
            ?? Rooms.FirstOrDefault(value => value.Id == SelectedWorkspace?.SelectedRoomId)
            ?? Rooms.FirstOrDefault();
        selecting = false;
        OnPropertyChanged(nameof(NeedsSetup));
        RefreshStatus();
        RoomChanged?.Invoke();
    }

    /// <summary>保存平台偏好并应用共享主题及语言设置。</summary>
    public async Task ApplyPreferencesAsync(bool screenOn)
    {
        await preferences.SaveKeepScreenOnAsync(screenOn);
        KeepScreenOn = screenOn;
        applyAppearance();
        Runtime.NotifyAppearanceChanged();
        RefreshStatus();
    }

    /// <summary>串行执行各设置页面的配置事务，取消尚未开始的过期页面操作。</summary>
    public async Task ExecuteSettingsUpdateAsync(Func<Task> update, CancellationToken cancellationToken)
    {
        await settingsTransactions.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await update();
        }
        finally
        {
            settingsTransactions.Release();
        }
    }

    /// <summary>刷新连接状态与首次配置提示。</summary>
    public void RefreshStatus()
    {
        Status = HasLiveRoom ? Localization.Get(Runtime.GetStatus(SelectedRoom)) : Localization.Get("Panel.SelectRoom");
        OnPropertyChanged(nameof(HasLiveRoom));
        OnPropertyChanged(nameof(ListeningText));
        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(RoomTitle));
        OnPropertyChanged(nameof(RoomNumber));
        OnPropertyChanged(nameof(ListenIcon));
        OnPropertyChanged(nameof(NeedsSetup));
        ToggleListeningCommand.NotifyCanExecuteChanged();
        StatusChanged?.Invoke();
    }

    /// <summary>在界面展示脱敏后的错误信息。</summary>
    public void ReportError(Exception exception)
    {
        Error = DiagnosticSanitizer.Sanitize(LocalizedExceptionText.Get(exception), GetSensitiveValues());
    }

    /// <summary>执行用户操作并统一恢复繁忙状态、捕获错误。</summary>
    public async Task RunAsync(Func<Task> action)
    {
        activeOperations++;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            activeOperations--;
            IsBusy = activeOperations > 0;
            RefreshStatus();
        }
    }

    /// <summary>解除应用级状态订阅，不释放由宿主持有的运行时。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Runtime.Changed -= OnRuntimeChanged;
        Localization.LanguageChanged -= OnLanguageChanged;
        Runtime.Configuration.Accounts.CollectionChanged -= OnAccountsChanged;
        foreach (var account in observedAccounts)
        {
            account.PropertyChanged -= OnAccountChanged;
        }
        observedAccounts.Clear();
        log.Changed -= OnLogChanged;
        logBridge.Dispose();
        log.Dispose();
    }

    private IEnumerable<string> GetSensitiveValues()
    {
        return Volatile.Read(ref accountSecrets).Append(Runtime.Configuration.Settings.Translation.ApiKey);
    }

    private void RefreshAccountSubscriptions()
    {
        var accounts = Runtime.Configuration.Accounts.ToHashSet();
        foreach (var account in observedAccounts.Except(accounts).ToArray())
        {
            account.PropertyChanged -= OnAccountChanged;
            observedAccounts.Remove(account);
        }
        foreach (var account in accounts.Except(observedAccounts))
        {
            account.PropertyChanged += OnAccountChanged;
            observedAccounts.Add(account);
        }
        RefreshAccountSecrets();
    }

    private void RefreshAccountSecrets()
    {
        Volatile.Write(ref accountSecrets, Runtime.Configuration.Accounts.Select(account => account.Cookie).ToArray());
    }

    private void OnAccountsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        RefreshAccountSubscriptions();
    }

    private void OnAccountChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(BilibiliAccount.Cookie))
        {
            RefreshAccountSecrets();
        }
    }

    private void OnLogChanged(object? sender, EventArgs args)
    {
        var latest = log.Entries.LastOrDefault(entry => entry.IsError);
        if (latest is null)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (disposed || latest.Sequence <= lastErrorSequence)
            {
                return;
            }
            lastErrorSequence = latest.Sequence;
            Error = DiagnosticSanitizer.Sanitize($"{latest.Source}: {latest.Message}", GetSensitiveValues());
        });
    }

    private void OnRuntimeChanged(string roomId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed)
            {
                RefreshStatus();
            }
        });
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        RefreshRooms();
    }

    partial void OnSelectedWorkspaceChanged(WorkspaceState? value)
    {
        Runtime.Configuration.Settings.SelectedWorkspaceId = value?.Id;
        selectedRoom = null;
        RefreshRooms();
        _ = SaveSelectionAsync();
    }

    partial void OnSelectedRoomChanged(RoomTabState? value)
    {
        if (selecting)
        {
            return;
        }
        if (SelectedWorkspace is { } workspace && HasLiveRoom)
        {
            workspace.SelectedRoomId = value?.Id;
        }
        RefreshStatus();
        RoomChanged?.Invoke();
        _ = SaveSelectionAsync();
    }

    partial void OnIsReadyChanged(bool value)
    {
        ToggleListeningCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        ToggleListeningCommand.NotifyCanExecuteChanged();
    }

    partial void OnErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnNoticeChanged(string value)
    {
        OnPropertyChanged(nameof(HasNotice));
    }

    private async Task SaveSelectionAsync()
    {
        try
        {
            await Runtime.ConfigurationStore.SaveAsync(Runtime.Configuration);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }
}
