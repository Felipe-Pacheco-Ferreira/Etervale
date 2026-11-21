using PristonTale.Application.World;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;

namespace PristonTale.Server.Net;

internal sealed partial class PresenceEncoder
{
    private TitleView? _titles;

    // The receiver's titles whenever the world published new ones (WorldServer.StepTitles).
    private void SendTitles(TitleView? titles, IPacketSink connection)
    {
        if (titles is null || ReferenceEquals(titles, _titles))
        {
            return;
        }

        _titles = titles;
        var length = new TitlesPacket((byte)titles.Shown, titles.Owned).Write(_scratch);
        connection.Send(_scratch.AsSpan(0, length));
    }
}
