using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Mobile;
using PristonTale.Simulation.Parties;

// The party on the phone, through the same server requests as the PC, so phone and PC players party together: a
// "Convidar" button under the target frame while another player is the target, the members' plates (name, level, life)
// with the party's commands on a tap, and the request window (accept or decline, gone after PartyRules.InviteMs).
// Called on the UI thread.
internal sealed class VulkanPartyHud
{
    private readonly Activity _activity;
    private readonly FrameLayout _overlay;
    private readonly VulkanWorldSession _session;
    private readonly ulong _self;
    private readonly List<MemberPlate> _plates = [];
    private PartyInfo _shown;
    private VulkanPanel _request;
    private long _requestUntil;

    public VulkanPartyHud(Activity activity, FrameLayout overlay, VulkanWorldSession session)
    {
        _activity = activity;
        _overlay = overlay;
        _session = session;
        _self = session.Entered.EntityId;
        Invite = MobileHudTheme.Button(activity, "Convidar p/ grupo", prominent: true);
        Invite.TextSize = 12;
        Invite.Visibility = ViewStates.Gone;
        Invite.Click += async (_, _) =>
        {
            if (_session.Target is { Player: true } target)
            {
                Invite.Visibility = ViewStates.Gone;
                await Send(_session.InviteToPartyAsync(target.Id), $"Convite enviado para {target.Name}.");
            }
        };
        Members = new LinearLayout(activity) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
    }

    // The invite button, to place under the target frame.
    public Button Invite { get; }

    // The members' plates, one under another.
    public LinearLayout Members { get; }

    // Each HUD refresh: the invite button, the members and the requests.
    public void Refresh()
    {
        var party = _session.Party;
        var target = _session.Target;
        var inviting = target is { Player: true } && target.Id != _self && !party.Contains(target.Id)
            && (party.Members.Count == 0 || party.Leads(_self)) && party.Members.Count < PartyLimits.MaxMembers;
        Invite.Visibility = inviting ? ViewStates.Visible : ViewStates.Gone;
        if (!ReferenceEquals(party, _shown))
        {
            _shown = party;
            ShowMembers(party);
        }

        if (_request is not null && Android.OS.SystemClock.ElapsedRealtime() > _requestUntil)
        {
            CloseRequest();
        }

        if (_session.TryReadPartyRequest(out var request))
        {
            ShowRequest(request);
        }
    }

    private void ShowMembers(PartyInfo party)
    {
        var others = party.Members.Where(member => member.EntityId != _self).ToList();
        while (_plates.Count > others.Count)
        {
            Members.RemoveView(_plates[^1]);
            _plates.RemoveAt(_plates.Count - 1);
        }

        while (_plates.Count < others.Count)
        {
            var plate = new MemberPlate(_activity);
            plate.Click += (_, _) => Commands(plate.Member);
            Members.AddView(plate, new LinearLayout.LayoutParams(Dp(170), Dp(40)) { BottomMargin = Dp(4) });
            _plates.Add(plate);
        }

        for (var index = 0; index < others.Count; index++)
        {
            _plates[index].Show(others[index], party.Leads(others[index].EntityId));
        }

        Members.Visibility = others.Count > 0 ? ViewStates.Visible : ViewStates.Gone;
    }

    // A member's commands: leaving for anyone, and for the leader passing the lead, removing the member or ending the party.
    private void Commands(PartyMemberInfo member)
    {
        if (member is null)
        {
            return;
        }

        var party = _session.Party;
        var panel = new VulkanPanel(_activity, _overlay, "Grupo", 380);
        panel.Content.AddView(VulkanPanel.Body(_activity, $"{member.Name}  ·  Nível {member.Level}", 15),
            new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(10) });
        if (party.Leads(_self))
        {
            AddCommand(panel, "Passar liderança", PartyCommand.Delegate, member.EntityId, false);
            AddCommand(panel, "Expulsar do grupo", PartyCommand.Kick, member.EntityId, false);
            AddCommand(panel, "Desfazer o grupo", PartyCommand.Disband, 0, false);
        }

        AddCommand(panel, "Sair do grupo", PartyCommand.Leave, 0, true);
    }

    private void AddCommand(VulkanPanel panel, string text, PartyCommand command, ulong member, bool prominent)
    {
        var button = MobileHudTheme.Button(_activity, text, prominent);
        button.Click += async (_, _) =>
        {
            panel.Close();
            await Send(_session.CommandPartyAsync(command, member), null);
        };
        panel.Content.AddView(button, new LinearLayout.LayoutParams(-1, Dp(44)) { BottomMargin = Dp(6) });
    }

    private void ShowRequest(PartyRequestInfo request)
    {
        CloseRequest();
        _request = new VulkanPanel(_activity, _overlay, "Convite de grupo", 420);
        _requestUntil = Android.OS.SystemClock.ElapsedRealtime() + PartyRules.InviteMs;
        var line = request.Recommended.Length > 0
            ? $"{request.FromName} recomenda {request.Recommended} para o grupo."
            : $"{request.FromName} convida você para o grupo.";
        _request.Content.AddView(VulkanPanel.Body(_activity, line, 15), new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(12) });
        var row = new LinearLayout(_activity);
        _request.Content.AddView(row);
        var decline = MobileHudTheme.Button(_activity, "Recusar");
        var accept = MobileHudTheme.Button(_activity, "Aceitar", prominent: true);
        decline.Click += async (_, _) => await Answer(request.FromId, false);
        accept.Click += async (_, _) => await Answer(request.FromId, true);
        row.AddView(decline, new LinearLayout.LayoutParams(0, Dp(44), 1) { RightMargin = Dp(8) });
        row.AddView(accept, new LinearLayout.LayoutParams(0, Dp(44), 1));
    }

    private async Task Answer(ulong from, bool accept)
    {
        CloseRequest();
        await Send(_session.AnswerPartyAsync(from, accept), null);
    }

    private void CloseRequest()
    {
        if (_request is { } request)
        {
            _request = null;
            request.Close();
        }
    }

    private async Task Send(Task request, string done)
    {
        try
        {
            await request;
            if (done is not null)
            {
                Toast.MakeText(_activity, done, ToastLength.Short)?.Show();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            Toast.MakeText(_activity, "Não foi possível falar com o servidor.", ToastLength.Short)?.Show();
        }
    }

    private int Dp(int value) => (int)MathF.Round(value * (_activity.Resources?.DisplayMetrics?.Density ?? 1f));

    // A member's plate: the name and level over the life gauge, a crown before the leader's name.
    private sealed class MemberPlate(Context context) : View(context)
    {
        private readonly RectF _box = new();
        private string _name = "";
        private string _life = "";
        private float _fraction;

        public PartyMemberInfo Member { get; private set; }

        public void Show(PartyMemberInfo member, bool leader)
        {
            Member = member;
            _name = $"{(leader ? "♛ " : "")}{member.Name}  Nv {member.Level}";
            _fraction = member.MaxLife > 0 ? Math.Clamp(member.Life / (float)member.MaxLife, 0f, 1f) : 0f;
            _life = member.MaxLife > 0 ? $"{member.Life} / {member.MaxLife}" : "";
            ContentDescription = $"{member.Name}, nível {member.Level}";
            Invalidate();
        }

        protected override void OnDraw(Canvas canvas)
        {
            var width = Width;
            var height = Height;
            _box.Set(0, 0, width, height);
            VulkanHudArt.Plate(canvas, _box, height * .22f);
            VulkanHudArt.Label(canvas, _name, height * .2f, height * .3f, height * .28f, VulkanHudArt.EdgeLight, Paint.Align.Left!);
            _box.Set(height * .2f, height * .55f, width - height * .2f, height * .85f);
            VulkanHudArt.Bar(canvas, _box, VulkanHudArt.Life, _fraction, _fraction, _life, height * .22f);
        }
    }
}
