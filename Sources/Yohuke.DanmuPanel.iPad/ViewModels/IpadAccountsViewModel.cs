using Yohuke.DanmuPanel.I18n;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuCore.Apis;
using Yohuke.DanmuPanel.Models.State;
using Yohuke.DanmuPanel.ViewModels.Settings;

namespace Yohuke.DanmuPanel.iPad.ViewModels;

/// <summary>管理移动账号页面的登录、手动凭据编辑和验证状态。</summary>
public sealed partial class IpadAccountsViewModel : ObservableObject, IDisposable
{
    private readonly Action<BilibiliAccount?> openBrowser;
    private readonly Func<string, string, Task<bool>> confirm;
    private readonly Dictionary<BilibiliAccount, AccountSettingsRowViewModel> rows = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private BilibiliAccount? manualAccount;
    private int manualRevision;
    private bool disposed;
    [ObservableProperty] private bool isManualOpen;
    [ObservableProperty] private bool isBrowserOpen;
    [ObservableProperty] private string manualName = "";
    [ObservableProperty] private string manualCookie = "";
    [ObservableProperty] private string browserStatus = "";
    public IpadShellViewModel Shell { get; }
    public ObservableCollection<AccountSettingsRowViewModel> Accounts { get; } = [];
    public IRelayCommand BrowserLoginCommand { get; }
    public IRelayCommand AddManualCommand { get; }
    public IAsyncRelayCommand SaveManualCommand { get; }
    public IRelayCommand CancelManualCommand { get; }

    /// <summary>连接原生网页宿主、确认交互和共享账号会话服务。</summary>
    public IpadAccountsViewModel(IpadShellViewModel shell, Action<BilibiliAccount?> openBrowser,
        Func<string, string, Task<bool>> confirm)
    {
        Shell = shell;
        lifetimeToken = lifetime.Token;
        this.openBrowser = openBrowser;
        this.confirm = confirm;
        BrowserLoginCommand = new RelayCommand(() => openBrowser(null));
        AddManualCommand = new RelayCommand(() => EditManual(null));
        CancelManualCommand = new RelayCommand(CloseManual);
        SaveManualCommand = new AsyncRelayCommand(() => Shell.RunAsync(async () =>
        {
            var revision = manualRevision;
            await SaveCookieAsync(manualAccount, ManualCookie, ManualName);
            if (!disposed && revision == manualRevision)
            {
                CloseManual();
            }
        }));
        Shell.Runtime.AccountSessions.Changed += OnAccountStateChanged;
        RefreshAccounts();
    }

    /// <summary>保存共享服务规范化后的凭据并验证账号身份。</summary>
    public async Task SaveCookieAsync(BilibiliAccount? account, string cookie, string? name = null)
    {
        if (disposed)
        {
            return;
        }
        if (account is not null && !Shell.Runtime.Configuration.Accounts.Contains(account))
        {
            throw new InvalidOperationException(Localization.Get("iPad.AccountNoLongerAvailable"));
        }
        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new InvalidOperationException(Localization.Get("iPad.LoginRequired"));
        }
        var normalized = ApiCookieHelper.CreateBilibiliCookie(cookie);
        account ??= new()
        {
            Name = Localization.Get("Settings.Accounts"),
            AutoNameOnLogin = true
        };
        if (!string.IsNullOrWhiteSpace(name))
        {
            account.Name = name.Trim();
            account.AutoNameOnLogin = false;
        }
        account.Cookie = normalized;
        if (!Shell.Runtime.Configuration.Accounts.Contains(account))
        {
            Shell.Runtime.Configuration.Accounts.Add(account);
        }
        Shell.Runtime.Configuration.Settings.DefaultAccountId ??= account.Id;
        await SaveAsync();
        if (disposed)
        {
            return;
        }
        RefreshAccounts();
        await ValidateAsync(account);
        if (!disposed)
        {
            Shell.Notice = Localization.Get("iPad.AccountSaved");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Shell.Runtime.AccountSessions.Changed -= OnAccountStateChanged;
        lifetime.Cancel();
        lifetime.Dispose();
        CloseManual();
    }

    private void RefreshAccounts()
    {
        if (disposed)
        {
            return;
        }
        foreach (var account in Shell.Runtime.Configuration.Accounts)
        {
            if (!rows.ContainsKey(account))
            {
                var row = new AccountSettingsRowViewModel(account,
                    () =>
                    {
                        openBrowser(account);
                        return Task.CompletedTask;
                    },
                    () => Shell.RunAsync(() => SetDefaultAsync(account)),
                    () => Shell.RunAsync(async () =>
                    {
                        rows[account].CommitEdits();
                        await SaveAsync();
                        await ValidateAsync(account);
                    }),
                    () => Shell.RunAsync(() => DeleteAsync(account)),
                    () =>
                    {
                        EditManual(account);
                        return Task.CompletedTask;
                    },
                    () =>
                    {
                        EditManual(null);
                        return Task.CompletedTask;
                    }, Shell.ReportError);
                rows.Add(account, row);
                Accounts.Add(row);
            }
        }
        foreach (var account in rows.Keys.Where(account => !Shell.Runtime.Configuration.Accounts.Contains(account)).ToArray())
        {
            Accounts.Remove(rows[account]);
            rows.Remove(account);
        }
        foreach (var (account, row) in rows)
        {
            row.RefreshState(Shell.Runtime.AccountSessions.GetState(account.Id),
                Shell.Runtime.Configuration.Settings.DefaultAccountId == account.Id);
        }
    }

    private void EditManual(BilibiliAccount? account)
    {
        manualRevision++;
        manualAccount = account;
        ManualName = account?.Name ?? "";
        ManualCookie = account?.Cookie ?? "";
        IsManualOpen = true;
    }

    private async Task ValidateAsync(BilibiliAccount account)
    {
        if (disposed || !Shell.Runtime.Configuration.Accounts.Contains(account))
        {
            return;
        }
        try
        {
            var result = await Shell.Runtime.AccountSessions.ValidateAsync(account.Id, cancellationToken: lifetimeToken);
            if (disposed || !Shell.Runtime.Configuration.Accounts.Contains(account))
            {
                return;
            }
            if (result.Uid > 0 && account.AutoNameOnLogin)
            {
                account.Name = result.UserName;
                account.AutoNameOnLogin = false;
                rows[account].RefreshName();
                await SaveAsync();
            }
            RefreshAccounts();
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
    }

    private async Task SetDefaultAsync(BilibiliAccount account)
    {
        rows[account].CommitEdits();
        Shell.Runtime.Configuration.Settings.DefaultAccountId = account.Id;
        await SaveAsync();
        RefreshAccounts();
    }

    private async Task DeleteAsync(BilibiliAccount account)
    {
        if (!await confirm(Localization.Get("iPad.DeleteAccount"), Localization.Format("iPad.DeleteAccountConfirm", account.Name)))
        {
            return;
        }
        if (disposed)
        {
            return;
        }
        if (ReferenceEquals(manualAccount, account))
        {
            CloseManual();
        }
        Shell.Runtime.Configuration.Accounts.Remove(account);
        if (Shell.Runtime.Configuration.Settings.DefaultAccountId == account.Id)
        {
            Shell.Runtime.Configuration.Settings.DefaultAccountId = Shell.Runtime.Configuration.Accounts.FirstOrDefault()?.Id;
        }
        foreach (var workspace in Shell.Workspaces)
        {
            if (workspace.AccountOverrideId == account.Id)
            {
                workspace.AccountOverrideId = null;
            }
            foreach (var room in workspace.Rooms.Where(room => room.AccountOverrideId == account.Id))
            {
                room.AccountOverrideId = null;
            }
        }
        await SaveAsync();
        RefreshAccounts();
    }

    private Task SaveAsync()
    {
        return Shell.Runtime.ConfigurationStore.SaveAsync(Shell.Runtime.Configuration);
    }

    private void CloseManual()
    {
        manualRevision++;
        manualAccount = null;
        ManualCookie = "";
        ManualName = "";
        IsManualOpen = false;
    }

    private void OnAccountStateChanged(object? sender, string accountId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed)
            {
                RefreshAccounts();
            }
        });
    }
}
