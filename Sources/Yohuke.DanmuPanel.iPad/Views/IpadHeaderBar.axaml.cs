using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lucide.Avalonia;
using Yohuke.DanmuPanel.I18n;
using Yohuke.DanmuPanel.iPad.ViewModels;
using Yohuke.DanmuPanel.Models.Messages;

namespace Yohuke.DanmuPanel.iPad.Views;

/// <summary>提供单行菜单、居中房间监听状态和触控图标入口。</summary>
public sealed partial class IpadHeaderBar : UserControl
{
    private readonly Menu menu;
    private readonly MenuItem applicationMenu;
    private readonly MenuItem viewsMenu;
    private readonly MenuItem layoutsMenu;
    private readonly MenuItem defaultLayout;
    private readonly MenuItem focusLayout;
    private readonly Separator compactSeparator = new();
    private bool compact;

    public event Action? WorkspaceRequested;
    public event Action? RoomsRequested;
    public event Action? AccountsRequested;
    public event Action? SettingsRequested;
    public event Action<bool>? LayoutRequested;
    public event Action<MessageKind>? PanelRequested;

    /// <summary>将现有阅读台模型和实际可用操作组合为桌面式菜单栏。</summary>
    public IpadHeaderBar(IpadShellViewModel model)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = model;
        menu = this.FindControl<Menu>("ApplicationMenu")!;
        applicationMenu = CreateGroup("Shell.Menu.Application");
        applicationMenu.Items.Add(CreateItem("iPad.RoomsTitle", LucideIconKind.FolderTree, () => RoomsRequested?.Invoke()));
        applicationMenu.Items.Add(CreateItem("Settings.Accounts", LucideIconKind.UsersRound, () => AccountsRequested?.Invoke()));
        applicationMenu.Items.Add(CreateItem("Settings.Title", LucideIconKind.Settings, () => SettingsRequested?.Invoke()));
        compactSeparator.IsVisible = false;
        applicationMenu.Items.Add(compactSeparator);
        viewsMenu = CreateGroup("Shell.Menu.View");
        viewsMenu.Items.Add(CreateItem("iPad.ToggleWorkspace", LucideIconKind.PanelLeft, () => WorkspaceRequested?.Invoke()));
        viewsMenu.Items.Add(new Separator());
        foreach (var kind in Enum.GetValues<MessageKind>())
        {
            viewsMenu.Items.Add(CreateItem("Workbench.Panel." + kind, GetPanelIcon(kind), () => PanelRequested?.Invoke(kind)));
        }
        layoutsMenu = CreateGroup("Shell.Menu.Layout");
        defaultLayout = CreateItem("Layout.Preset.Default", LucideIconKind.PanelsTopLeft, () => LayoutRequested?.Invoke(false));
        focusLayout = CreateItem("iPad.FocusLayout", LucideIconKind.PanelTop, () => LayoutRequested?.Invoke(true));
        layoutsMenu.Items.Add(defaultLayout);
        layoutsMenu.Items.Add(focusLayout);
        menu.Items.Add(applicationMenu);
        menu.Items.Add(viewsMenu);
        menu.Items.Add(layoutsMenu);
        SizeChanged += OnSizeChanged;
    }

    /// <summary>同步当前会话布局及工作区可见状态。</summary>
    public void UpdateLayoutState(bool workspaceVisible, bool focused)
    {
        this.FindControl<Button>("WorkspaceToggleButton")!.Classes.Set("selected", workspaceVisible);
        defaultLayout.Icon = new LucideIcon
        {
            Kind = focused ? LucideIconKind.PanelsTopLeft : LucideIconKind.Check,
            Size = 16
        };
        focusLayout.Icon = new LucideIcon
        {
            Kind = focused ? LucideIconKind.Check : LucideIconKind.PanelTop,
            Size = 16
        };
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        var width = Bounds.Width;
        this.FindControl<TextBlock>("ApplicationName")!.IsVisible = width >= 1000;
        this.FindControl<Button>("AccountsButton")!.IsVisible = width >= 700;
        this.FindControl<Button>("SettingsButton")!.IsVisible = width >= 700;
        this.FindControl<TextBlock>("CurrentRoomNumber")!.IsVisible = width >= 560;
        this.FindControl<Grid>("RoomStatusToolbar")!.MaxWidth = width >= 1100 ? 360 : width >= 880 ? 320 : width >= 700 ? 260 : Math.Clamp(width - 140, 140, 260);
        var nextCompact = width < 700;
        if (compact == nextCompact)
        {
            return;
        }
        compact = nextCompact;
        compactSeparator.IsVisible = compact;
        if (compact)
        {
            menu.Items.Remove(viewsMenu);
            menu.Items.Remove(layoutsMenu);
            applicationMenu.Items.Add(viewsMenu);
            applicationMenu.Items.Add(layoutsMenu);
        }
        else
        {
            applicationMenu.Items.Remove(viewsMenu);
            applicationMenu.Items.Remove(layoutsMenu);
            menu.Items.Add(viewsMenu);
            menu.Items.Add(layoutsMenu);
        }
    }

    private static MenuItem CreateGroup(string key)
    {
        var item = new MenuItem();
        item.Bind(MenuItem.HeaderProperty, Localization.Observe(key));
        return item;
    }

    private static MenuItem CreateItem(string key, LucideIconKind icon, Action action)
    {
        var item = CreateGroup(key);
        item.Icon = new LucideIcon
        {
            Kind = icon,
            Size = 16
        };
        item.Click += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        return item;
    }

    private static LucideIconKind GetPanelIcon(MessageKind kind)
    {
        return kind switch
        {
            MessageKind.Danmu => LucideIconKind.MessagesSquare,
            MessageKind.SuperChat => LucideIconKind.BadgeDollarSign,
            MessageKind.Gift => LucideIconKind.Gift,
            MessageKind.RedPocket => LucideIconKind.Mail,
            MessageKind.Guard => LucideIconKind.Ship,
            _ => LucideIconKind.PanelTop
        };
    }

    private void Workspace_OnClick(object? sender, RoutedEventArgs args)
    {
        WorkspaceRequested?.Invoke();
    }

    private void Accounts_OnClick(object? sender, RoutedEventArgs args)
    {
        AccountsRequested?.Invoke();
    }

    private void Settings_OnClick(object? sender, RoutedEventArgs args)
    {
        SettingsRequested?.Invoke();
    }
}
