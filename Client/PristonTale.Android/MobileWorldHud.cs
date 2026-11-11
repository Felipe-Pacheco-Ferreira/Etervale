using Android.Content;
using Android.Graphics;
using Android.Widget;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;

namespace PristonTale.Mobile;

internal sealed class MobileWorldHud
{
    private readonly Meter _life;
    private readonly Meter _mana;
    private readonly Meter _stamina;
    private readonly Meter _experience;

    public LinearLayout Status { get; }
    public TextView Target { get; }
    public Button Journal { get; }

    public MobileWorldHud(Context context)
    {
        Status = new LinearLayout(context) { Orientation = Orientation.Vertical };
        Status.SetMinimumWidth((int)(160 * (context.Resources?.DisplayMetrics?.Density ?? 1f)));
        _life = AddMeter("PV", Color.Rgb(177, 53, 45));
        _mana = AddMeter("PM", Color.Rgb(61, 116, 201));
        _stamina = AddMeter("RES", Color.Rgb(86, 146, 63));
        _experience = AddMeter("EXP", Color.Rgb(199, 154, 62));

        Target = new TextView(context) { TextSize = 14, Visibility = global::Android.Views.ViewStates.Gone };
        Target.SetTextColor(Color.Rgb(246, 226, 181));
        MobileHudTheme.Card(Target);
        Target.SetPadding(12, 6, 12, 6);
        Journal = MobileHudTheme.Button(context, "Missões");

        Meter AddMeter(string caption, Color tint)
        {
            var label = new TextView(context) { Text = caption, TextSize = 11 };
            label.SetTextColor(Color.Rgb(241, 227, 199));
            var bar = new ProgressBar(context, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 1000 };
            bar.ProgressTintList = global::Android.Content.Res.ColorStateList.ValueOf(tint);
            Status.AddView(label);
            Status.AddView(bar);
            return new Meter(caption, label, bar);
        }
    }

    public void UpdateOwner(OwnerState owner)
    {
        _life.Set(owner.Life, owner.MaxLife);
        _mana.Set(owner.Mana, owner.MaxMana);
        _stamina.Set(owner.Stamina, owner.MaxStamina);
        var gained = owner.Experience >= owner.LevelStart ? owner.Experience - owner.LevelStart : 0;
        var needed = owner.NextLevel > owner.LevelStart ? owner.NextLevel - owner.LevelStart : 0;
        _experience.Set(gained, needed);
    }

    public void UpdateQuest(QuestView view)
    {
        Journal.Text = view.KillsNeeded > 0 ? $"Missões · {view.TargetName} {view.Kills}/{view.KillsNeeded}" : "Missões";
    }

    public void SetTarget(string name, int life, int maxLife)
    {
        Target.Text = $"{name}   {life}/{maxLife}";
        Target.Visibility = global::Android.Views.ViewStates.Visible;
    }

    public void ClearTarget() => Target.Visibility = global::Android.Views.ViewStates.Gone;

    private sealed record Meter(string Caption, TextView Label, ProgressBar Bar)
    {
        public void Set(double value, double max)
        {
            Label.Text = $"{Caption}  {value:0}/{max:0}";
            Bar.Progress = max > 0 ? (int)Math.Clamp(value / max * Bar.Max, 0d, Bar.Max) : 0;
        }
    }
}
