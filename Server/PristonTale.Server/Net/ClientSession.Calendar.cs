using PristonTale.Application.Calendar;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.World.Calendar;

namespace PristonTale.Server.Net;

internal sealed partial class ClientSession
{
    // The event calendar of a player in the world, in its language.
    private void SendEventCalendar(EventCalendarRequestPacket request)
    {
        if (_player is null || _session is not { } session)
        {
            return;
        }

        var packet = ToPacket(request.OperationId, EventCalendarQuery.For(_services.World, _services.Castle, session.Language));
        SendLarge(packet.Write, EventCalendarPacket.MaxSize);
    }

    // The calendar on the wire, its occurrences in whole minutes; what passes the wire's limits is left out.
    internal static EventCalendarPacket ToPacket(Guid operationId, EventCalendar calendar)
    {
        var bosses = new CalendarBossWire[Math.Min(calendar.Bosses.Count, EventCalendarWire.MaxBosses)];
        for (var index = 0; index < bosses.Length; index++)
        {
            bosses[index] = new CalendarBossWire(calendar.Bosses[index].Name, calendar.Bosses[index].FieldName);
        }

        var occurrences = new CalendarOccurrenceWire[Math.Min(calendar.Occurrences.Count, EventCalendarWire.MaxOccurrences)];
        for (var index = 0; index < occurrences.Length; index++)
        {
            var occurrence = calendar.Occurrences[index];
            occurrences[index] = CalendarOccurrenceWire.Between((byte)occurrence.Kind, (byte)occurrence.Flags, occurrence.Bosses, occurrence.Start, occurrence.End);
        }

        return new EventCalendarPacket(operationId, calendar.ExperiencePercent, (ushort)calendar.Drops, bosses, occurrences);
    }
}
