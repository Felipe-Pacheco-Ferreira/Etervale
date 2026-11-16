using System.Net;
using System.Net.Sockets;

namespace PristonTale.Protocol.Transport;

// The carrier under a FrameConnection's stream: its endpoints, captured when it opened (a closed Socket throws for
// them), and how to tear it down at once when the connection closes.
public sealed record TransportLink(EndPoint? RemoteEndPoint, EndPoint? LocalEndPoint, Action Abort)
{
    public static TransportLink Of(Socket socket)
    {
        socket.NoDelay = true;
        return new TransportLink(socket.RemoteEndPoint, socket.LocalEndPoint, () => Shutdown(socket));
    }

    private static void Shutdown(Socket socket)
    {
        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
        }

        socket.Close();
    }
}
