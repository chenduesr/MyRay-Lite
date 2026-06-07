using System.Diagnostics;
using System.Net.Sockets;

namespace V2RayLite.Core;

public sealed class DelayTestService
{
    public async Task<int?> TestTcpDelayAsync(ProxyNode node, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            using var client = new TcpClient();
            var stopwatch = Stopwatch.StartNew();
            await client.ConnectAsync(node.Address, node.Port, timeoutCts.Token);
            stopwatch.Stop();
            return (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue);
        }
        catch
        {
            return null;
        }
    }

    public async Task TestManyAsync(IEnumerable<ProxyNode> nodes, Action<ProxyNode>? onNodeUpdated = null, CancellationToken cancellationToken = default)
    {
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            node.Status = NodeStatus.Testing;
            onNodeUpdated?.Invoke(node);

            var delay = await TestTcpDelayAsync(node, TimeSpan.FromSeconds(5), cancellationToken);
            node.DelayMs = delay;
            node.Status = delay is null ? NodeStatus.Unavailable : NodeStatus.Available;
            node.LastTested = DateTimeOffset.Now;
            onNodeUpdated?.Invoke(node);
        }
    }
}
