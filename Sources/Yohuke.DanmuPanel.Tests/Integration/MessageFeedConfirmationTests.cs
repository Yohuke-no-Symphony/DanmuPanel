using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuPanel.Models.Messages;
using Yohuke.DanmuPanel.Services;
using Yohuke.DanmuPanel.Tests.Messages;
using Yohuke.DanmuPanel.Views;
using Yohuke.DanmuPanel.Views.Dialogs;
using Xunit;

namespace Yohuke.DanmuPanel.Tests.Integration;

/// <summary>验证共享消息面板通过宿主确认服务工作，不依赖桌面窗口。</summary>
public sealed class MessageFeedConfirmationTests : IConfirmationService
{
    private readonly TaskCompletionSource<bool> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Control? source;
    private string message = "";

    /// <summary>没有桌面窗口时仍等待宿主确认，且只清理确认时指定的房间。</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HostConfirmationRetainsOriginalRoomWithoutDesktopWindow(bool accepted)
    {
        using var directory = new MessageStoreTestDirectory();
        await using var runtime = new PanelRuntime(new(directory.Path), new());
        await runtime.Messages.AppendAsync(new() { RoomTabId = "first", Kind = MessageKind.Danmu, Content = "first" }, new());
        await runtime.Messages.AppendAsync(new() { RoomTabId = "second", Kind = MessageKind.Danmu, Content = "second" }, new());
        var errors = new List<Exception>();
        var view = new MessageFeedView(this, runtime, MessageKind.Danmu, errors.Add);
        try
        {
            await view.Model.SelectRoomAsync(new() { Id = "first", Name = "原房间" });
            var clearing = view.Header.ClearCommand.ExecuteAsync(null);
            Assert.Same(view, source);
            Assert.Contains("原房间", message);
            Assert.False(clearing.IsCompleted);
            await view.Model.SelectRoomAsync(new() { Id = "second", Name = "新房间" });
            response.SetResult(accepted);
            await clearing;
            Assert.Equal(accepted ? 0 : 1, (await runtime.Messages.LoadPageAsync("first", MessageKind.Danmu, 0, 10)).Count);
            Assert.Single(await runtime.Messages.LoadPageAsync("second", MessageKind.Danmu, 0, 10));
            Assert.Equal("second", Assert.Single(view.Model.Items).Record.RoomTabId);
            Assert.Empty(errors);
        }
        finally
        {
            view.Model.Dispose();
        }
    }

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(Control source, string title, string message)
    {
        this.source = source;
        this.message = message;
        return response.Task;
    }
}
