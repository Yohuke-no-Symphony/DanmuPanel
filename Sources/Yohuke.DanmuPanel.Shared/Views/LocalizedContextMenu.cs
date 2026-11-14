using Avalonia.Controls;
using Yohuke.DanmuPanel.I18n;

namespace Yohuke.DanmuPanel.Views;

/// <summary>为上下文操作提供随界面语言更新的标题。</summary>
public sealed class LocalizedContextMenu : ContextMenu
{
    private readonly Dictionary<MenuItem, string> headerKeys = new();
    private bool isObservingLanguage;

    protected override Type StyleKeyOverride => typeof(ContextMenu);

    /// <summary>创建可跟随当前语言刷新的上下文菜单。</summary>
    public LocalizedContextMenu()
    {
        Opened += (_, _) =>
        {
            if (!isObservingLanguage)
            {
                Localization.LanguageChanged += OnLanguageChanged;
                isObservingLanguage = true;
            }
            RefreshHeaders();
        };
        Closed += (_, _) =>
        {
            Localization.LanguageChanged -= OnLanguageChanged;
            isObservingLanguage = false;
        };
    }

    /// <summary>关联菜单项与本地化资源键。</summary>
    public void SetHeader(MenuItem item, string key)
    {
        headerKeys[item] = key;
        item.Header = Localization.Get(key);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshHeaders();
    }

    private void RefreshHeaders()
    {
        foreach (var (item, key) in headerKeys)
        {
            item.Header = Localization.Get(key);
        }
    }
}
