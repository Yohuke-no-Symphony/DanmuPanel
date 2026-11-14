namespace Yohuke.DanmuPanel.iPad.Services;

/// <summary>保存独立于跨平台分享设置的 iPad 阅读偏好。</summary>
public sealed class IpadPreferencesStore
{
    private readonly string path;

    /// <summary>在宿主应用容器中保存常亮选项。</summary>
    public IpadPreferencesStore(string directory)
    {
        path = Path.Combine(directory, "ipad-keep-screen-on.txt");
    }

    /// <summary>首次运行默认在前台保持屏幕常亮。</summary>
    public bool LoadKeepScreenOn()
    {
        return !File.Exists(path) || !bool.TryParse(File.ReadAllText(path), out var value) || value;
    }

    /// <summary>原子保存常亮选项。</summary>
    public async Task SaveKeepScreenOnAsync(bool value)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, value.ToString());
        File.Move(temporary, path, true);
    }
}
