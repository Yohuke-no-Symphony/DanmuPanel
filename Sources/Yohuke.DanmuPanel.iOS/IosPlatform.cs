using Foundation;
using UIKit;
using Yohuke.DanmuPanel.iPad.Services;

namespace Yohuke.DanmuPanel.iOS;

internal sealed class IosPlatform : IIpadPlatform
{
    private bool keepScreenOn;
    private bool isActive;

    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yohuke.DanmuPanel");

    public Version Version { get; } = System.Version.Parse(
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")!.ToString()!);

    /// <inheritdoc />
    public void SetKeepScreenOn(bool enabled)
    {
        keepScreenOn = enabled;
        ApplyKeepScreenOn();
    }

    internal void SetActive(bool active)
    {
        isActive = active;
        ApplyKeepScreenOn();
    }

    private void ApplyKeepScreenOn()
    {
        UIApplication.SharedApplication.IdleTimerDisabled = isActive && keepScreenOn;
    }
}
