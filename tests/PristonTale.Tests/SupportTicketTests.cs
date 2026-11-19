using System.Text;
using PristonTale.Application.Chat;
using PristonTale.Application.Support;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Tests;

internal static class SupportTicketTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("tickets: a ticket is a conversation; its player edits or cancels it until the staff answers, the staff closes and reopens it", StorageAsync);
        yield return ("tickets: tickets of the single-answer format read as a conversation", LegacyAsync);
        yield return ("tickets: the support window's commands list, show, open, answer, edit and cancel tickets", ChatAsync);
        yield return ("tickets: the support window reads the list and the ticket the server sends", WindowAsync);
    }

    private static async Task WithStoreAsync(Func<SupportTickets, string, Task> run)
    {
        var folder = Path.Combine(Path.GetTempPath(), "pt-tickets-" + Guid.NewGuid().ToString("N"));
        try
        {
            await run(new SupportTickets(folder), folder);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static Task StorageAsync() => WithStoreAsync(async (store, _) =>
    {
        var ct = CancellationToken.None;
        var id = await store.CreateAsync(7, "[Melhoria] Gráfico", "Suporte nativo no PC", "ana (Zephira)", ct);
        var ticket = (await store.FindAsync(7, id, ct))!;
        Assert.True(ticket.Category == "Melhoria" && ticket.Subject == "Gráfico" && ticket.AuthorName == "ana (Zephira)",
            "the category prefix becomes the category");
        Assert.True(ticket.Status == TicketStatus.Open && ticket.Editable && ticket.Messages.Count == 1 && ticket.Number is >= 1000 and <= 9999,
            "a new ticket is open, editable, numbered, and its message starts the conversation");

        Assert.True(await store.EditAsync(7, id, "[Bug] Gráfico quebrado", "Texto novo", ct), "its player edits it before any answer");
        Assert.True(await store.AddMessageAsync(7, id, TicketAuthor.Player, "ana (Zephira)", "Mais detalhes", ct) is not null, "its player adds a message");
        ticket = (await store.FindAsync(7, id, ct))!;
        Assert.True(ticket is { Category: "Bug", Subject: "Gráfico quebrado", Message: "Texto novo", Status: TicketStatus.Open } && ticket.Messages.Count == 2,
            "the edit changes the subject, category and first message");

        Assert.True(await store.ReplyAsync(7, id, "GM Bia", "Vamos olhar", ct), "the staff answers");
        ticket = (await store.FindAsync(7, id, ct))!;
        Assert.True(ticket.Status == TicketStatus.Answered && !ticket.Editable && ticket.Messages[^1] is { Author: TicketAuthor.Staff, Name: "GM Bia" },
            "an answered ticket is no longer editable");
        Assert.True(!await store.EditAsync(7, id, "[Bug] x", "y", ct) && !await store.CancelAsync(7, id, ct),
            "its player can no longer edit or cancel it");
        Assert.True(await store.AddMessageAsync(7, id, TicketAuthor.Player, "ana (Zephira)", "Obrigada", ct) is not null
            && (await store.FindAsync(7, id, ct))!.Status == TicketStatus.Open, "a player's message after the answer waits for the staff again");

        Assert.True(await store.CloseAsync(7, id, "GM Bia", ct) && !await store.CloseAsync(7, id, "GM Bia", ct), "the staff closes it once");
        ticket = (await store.FindAsync(7, id, ct))!;
        Assert.True(ticket is { Status: TicketStatus.Closed, ClosedByPlayer: false } && ticket.ClosedBy == SupportTickets.ClosedByStaffPrefix + "GM Bia",
            "a closed ticket keeps who closed it");
        Assert.True(await store.AddMessageAsync(7, id, TicketAuthor.Player, "ana", "x", ct) is null && !await store.ReplyAsync(7, id, "GM", "x", ct),
            "a closed ticket takes no message");
        Assert.True(!await store.AttachAsync(7, id, new MemoryStream([1, 2, 3]), ".png", ct), "a closed ticket takes no attachment");
        Assert.True(store.Reopen(7, id) && (await store.FindAsync(7, id, ct))!.Status == TicketStatus.Open && !store.Reopen(7, id),
            "the staff reopens a closed ticket");

        var other = await store.CreateAsync(7, "Outra coisa", "Desisti", "ana (Zephira)", ct);
        Assert.True(await store.CancelAsync(7, other, ct) && (await store.FindAsync(7, other, ct))!.ClosedByPlayer, "its player cancels an unanswered ticket");
        var queue = await store.QueueAsync(ct);
        Assert.True(queue.Count == 2 && queue[0].Ticket.Id == id && queue[1].Ticket.Closed, "the queue lists tickets waiting for the staff before closed ones");
    });

    private static Task LegacyAsync() => WithStoreAsync(async (store, folder) =>
    {
        var id = Guid.NewGuid();
        var account = Path.Combine(folder, "3");
        Directory.CreateDirectory(account);
        await File.WriteAllTextAsync(Path.Combine(account, id.ToString("N") + ".json"),
            $"{{\"Id\":\"{id}\",\"CreatedAt\":\"2026-11-10T10:00:00+00:00\",\"Subject\":\"Antigo\",\"Message\":\"Pergunta\",\"Reply\":null}}");
        await File.WriteAllTextAsync(Path.Combine(account, id.ToString("N") + ".reply"), "Resposta antiga");
        var ticket = (await store.ListAsync(3, CancellationToken.None)).Single();
        Assert.True(ticket.Messages.Count == 2 && ticket.Messages[1] is { Author: TicketAuthor.Staff, Name: SupportTickets.StaffName, Text: "Resposta antiga" }
            && ticket.Status == TicketStatus.Answered && ticket.Category.Length == 0, "the old answer is the staff's message");
    });

    private static Task ChatAsync() => WithStoreAsync(async (store, _) =>
    {
        var fixture = new ChatFixture();
        var player = await fixture.JoinAsync("Zephira", 0, 0);
        var chat = new ChatAppService(fixture.Directory, fixture.World, fixture.GameMasters) { Tickets = store, NowMs = () => fixture.NowMs };
        async Task<string[]> SayAsync(params string[] lines)
        {
            player.Link.Clear();
            foreach (var line in lines)
            {
                await fixture.RunAsync(chat.SayAsync(player.Id, line));
            }

            return player.Link.Texts().Where(static text => text.StartsWith("Ticket> ", StringComparison.Ordinal)).Select(static text => text[8..]).ToArray();
        }

        static string Part(string text) => "/ticketpart " + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        var saved = await SayAsync("/ticketstart [Melhoria] Gráfico", Part("Suporte nativo "), Part("no PC"), "/ticketsave");
        Assert.True(saved.Single().StartsWith("saved ", StringComparison.Ordinal), "a ticket sent in parts is saved");
        var id = saved[0][6..];

        var list = await SayAsync("/ticketlist");
        var ticket = (await store.ListAsync(player.Member.AccountId, CancellationToken.None)).Single();
        Assert.True(list.Length == 3 && list[0] == "list" && list[2] == "listend"
            && list[1].StartsWith($"item {id} {ticket.Number} Open Melhoria ", StringComparison.Ordinal) && list[1].EndsWith(" 1 1 Gráfico", StringComparison.Ordinal),
            $"the list names each ticket with its number, state, category, messages and subject ({string.Join(" | ", list)})");
        Assert.True(ticket.AuthorName == $"{player.Member.UserId} (Zephira)", "the ticket keeps its account and character");

        var view = await SayAsync("/ticketview " + id);
        Assert.True(view[0].StartsWith($"view {id} {ticket.Number} Open Melhoria ", StringComparison.Ordinal) && view[0].EndsWith(" 1 " + ticket.AuthorName, StringComparison.Ordinal)
            && view[1] == "subject Gráfico" && view[2].StartsWith("msg P ", StringComparison.Ordinal) && view[3] == "text Suporte nativo no PC" && view[^1] == "viewend",
            $"the view carries the header, subject and each message ({string.Join(" | ", view)})");

        Assert.True((await SayAsync("/ticketedit " + id + " [Bug] Tela preta", Part("Texto editado"), "/ticketsave")).Single() == "edited " + id,
            "its player edits it");
        Assert.True((await SayAsync("/ticketreply " + id, Part("Mais detalhes"), "/ticketsave")).Single() == "replied " + id, "its player answers");
        await store.ReplyAsync(player.Member.AccountId, Guid.ParseExact(id, "N"), "GM Bia", "Corrigido", CancellationToken.None);
        Assert.True((await SayAsync("/ticketclose " + id)).Single() == "error", "an answered ticket cannot be cancelled by its player");
        Assert.True((await SayAsync("/ticketview " + id)).Contains("text Corrigido"), "the staff's answer reaches the window");

        var other = (await SayAsync("/ticketstart [Outro] Duvida", Part("Desisti"), "/ticketsave")).Single()[6..];
        Assert.True((await SayAsync("/ticketclose " + other)).Single() == "closed " + other, "its player cancels an unanswered ticket");
        Assert.True((await SayAsync("/ticketlist")).Any(line => line.StartsWith($"item {other} ", StringComparison.Ordinal) && line.Contains(" Cancelled ", StringComparison.Ordinal)),
            "a cancelled ticket stays listed as cancelled");
        Assert.True((await SayAsync("/ticketpart " + Convert.ToBase64String("x"u8.ToArray()))).Single() == "error", "a part without a start is refused");

        var legacy = await SayAsync("/tickets");
        Assert.True(legacy[0] == "begin" && legacy[^1] == "end" && legacy.All(static line => line is "begin" or "end" || line.StartsWith("line ", StringComparison.Ordinal)),
            "the plain list of earlier windows is unchanged");
    });

    private static Task WindowAsync()
    {
        var id = Guid.NewGuid();
        var item = HudNews.ParseItem($"{id:N} 5388 Answered Melhoria 1763437620 1763437680 3 0 MELHORIA NO GRAFICO");
        Assert.True(item is { Number: "#5388", Status: "Answered", Category: "Melhoria", Subject: "MELHORIA NO GRAFICO" } && item.Id == id
            && item.Details.Contains("3 ", StringComparison.Ordinal), $"a list item is read with its subject and details ({item})");
        Assert.True(HudNews.ParseItem("broken") is null, "a broken item is left out");

        var wrapped = HudNews.WrapBold("A **poção da Black Friday** já pode", 60, static text => text.Length * 6);
        Assert.True(wrapped.SequenceEqual(["A **poção da**", "**Black**", "**Friday** já", "pode"]),
            $"a bold run broken across lines stays bold on each line and is measured without its marks ({string.Join(" | ", wrapped)})");

        var window = new HudNews();
        window.ReceiveTicket("Ticket> list");
        window.ReceiveTicket($"Ticket> item {id:N} 5388 Answered Melhoria 1763437620 1763437680 3 0 MELHORIA NO GRAFICO");
        window.ReceiveTicket($"Ticket> item {Guid.NewGuid():N} 5389 Open Bug 1763437620 1763437680 1 0 OUTRO");
        window.ReceiveTicket("Ticket> listend");
        var unseen = new PristonTale.Engine3D.Rendering.ClientSettings();
        Assert.True(window.UpdateNotices(unseen) is null && window.Unread == 1, "an answered ticket not read marks the News & Support plaque");
        Assert.True(window.UpdateNotices(unseen with { TicketsSeen = [item!.SeenKey] }) is null && window.Unread == 0, "a read answer marks nothing");
        Assert.True(HudNews.ParseItem($"{id:N} 5388 Answered Melhoria 1763437620 1763437999 4 0 X")!.SeenKey != item.SeenKey,
            "a new answer on the same ticket marks it again");
        var detail = HudNews.ParseView($"{id:N} 5388 Open - 1763437620 1 SatroX (Zephira)");
        Assert.True(detail is { Number: "#5388", Status: "Open", Category: "", Editable: true } && detail.Id == id
            && detail.Opened.EndsWith("SatroX (Zephira)", StringComparison.Ordinal), "a ticket's header is read with who opened it");
        Assert.True(SupportTickets.AttachmentExtension("Foto.JPEG") == ".jpg" && SupportTickets.AttachmentExtension("clip.webm") == ".webm"
            && SupportTickets.AttachmentExtension("sem-extensao") == "", "an uploaded file is not kept under its lower-case extension, .jpeg as .jpg");

        var page = new HudNews();
        Assert.True(!page.PageAction("send", "", " ", 0, "texto") && page.PageState().Contains("\"state\":\"invalid\"", StringComparison.Ordinal),
            "the page's form sent a ticket without a subject");
        Assert.True(!page.PageAction("unknown", "", "", 0, "") && !page.PageAction("cancel", "", "", 0, ""), "the page ran an unknown action or one without its ticket");
        return Task.CompletedTask;
    }
}
