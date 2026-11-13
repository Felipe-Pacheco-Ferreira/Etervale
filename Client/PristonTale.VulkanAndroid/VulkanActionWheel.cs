using Android.App;
using Android.Widget;
using PristonTale.Mobile;
using PristonTale.Mobile.CharacterSelect;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Skills;

// The combat controls at the bottom right: the large attack button, the skill buttons on an arc around it and the
// potion belt beside them. The skill buttons fill with the learned skills in order (a slot without one is dimmed); a long press on one picks another
// skill for it. A skill that needs an enemy is cast at the current target, or asks for one. Belt and skill use are the
// commands the server validates.
internal sealed class VulkanActionWheel
{
    private const int SkillSlots = 4;
    // The wheel's box, the attack button and the arc, in fractions of the screen height.
    private const float BoxWidth = .7f;
    private const float BoxHeight = .56f;
    private const float AttackRadius = .13f;
    private const float AttackInset = .16f;
    private const float ArcRadius = .25f;
    private const float SkillRadius = .066f;
    private const float PotionRadius = .052f;
    private static readonly float[] SkillAngles = [184f, 146f, 109f, 72f];

    private readonly Activity _activity;
    private readonly VulkanWorldSession _session;
    private readonly VulkanItemIcons _icons;
    private readonly TextView _notice;
    private readonly CancellationToken _ct;
    private readonly VulkanActionOrb[] _skills = new VulkanActionOrb[SkillSlots];
    private readonly uint[] _skillCodes = new uint[SkillSlots];
    private readonly bool[] _chosen = new bool[SkillSlots];
    private readonly VulkanActionOrb[] _belt = new VulkanActionOrb[PotionBelt.Boxes];
    private OwnerState _owner;
    private uint _sequence;
    private bool _pending;
    // The wheel's distance from the screen's right and bottom edges, in pixels.
    private readonly int _right;
    private readonly int _bottom;

    public FrameLayout View { get; }
    public VulkanActionOrb Attack { get; }
    public IReadOnlyList<VulkanActionOrb> Skills => _skills;
    public IReadOnlyList<VulkanActionOrb> Potions => _belt;
    public int BoxPixelsWide { get; }
    public int BoxPixelsHigh { get; }

    public VulkanActionWheel(Activity activity, VulkanWorldSession session, VulkanItemIcons icons, TextView notice, float screenHeight, int right, int bottom,
        CancellationToken ct)
    {
        _right = right;
        _bottom = bottom;
        _activity = activity;
        _session = session;
        _icons = icons;
        _notice = notice;
        _ct = ct;
        var h = screenHeight;
        BoxPixelsWide = (int)(BoxWidth * h);
        BoxPixelsHigh = (int)(BoxHeight * h);
        View = new FrameLayout(activity);
        var centerX = (BoxWidth - AttackInset) * h;
        var centerY = (BoxHeight - AttackInset) * h;

        Attack = new VulkanActionOrb(activity, HudGlyph.Attack, prominent: true) { ContentDescription = "Atacar" };
        Attack.Click += (_, _) => session.AttackSelected();
        Place(Attack, centerX, centerY, AttackRadius * h);

        for (var slot = 0; slot < SkillSlots; slot++)
        {
            var index = slot;
            var angle = SkillAngles[slot] * MathF.PI / 180f;
            var orb = new VulkanActionOrb(activity, HudGlyph.Skill) { ContentDescription = $"Habilidade {slot + 1}" };
            orb.Click += async (_, _) => await CastAsync(index);
            orb.LongClick += (_, args) =>
            {
                args.Handled = true;
                ChooseSkill(index);
            };
            Place(orb, centerX + MathF.Cos(angle) * ArcRadius * h, centerY - MathF.Sin(angle) * ArcRadius * h, SkillRadius * h);
            _skills[slot] = orb;
        }

        for (var box = 0; box < _belt.Length; box++)
        {
            var index = box;
            var orb = new VulkanActionOrb(activity, HudGlyph.Potion) { Enabled = false };
            orb.Click += async (_, _) => await UseBeltAsync(index);
            Place(orb, PotionRadius * 1.15f * h, (BoxHeight - PotionRadius * 1.1f - box * PotionRadius * 2.2f) * h, PotionRadius * h);
            _belt[box] = orb;
        }

        icons.Loaded += () =>
        {
            if (_owner is not null)
            {
                Update(_owner);
            }
        };
    }

    public void Update(OwnerState owner)
    {
        _owner = owner;
        for (var box = 0; box < _belt.Length; box++)
        {
            var item = OwnedItemView.InBox(owner.Items, box);
            _belt[box].SetPicture(item is null ? null : _icons.Get(item.CatalogKey));
            _belt[box].SetCaption(item is null ? $"{box + 1}" : "", item is null ? "" : item.Count.ToString());
            _belt[box].ContentDescription = item is null ? $"Poção {box + 1}" : $"{item.Name}, {item.Count}";
            _belt[box].Enabled = item is not null && owner.Life > 0;
        }

        var learned = owner.Skills.Where(skill => skill.Rank > 0 && SkillRuleCatalog.TryGet(skill.Code, out _)).Select(skill => skill.Code).ToArray();
        var taken = new HashSet<uint>();
        for (var slot = 0; slot < SkillSlots; slot++)
        {
            _chosen[slot] &= learned.Contains(_skillCodes[slot]);
            if (_chosen[slot])
            {
                taken.Add(_skillCodes[slot]);
            }
        }

        var next = 0;
        for (var slot = 0; slot < SkillSlots; slot++)
        {
            if (!_chosen[slot])
            {
                while (next < learned.Length && taken.Contains(learned[next]))
                {
                    next++;
                }

                _skillCodes[slot] = next < learned.Length ? learned[next++] : 0;
            }

            _skills[slot].SetCaption(_skillCodes[slot] == 0 ? "" : Short(SkillName(_skillCodes[slot])));
            // A slot with no learned skill to hold stays in its place, dimmed.
            _skills[slot].Enabled = owner.Life > 0 && _skillCodes[slot] != 0;
        }
    }

    // Each button is placed from the screen's lower right corner inside a layer that spans the screen, so the HUD editor can
    // move any of them anywhere and it still takes touches there (a view outside its parent's bounds gets none).
    private void Place(Android.Views.View orb, float x, float y, float radius)
    {
        var size = (int)(radius * 2f);
        View.AddView(orb, new FrameLayout.LayoutParams(size, size, Android.Views.GravityFlags.Bottom | Android.Views.GravityFlags.Right)
        {
            RightMargin = _right + BoxPixelsWide - (int)(x + radius),
            BottomMargin = _bottom + BoxPixelsHigh - (int)(y + radius)
        });
    }

    private void ChooseSkill(int slot)
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
        builder.SetTitle($"Habilidade do botão {slot + 1}");
        builder.SetItems(options.Select(skill => SkillName(skill.Code)).ToArray(), (_, args) =>
        {
            _skillCodes[slot] = options[args.Which].Code;
            _chosen[slot] = true;
            Update(owner);
        });
        builder.SetNegativeButton("Fechar", (_, _) => { });
        builder.Show();
    }

    private static string SkillName(uint code) => SkillRuleCatalog.TryGet(code, out var rule) ? rule.Name : "Habilidade";

    // The first word of a skill's name, cut to fit a button.
    private static string Short(string name)
    {
        var word = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
        return word.Length <= 7 ? word : word[..6] + "…";
    }

    private async Task CastAsync(int slot)
    {
        if (_owner is not { Life: > 0 })
        {
            return;
        }

        if (!SkillRuleCatalog.TryGet(_skillCodes[slot], out var rule))
        {
            ChooseSkill(slot);
            return;
        }

        var target = _session.AttackTarget;
        if (rule.Target == SkillTarget.Enemy && target == 0)
        {
            _session.AttackSelected();
            _notice.Text = "Escolha um alvo e use a habilidade de novo.";
            return;
        }

        await SendAsync(() => _session.Play.SendSkillCastAsync(++_sequence, rule.Code, rule.Target == SkillTarget.Enemy ? target : 0, null, _ct));
    }

    private async Task UseBeltAsync(int box)
    {
        if (_owner is not { Life: > 0 } owner || OwnedItemView.InBox(owner.Items, box) is not { } item)
        {
            return;
        }

        await SendAsync(async () =>
        {
            var result = await _session.Play.UseItemAsync(new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, item.InstanceId), _ct);
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
            _notice.Text = "Não foi possível usar o atalho.";
        }
        finally
        {
            _pending = false;
        }
    }
}
