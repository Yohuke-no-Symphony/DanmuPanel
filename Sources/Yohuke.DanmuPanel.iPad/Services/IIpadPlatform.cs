namespace Yohuke.DanmuPanel.iPad.Services;

/// <summary>提供移动宿主的数据位置、版本和屏幕常亮能力。</summary>
public interface IIpadPlatform
{
    string DataDirectory { get; }
    Version Version { get; }

    /// <summary>根据前台阅读状态启用或恢复系统自动锁屏。</summary>
    void SetKeepScreenOn(bool enabled);
}
