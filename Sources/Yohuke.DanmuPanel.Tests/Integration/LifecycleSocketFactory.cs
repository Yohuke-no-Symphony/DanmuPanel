using Yohuke.DanmuCore.Services;

namespace Yohuke.DanmuPanel.Tests.Integration;

internal sealed class LifecycleSocketFactory : ILiveDanmuSocketFactory
{
    private readonly Dictionary<long, ReceptionTestSocket> sockets = new();
    internal HashSet<long> FailingRooms { get; } = [];

    internal ReceptionTestSocket GetSocket(long roomId)
    {
        if (!sockets.TryGetValue(roomId, out var socket))
        {
            socket = new();
            sockets.Add(roomId, socket);
        }
        return socket;
    }

    /// <inheritdoc/>
    public ILiveDanmuSocket Create(long roomId, string cookie)
    {
        if (FailingRooms.Contains(roomId))
        {
            throw new IOException("The test listener could not be created.");
        }
        return GetSocket(roomId);
    }
}
