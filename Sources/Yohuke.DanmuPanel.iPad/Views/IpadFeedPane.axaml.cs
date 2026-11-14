using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Lucide.Avalonia;
using Yohuke.DanmuPanel.I18n;
using Yohuke.DanmuPanel.Models.Messages;
using Yohuke.DanmuPanel.Views;

namespace Yohuke.DanmuPanel.iPad.Views;

/// <summary>以紧凑桌面式标题承载共享消息列表，窄卡片通过触控菜单保留全部操作。</summary>
public sealed partial class IpadFeedPane : UserControl
{
    private readonly ContentControl actions;
    private readonly ContentControl readTabs;
    private readonly Button moreActions;
    private readonly TextBlock title;

    public MessageFeedView Feed { get; }

    /// <summary>装配共享标题、阅读标签及操作命令，不复制消息业务。</summary>
    public IpadFeedPane(MessageFeedView feed)
    {
        Feed = feed;
        AvaloniaXamlLoader.Load(this);
        title = this.FindControl<TextBlock>("TitleText")!;
        title.Bind(TextBlock.TextProperty, Localization.Observe("Workbench.Panel." + feed.Kind));
        actions = this.FindControl<ContentControl>("ActionsHost")!;
        actions.Content = feed.HeaderActionsTemplate.Build(feed);
        readTabs = this.FindControl<ContentControl>("ReadTabsHost")!;
        readTabs.Content = feed.HeaderTabsTemplate?.Build(feed);
        this.FindControl<ContentControl>("BadgeHost")!.Content = feed.HeaderBadgeTemplate?.Build(feed);
        this.FindControl<ContentControl>("FeedHost")!.Content = feed;
        moreActions = this.FindControl<Button>("MoreActionsButton")!;
        moreActions.Flyout = CreateActionsMenu();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        var showActions = Bounds.Width >= (Feed.Kind == MessageKind.Danmu ? 280 : 400);
        actions.IsVisible = showActions;
        moreActions.IsVisible = !showActions;
        title.MaxWidth = Math.Max(60, Math.Min(120, Bounds.Width - 100));
        var inlineTabs = Bounds.Width >= 340;
        Grid.SetRow(readTabs, inlineTabs ? 0 : 1);
        Grid.SetColumn(readTabs, inlineTabs ? 2 : 0);
        Grid.SetColumnSpan(readTabs, inlineTabs ? 1 : 4);
        readTabs.Margin = inlineTabs ? new(4, 0, 0, 0) : new(0, 0, 0, 2);
    }

    private MenuFlyout CreateActionsMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(CreateAction("Panel.ClearFeed", LucideIconKind.Trash2, Feed.Header.ClearCommand));
        if (Feed.Kind != MessageKind.Danmu)
        {
            menu.Items.Add(CreateAction("Panel.MarkAllRead", LucideIconKind.CheckCheck, Feed.Header.MarkAllReadCommand));
            menu.Items.Add(CreateAction("Panel.BackToTop", LucideIconKind.ArrowUpToLine, Feed.Header.BackToTopCommand));
        }
        menu.Items.Add(CreateAction("Panel.BackToLatest", LucideIconKind.ArrowDownToLine, Feed.Header.BackToLatestCommand));
        menu.Items.Add(CreateAction("Panel.MoreHistory", LucideIconKind.History, Feed.Header.LoadMoreCommand));
        return menu;
    }

    private static MenuItem CreateAction(string key, LucideIconKind icon, ICommand command)
    {
        var item = new MenuItem
        {
            Command = command,
            Icon = new LucideIcon
            {
                Kind = icon,
                Size = 16
            }
        };
        item.Bind(MenuItem.HeaderProperty, Localization.Observe(key));
        return item;
    }
}
