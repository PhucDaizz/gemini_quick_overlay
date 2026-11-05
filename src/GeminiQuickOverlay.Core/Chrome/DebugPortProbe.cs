using System.Net;
using System.Net.Sockets;

namespace GeminiQuickOverlay.Core.Chrome;

public static class DebugPortProbe
{
    public static bool IsOpen(int port, int timeoutMs = 500)
    {
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            try
            {
                using var client = new TcpClient(address.AddressFamily);
                var task = client.ConnectAsync(address, port);
                if (task.Wait(timeoutMs) && client.Connected)
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }
}
