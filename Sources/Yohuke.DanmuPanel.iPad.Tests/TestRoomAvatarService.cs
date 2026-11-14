using Yohuke.DanmuPanel.Models.Workspace;
using Yohuke.DanmuPanel.Services;

namespace Yohuke.DanmuPanel.iPad.Tests;

internal sealed class TestRoomAvatarService : IRoomAvatarService
{
    internal List<(string RoomId, string OwnerUid, string? Cookie, CancellationToken CancellationToken)> Requests { get; } = [];
    internal Func<string, string, string?, CancellationToken, Task<RoomAvatarResult>> Resolve { get; init; } =
        (_, _, _, _) => Task.FromResult(new RoomAvatarResult());

    /// <inheritdoc />
    public Task<RoomAvatarResult> GetRoomAvatarResultAsync(string roomId, string ownerUid = "", string? cookie = "",
        CancellationToken cancellationToken = default)
    {
        Requests.Add((roomId, ownerUid, cookie, cancellationToken));
        return Resolve(roomId, ownerUid, cookie, cancellationToken);
    }
}
