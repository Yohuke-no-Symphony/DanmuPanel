using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Yohuke.DanmuPanel.I18n;
using Yohuke.DanmuPanel.iPad.Services;
using Yohuke.DanmuPanel.iPad.ViewModels;
using Yohuke.DanmuPanel.Models.Messages;
using Yohuke.DanmuPanel.Views;

namespace Yohuke.DanmuPanel.iPad.Views;

/// <summary>将弹幕和付费消息分页组合为阅读布局，并提供浮动工作区与独立移动导航。</summary>
public sealed partial class IpadShellView : UserControl, IDisposable
{
    private readonly IpadShellViewModel model;
    private readonly List<IpadFeedPane> panes = [];
    private readonly SemaphoreSlim confirmations = new(1, 1);
    private readonly IpadConfirmationService confirmationService = new();
    private readonly IpadHeaderBar header;
    private readonly IpadWorkspaceSidebarViewModel sidebarModel;
    private readonly IpadWorkspaceSidebar sidebar;
    private readonly Dictionary<MessageKind, ContentControl> wideHosts = [];
    private readonly Dictionary<MessageKind, ContentControl> singleHosts = [];
    private readonly Grid single;
    private readonly ContentControl drawerHost;
    private readonly TabStrip categories;
    private readonly TabStrip paidCategories;
    private TaskCompletionSource<bool>? pendingConfirmation;
    private IDisposable? pageLifetime;
    private IDisposable? pageTitleBinding;
    private bool initialized;
    private bool disposed;
    private bool wideLayout;
    private bool layoutMounted;
    private bool focusMode;
    private bool drawerOpen;
    private bool synchronizingSelection;
    private MessageKind selectedPanel = MessageKind.Danmu;
    private MessageKind selectedPaidPanel = MessageKind.SuperChat;

    /// <summary>装配菜单栏、工作区树和共享消息面板，保留独立页面的既有业务入口。</summary>
    public IpadShellView(IpadShellViewModel model)
    {
        this.model = model;
        AvaloniaXamlLoader.Load(this);
        DataContext = model;
        single = this.FindControl<Grid>("SingleHost")!;
        drawerHost = this.FindControl<ContentControl>("DrawerSidebarHost")!;
        categories = this.FindControl<TabStrip>("CategoryTabs")!;
        paidCategories = this.FindControl<TabStrip>("PaidCategoryTabs")!;
        foreach (var kind in new[]
                 {
                     MessageKind.Danmu, MessageKind.SuperChat, MessageKind.Gift, MessageKind.Guard, MessageKind.RedPocket
                 })
        {
            panes.Add(new(new MessageFeedView(confirmationService, model.Runtime, kind, model.ReportError, NavigateToMessageAsync)));
            wideHosts.Add(kind, this.FindControl<ContentControl>(kind + "Host")!);
            singleHosts.Add(kind, this.FindControl<ContentControl>("Single" + kind + "Host")!);
        }
        foreach (var pane in panes.Where(pane => pane.Feed.Kind != MessageKind.Danmu))
        {
            this.FindControl<ContentControl>(pane.Feed.Kind + "DockBadge")!.Content =
                pane.Feed.HeaderBadgeTemplate!.Build(pane.Feed);
        }
        sidebarModel = new(model);
        sidebarModel.RoomSelected += CloseDrawer;
        sidebar = new(sidebarModel);
        drawerHost.Content = sidebar;
        sidebar.ManageRequested += ShowRooms;
        sidebar.CloseRequested += ToggleWorkspace;
        header = new(model);
        header.WorkspaceRequested += ToggleWorkspace;
        header.RoomsRequested += ShowRooms;
        header.AccountsRequested += ShowAccounts;
        header.SettingsRequested += ShowSettings;
        header.LayoutRequested += SetLayout;
        header.PanelRequested += ShowPanel;
        this.FindControl<ContentControl>("HeaderHost")!.Content = header;
        model.RoomChanged += OnRoomChanged;
        initialized = true;
        SizeChanged += OnSizeChanged;
        RefreshLayout();
    }

    /// <summary>将房间选择同步给全部共享面板。</summary>
    public Task RefreshFeedsAsync()
    {
        return Task.WhenAll(panes.Select(pane => pane.Feed.Model.SelectRoomAsync(model.SelectedRoom)));
    }

    /// <summary>串行呈现需要明确用户选择的确认交互。</summary>
    public async Task<bool> ConfirmAsync(string title, string message)
    {
        await confirmations.WaitAsync();
        try
        {
            if (disposed)
            {
                return false;
            }
            pendingConfirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            this.FindControl<TextBlock>("ConfirmationTitle")!.Text = title;
            this.FindControl<TextBlock>("ConfirmationMessage")!.Text = message;
            this.FindControl<Border>("ConfirmationOverlay")!.IsVisible = true;
            return await pendingConfirmation.Task;
        }
        finally
        {
            this.FindControl<Border>("ConfirmationOverlay")!.IsVisible = false;
            pendingConfirmation = null;
            confirmations.Release();
        }
    }

    /// <summary>进入后台时取消尚未决定的确认框。</summary>
    public void CancelConfirmation()
    {
        pendingConfirmation?.TrySetResult(false);
    }

    /// <summary>释放登录网页等瞬态内容，保留阅读运行时。</summary>
    public void CloseTransientPage()
    {
        if (this.FindControl<ContentControl>("PageHost")!.Content is IpadAccountsPage accounts)
        {
            accounts.ReleaseBrowser();
        }
    }

    /// <summary>关闭当前页面并释放其页面级订阅和原生资源。</summary>
    public void ClosePage()
    {
        pageLifetime?.Dispose();
        pageTitleBinding?.Dispose();
        pageTitleBinding = null;
        pageLifetime = null;
        this.FindControl<ContentControl>("PageHost")!.Content = null;
        this.FindControl<Border>("PageOverlay")!.IsVisible = false;
        if (!disposed)
        {
            model.RefreshRooms();
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
        model.RoomChanged -= OnRoomChanged;
        SizeChanged -= OnSizeChanged;
        CancelConfirmation();
        ClosePage();
        sidebarModel.Dispose();
        foreach (var pane in panes)
        {
            pane.Feed.Model.Dispose();
        }
    }

    private void ShowPage(string titleKey, Control page)
    {
        CloseDrawer();
        ClosePage();
        pageLifetime = page as IDisposable;
        pageTitleBinding = this.FindControl<TextBlock>("PageTitle")!.Bind(TextBlock.TextProperty, Localization.Observe(titleKey));
        this.FindControl<ContentControl>("PageHost")!.Content = page;
        this.FindControl<Border>("PageOverlay")!.IsVisible = true;
    }

    private async void OnRoomChanged()
    {
        await model.RunAsync(RefreshFeedsAsync);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        RefreshLayout();
    }

    private void RefreshLayout()
    {
        if (!initialized || disposed)
        {
            return;
        }
        var nextWide = !focusMode && Bounds.Width >= 900 && Bounds.Height >= 500;
        this.FindControl<Grid>("WorkspaceDrawer")!.IsVisible = drawerOpen;
        drawerHost.Width = Math.Min(320, Math.Max(220, Bounds.Width * 0.84));
        header.UpdateLayoutState(drawerOpen, focusMode);
        if (!layoutMounted || wideLayout != nextWide)
        {
            foreach (var host in wideHosts.Values.Concat(singleHosts.Values))
            {
                host.Content = null;
            }
            wideLayout = nextWide;
            var hosts = wideLayout ? wideHosts : singleHosts;
            foreach (var pane in panes)
            {
                hosts[pane.Feed.Kind].Content = pane;
            }
            layoutMounted = true;
        }
        this.FindControl<Grid>("WideLayout")!.IsVisible = wideLayout;
        this.FindControl<ScrollViewer>("CategorySelector")!.IsVisible = !wideLayout;
        single.IsVisible = !wideLayout;
        foreach (var (kind, host) in wideHosts)
        {
            host.IsVisible = kind == MessageKind.Danmu || kind == selectedPaidPanel;
        }
        foreach (var (kind, host) in singleHosts)
        {
            host.IsVisible = kind == selectedPanel;
        }
    }

    private void ToggleWorkspace()
    {
        drawerOpen = !drawerOpen;
        RefreshLayout();
    }

    private void CloseDrawer()
    {
        drawerOpen = false;
        RefreshLayout();
    }

    private void SetLayout(bool focused)
    {
        focusMode = focused;
        drawerOpen = false;
        RefreshLayout();
    }

    private void ShowPanel(MessageKind kind)
    {
        selectedPanel = kind;
        if (kind != MessageKind.Danmu)
        {
            selectedPaidPanel = kind;
        }
        synchronizingSelection = true;
        try
        {
            categories.SelectedItem = categories.Items.OfType<TabStripItem>()
                .Single(item => item.Tag is MessageKind itemKind && itemKind == selectedPanel);
            paidCategories.SelectedItem = paidCategories.Items.OfType<TabStripItem>()
                .Single(item => item.Tag is MessageKind itemKind && itemKind == selectedPaidPanel);
        }
        finally
        {
            synchronizingSelection = false;
        }
        RefreshLayout();
    }

    private async Task NavigateToMessageAsync(MessageRecord record)
    {
        var index = panes.FindIndex(pane => pane.Feed.Kind == record.Kind);
        if (index < 0)
        {
            return;
        }
        ShowPanel(record.Kind);
        await Dispatcher.UIThread.InvokeAsync(() => panes[index].UpdateLayout(), DispatcherPriority.Loaded);
        await panes[index].Feed.PingAsync(record);
    }

    private void ShowRooms()
    {
        ShowPage("iPad.RoomsTitle", new IpadRoomsPage(model, confirmationService));
    }

    private void ShowAccounts()
    {
        ShowPage("Settings.Accounts", new IpadAccountsPage(model, confirmationService));
    }

    private void ShowSettings()
    {
        ShowPage("iPad.SettingsTitle", new IpadSettingsPage(model, confirmationService));
    }

    private void Category_OnChanged(object? sender, SelectionChangedEventArgs args)
    {
        args.Handled = true;
        if (initialized && !synchronizingSelection && categories.SelectedItem is TabStripItem { Tag: MessageKind kind })
        {
            ShowPanel(kind);
        }
    }

    private void PaidCategory_OnChanged(object? sender, SelectionChangedEventArgs args)
    {
        args.Handled = true;
        if (initialized && !synchronizingSelection && paidCategories.SelectedItem is TabStripItem { Tag: MessageKind kind })
        {
            ShowPanel(kind);
        }
    }

    private void Rooms_OnClick(object? sender, RoutedEventArgs args)
    {
        ShowRooms();
    }

    private void Back_OnClick(object? sender, RoutedEventArgs args)
    {
        ClosePage();
    }

    private void DrawerBackdrop_OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        CloseDrawer();
        args.Handled = true;
    }

    private void CancelConfirmation_OnClick(object? sender, RoutedEventArgs args)
    {
        pendingConfirmation?.TrySetResult(false);
    }

    private void AcceptConfirmation_OnClick(object? sender, RoutedEventArgs args)
    {
        pendingConfirmation?.TrySetResult(true);
    }
}
