using Yohuke.DanmuPanel.iPad.Services;

namespace Yohuke.DanmuPanel.iPad.Tests;

internal sealed class TestIpadPlatform : IIpadPlatform, IDisposable
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "danmu-ipad-tests-" + Guid.NewGuid().ToString("N"));
    public Version Version { get; } = new(1, 2, 3);
    public bool KeepScreenOn { get; private set; }

    internal TestIpadPlatform()
    {
        Directory.CreateDirectory(DataDirectory);
    }

    /// <inheritdoc />
    public void SetKeepScreenOn(bool enabled)
    {
        KeepScreenOn = enabled;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(DataDirectory, true);
    }
}
