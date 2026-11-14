namespace Yohuke.DanmuPanel.iPad.Tests;

internal sealed class DeferredReadStream(byte[] bytes) : MemoryStream(bytes, false)
{
    private readonly TaskCompletionSource readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task ReadStarted => readStarted.Task;

    internal void Release()
    {
        release.TrySetResult();
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        readStarted.TrySetResult();
        await release.Task.WaitAsync(cancellationToken);
        return await base.ReadAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        Release();
        base.Dispose(disposing);
    }
}
