using System.Net;
using System.Net.WebSockets;

namespace PristonTale.Protocol.Transport;

// The same frames over a WebSocket, for clients that cannot open raw TCP (the browser). Each flush of the connection's
// writer leaves as one binary message; the reader treats the messages as one continuous byte stream, so a frame may
// span messages. "wss" carries TLS itself (the browser checks the certificate); plain "ws" is accepted only on this
// machine, or behind a proxy that terminates TLS.
public static class WebSocketTransport
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    public static bool Carries(Uri address) => address.Scheme == Uri.UriSchemeWss || address.Scheme == Uri.UriSchemeWs;

    public static async Task<FrameConnection> ConnectAsync(Uri address, CancellationToken cancellationToken, FrameConnectionOptions? options = null)
    {
        var socket = new ClientWebSocket();
        try
        {
            if (!OperatingSystem.IsBrowser() && address.Scheme == Uri.UriSchemeWss && address.Query.Length > 0 &&
                ServerCertificatePolicy.FromAddress(address) is { PinnedSha256: not null } policy)
            {
                socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, errors) => policy.Accepts(certificate, errors);
                address = new UriBuilder(address) { Query = "" }.Uri;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TlsTransport.DefaultHandshakeTimeout);
            await socket.ConnectAsync(address, deadline.Token);
            return Wrap(socket, new DnsEndPoint(address.Host, address.Port), null, options);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // A WebSocket the server accepted; the endpoints are the request's, after any trusted proxy's forwarding.
    public static FrameConnection Accept(WebSocket socket, EndPoint? remote, EndPoint? local, FrameConnectionOptions? options = null) =>
        Wrap(socket, remote, local, options);

    private static FrameConnection Wrap(WebSocket socket, EndPoint? remote, EndPoint? local, FrameConnectionOptions? options)
    {
        var stream = WebSocketStream.Create(socket, WebSocketMessageType.Binary, CloseTimeout);
        return new FrameConnection(stream, new TransportLink(remote, local, socket.Abort), options);
    }
}
