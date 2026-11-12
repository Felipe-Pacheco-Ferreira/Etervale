using Android.App;
using Android.Widget;
using PristonTale.Client.Net;
using PristonTale.Mobile.CharacterSelect;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Skills;

namespace PristonTale.Mobile;

internal sealed class MobileShortcuts
{
    private readonly Button[] _belt = new Button[PotionBelt.Boxes];
    private readonly Button _skill;
    private readonly Activity _activity;
    private readonly IPlayChannel _play;
    private readonly TextView _notice;
    private readonly CancellationToken _ct;
    private OwnerState? _owner;
    private uint _selectedSkill;
    private uint _sequence;
    private bool _pending;
    private ulong _target;

    public LinearLayout View { get; }

    public MobileShortcuts(Activity activity, IPlayChannel play, TextView notice, CancellationToken ct)
    {
        _activity = activity;
        _play = play;
        _notice = notice;
        _ct = ct;
        View = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        for (var index = 0; index < _belt.Length; index++)
        {
            var box = index;
            var button = AddButton($"{box + 1}", HudGlyph.Potion);
            button.Enabled = false;
            button.Click += async (_, _) => await UseBeltAsync(box);
            _belt[index] = button;
        }
        _skill = AddButton("Habilidade", HudGlyph.Skill);
        _skill.Click += async (_, _) => await CastAsync();
        _skill.LongClick += (_, args) =>
        {
            args.Handled = true;
            ChooseSkill();
        };
    }

    public void Update(OwnerState owner)
    {
        _owner = owner;
        for (var index = 0; index < _belt.Length; index++)
        {
            var item = OwnedItemView.InBox(owner.Items, index);
            var label = item is null ? $"{index + 1}" : $"◊{item.Count}";
            _belt[index].ContentDescription = item is null ? $"PoÁ„o {index + 1}" : $"{item.Name}, {item.Count}";
            if (_belt[index].Text != label)
            {
                _belt[index].Text = label;
            }
            _belt[index].Enabled = item is not null && owner.Life > 0;
        }
        if (_selectedSkill != 0 && !owner.Skills.Any(skill => skill.Code == _selectedSkill && skill.Rank > 0))
        {
            _selectedSkill = 0;
            _skill.Text = "Habilidade";
        }
    }

    public void SetTarget(ulong target) => _target = target;

    private Button AddButton(string label, HudGlyph glyph)
    {
        var button = MobileHudTheme.Button(_activity, label);
        MobileHudTheme.Icon(button, glyph, stacked: true);
        button.SetPadding(2, 2, 2, 2);
        button.TextSize = 9;
        button.SetSingleLine(true);
        button.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        var density = _activity.Resources!.DisplayMetrics!.Density;
        View.AddView(button, new LinearLayout.LayoutParams((int)(glyph == HudGlyph.Skill ? 68 * density : 42 * density), (int)(44 * density)) { LeftMargin = (int)(3 * density) });
        return button;
    }

    private void ChooseSkill()
    {
        if (_owner is not { Life: > 0 } owner)
        {
            return;
        }

        var options = owner.Skills.Where(skill => skill.Rank > 0 && SkillRuleCatalog.TryGet(skill.Code, out _)).ToArray();
        if (options.Length == 0)
        {
            _notice.Text = "Nenhuma habilidade aprendida.";
            return;
        }

        using var builder = new AlertDialog.Builder(_activity);
        builder.SetTitle("Escolher habilidade");
        builder.SetItems(options.Select(skill => SkillName(skill.Code)).ToArray(), (_, args) =>
        {
            _selectedSkill = options[args.Which].Code;
            _skill.Text = SkillName(_selectedSkill);
        });
        builder.SetNegativeButton("Fechar", (_, _) => { });
        builder.Show();
    }

    private static string SkillName(uint code) => SkillRuleCatalog.TryGet(code, out var rule) ? rule.Name : "Habilidade";

    private async Task CastAsync()
    {
        if (_owner is not { Life: > 0 })
        {
            return;
        }

        if (!SkillRuleCatalog.TryGet(_selectedSkill, out var rule))
        {
            ChooseSkill();
            return;
        }

        if (rule.Target == SkillTarget.Enemy && _target == 0)
        {
            _notice.Text = "Selecione um monstro com Atacar.";
            return;
        }

        await SendAsync(() => _play.SendSkillCastAsync(++_sequence, rule.Code, rule.Target == SkillTarget.Enemy ? _target : 0, null, _ct));
    }

    private async Task UseBeltAsync(int box)
    {
        if (_owner is not { Life: > 0 } owner || OwnedItemView.InBox(owner.Items, box) is not { } item)
        {
            return;
        }

        await SendAsync(async () =>
        {
            var result = await _play.UseItemAsync(new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, item.InstanceId), _ct);
            if (!result.Ok)
            {
                _notice.Text = CharSelectText.Format(result.Message);
            }
        });
    }

    private async Task SendAsync(Func<Task> send)
    {
        if (_pending)
        {
            return;
        }

        _pending = true;
        try
        {
            await send();
        }
        catch (OperationCanceledException) when (_ct.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            _notice.Text = "N√£o foi poss√≠vel usar o atalho.";
        }
        finally
        {
            _pending = false;
        }
    }
}
