using System.Net.NetworkInformation;

namespace V2RayLite.Core;

public sealed class NetworkTrafficService
{
    public NetworkTrafficSnapshot Sample()
    {
        long upload = 0;
        long download = 0;

        foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (item.OperationalStatus != OperationalStatus.Up ||
                item.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            try
            {
                var stats = item.GetIPv4Statistics();
                upload += Math.Max(0, stats.BytesSent);
                download += Math.Max(0, stats.BytesReceived);
            }
            catch
            {
                // Some virtual adapters do not expose IPv4 counters reliably.
            }
        }

        return new NetworkTrafficSnapshot
        {
            UploadBytes = upload,
            DownloadBytes = download,
            Timestamp = DateTimeOffset.Now
        };
    }
}