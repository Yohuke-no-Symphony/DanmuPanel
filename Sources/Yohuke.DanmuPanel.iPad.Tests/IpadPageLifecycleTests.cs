using System.ComponentModel;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuCore.Apis;
using Yohuke.DanmuCore.Models.Accounts;
using Yohuke.DanmuPanel.iPad.ViewModels;
using Yohuke.DanmuPanel.Models.State;
using Yohuke.DanmuPanel.Services;
using Yohuke.DanmuPanel.Services.Settings;
using Yohuke.DanmuPanel.ViewModels.Workspace;
using Xunit;

namespace Yohuke.DanmuPanel.iPad.Tests;

/// <summary>验证移动页面关闭、重复提交与旧编辑结果不会破坏已保存的配置。</summary>
public sealed class IpadPageLifecycleTests
{
    private static readonly TimeSpan operationTimeout = TimeSpan.FromSeconds(10);

    /// <summary>关闭正在读文件的设置页后，旧导入不能覆盖新页面保存的设置或通知。</summary>
    [AvaloniaFact]
    public async Task ClosingSettingsPageDuringImportPreservesLaterSave()
    {
        using var platform = new TestIpadPlatform();
        var configuration = new PanelConfiguration();
        var store = new ConfigurationService(platform.DataDirectory);
        await using var runtime = new PanelRuntime(store, configuration);
        await runtime.InitializeAsync();
        using var shell = new IpadShellViewModel(runtime, platform, new(platform.DataDirectory), () => { });
        using var closingPage = new IpadSettingsViewModel(shell);
        using var payload = new MemoryStream();
        await new SettingsShareService().ExportAsync(new() { MessageTextScale = 1.8 }, payload);
        using var source = new DeferredReadStream(payload.ToArray());
        var importing = closingPage.ImportAsync(source);
        await source.ReadStarted.WaitAsync(operationTimeout);

        closingPage.Dispose();
        using var nextPage = new IpadSettingsViewModel(shell);
        nextPage.Reading.MessageTextScale = 140;
        await nextPage.SaveAsync().WaitAsync(operationTimeout);
        var notice = shell.Notice;
        source.Release();
        await importing.WaitAsync(operationTimeout);

        Assert.Equal(1.4, configuration.Settings.MessageTextScale);
        Assert.Equal(1.4, store.Load().Settings.MessageTextScale);
        Assert.Equal(notice, shell.Notice);
        Assert.False(shell.HasError);
    }

    /// <summary>旧编辑器的重复确认只增加一个房间，保存完成后仍保留后来打开的编辑器。</summary>
    [AvaloniaFact]
    public async Task RepeatedRoomConfirmationAddsOnceAndPreservesNextEditor()
    {
        using var platform = new TestIpadPlatform();
        var workspace = new WorkspaceState { Name = "测试工作区" };
        var configuration = new PanelConfiguration { Workspaces = [workspace] };
        var store = new ConfigurationService(platform.DataDirectory);
        await using var runtime = new PanelRuntime(store, configuration);
        using var shell = new IpadShellViewModel(runtime, platform, new(platform.DataDirectory), () => { });
        using var page = new IpadRoomsViewModel(shell, (_, _) => Task.FromResult(true));
        page.AddRoomCommand.Execute(null);
        var originalEditor = Assert.IsType<RoomEditorViewModel>(page.Editor);
        originalEditor.NewRoomId = "42";
        originalEditor.NewRoomName = "只添加一次";
        originalEditor.ConfirmCommand.Execute(null);
        page.AddRoomCommand.Execute(null);
        var nextEditor = Assert.IsType<RoomEditorViewModel>(page.Editor);
        originalEditor.ConfirmCommand.Execute(null);
        await WaitForIdleAsync(shell);
        await store.SaveAsync(configuration).WaitAsync(operationTimeout);

        Assert.Equal("42", Assert.Single(workspace.Rooms).RoomId);
        Assert.Single(Assert.Single(store.Load().Workspaces).Rooms);
        Assert.Same(nextEditor, page.Editor);
        Assert.True(page.IsEditing);
        Assert.False(shell.HasError);
    }

    /// <summary>删除正在编辑的账号清除草稿和绑定，并拒绝保存已经删除的旧账号对象。</summary>
    [AvaloniaFact]
    public async Task DeletingEditedAccountPreventsStaleSaveFromRestoringIt()
    {
        using var platform = new TestIpadPlatform();
        var account = new BilibiliAccount { Name = "待删除账号", Cookie = "DedeUserID=42" };
        var room = new RoomTabState { RoomId = "42", AccountOverrideId = account.Id };
        var workspace = new WorkspaceState { AccountOverrideId = account.Id, Rooms = [room] };
        var configuration = new PanelConfiguration { Accounts = [account], Workspaces = [workspace] };
        configuration.Settings.DefaultAccountId = account.Id;
        var store = new ConfigurationService(platform.DataDirectory);
        await using var runtime = new PanelRuntime(store, configuration);
        using var shell = new IpadShellViewModel(runtime, platform, new(platform.DataDirectory), () => { });
        using var page = new IpadAccountsViewModel(shell, _ => { }, (_, _) => Task.FromResult(true));
        var row = Assert.Single(page.Accounts);
        await row.EditCookieCommand.ExecuteAsync(null).WaitAsync(operationTimeout);
        Assert.True(page.IsManualOpen);
        await row.DeleteCommand.ExecuteAsync(null).WaitAsync(operationTimeout);

        Assert.False(page.IsManualOpen);
        Assert.Empty(page.ManualCookie);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            page.SaveCookieAsync(account, "DedeUserID=42").WaitAsync(operationTimeout));
        Assert.Empty(configuration.Accounts);
        Assert.Empty(page.Accounts);
        Assert.Null(configuration.Settings.DefaultAccountId);
        Assert.Null(workspace.AccountOverrideId);
        Assert.Null(room.AccountOverrideId);
        Assert.Empty(store.Load().Accounts);
        Assert.False(shell.HasError);
    }

    /// <summary>凭据提交期间关闭账号页仍完成本地保存，不再验证或发布成功通知。</summary>
    [AvaloniaFact]
    public async Task ClosingAccountPageDuringSaveSkipsValidationAndKeepsCredentials()
    {
        using var platform = new TestIpadPlatform();
        var account = new BilibiliAccount { Name = "离线账号", Cookie = "DedeUserID=42" };
        var configuration = new PanelConfiguration { Accounts = [account] };
        var store = new ConfigurationService(platform.DataDirectory);
        await using var runtime = new PanelRuntime(store, configuration);
        using var shell = new IpadShellViewModel(runtime, platform, new(platform.DataDirectory), () => { });
        using var page = new IpadAccountsViewModel(shell, _ => { }, (_, _) => Task.FromResult(true));
        var closed = false;
        account.PropertyChanged += ClosePageAfterCredentialChange;
        try
        {
            await page.SaveCookieAsync(account, "DedeUserID=43").WaitAsync(operationTimeout);
        }
        finally
        {
            account.PropertyChanged -= ClosePageAfterCredentialChange;
        }

        Assert.True(closed);
        Assert.Equal("43", ApiCookieHelper.GetValue(Assert.Single(store.Load().Accounts).Cookie, "DedeUserID"));
        Assert.Equal(AccountValidationStatus.Unchecked, runtime.AccountSessions.GetState(account.Id).Status);
        Assert.False(shell.HasNotice);
        Assert.False(shell.HasError);

        void ClosePageAfterCredentialChange(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(BilibiliAccount.Cookie))
            {
                closed = true;
                page.Dispose();
            }
        }
    }

    private static async Task WaitForIdleAsync(IpadShellViewModel shell)
    {
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.PropertyChanged += OnShellChanged;
        try
        {
            if (!shell.IsBusy)
            {
                return;
            }
            await idle.Task.WaitAsync(operationTimeout);
        }
        finally
        {
            shell.PropertyChanged -= OnShellChanged;
        }

        void OnShellChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(IpadShellViewModel.IsBusy) && !shell.IsBusy)
            {
                idle.TrySetResult();
            }
        }
    }
}
