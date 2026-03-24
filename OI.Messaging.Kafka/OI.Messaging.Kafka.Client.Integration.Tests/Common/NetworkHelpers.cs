using System.Net;
using System.Net.Sockets;

namespace OI.Messaging.Kafka.Client.Integration.Tests.Common;

internal static class NetworkHelpers
{
    public static int GetAvailablePort()
    {
        IPEndPoint defaultLoopbackEndpoint = new(IPAddress.Loopback, 0);

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(defaultLoopbackEndpoint);
        int port = ((IPEndPoint)socket.LocalEndPoint!).Port;

        return port;
    }
}
