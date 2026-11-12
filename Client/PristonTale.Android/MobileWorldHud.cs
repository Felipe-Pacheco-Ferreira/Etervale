using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;

namespace PristonTale.Mobile;

internal sealed class MobileWorldHud
{
    private readonly TextView _level;
    private readonly Meter _life;
    private readonly Meter _mana;
    private readonly Meter _stamina;
    private readonly Meter _experience;

    public LinearLayout Status { get; }
    public View Portrait { get; }
    public View Experience { get; }
    public TextView Target { get; }
    public Button Journal { get; }

    public MobileWorldHud(Context context, string characterName)
    {
        Status = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
        int Dp(int value) => (int)MathF.Round(value * density);
        MobileHudTheme.Card(Status);
        Status.SetPadding(Dp(7), Dp(4), Dp(7), Dp(5));
        var header = new LinearLayout(context);
        header.SetGravity(GravityFlags.CenterVertical);
        var name = new TextView(context) { Text = characterName, TextSize = 13, Typeface = Typeface.Create("serif", TypefaceStyle.Bold) };
        name.SetSingleLine(true);
        name.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        name.SetTextColor(MobileHudTheme.Ink);
        _level = new TextView(context) { TextSize = 10 };
        _level.SetTextColor(MobileHudTheme.Gold);
        header.AddView(name, new LinearLayout.LayoutParams(0, -2, 1f));
        header.AddView(_level);
        Status.AddView(header, new LinearLayout.LayoutParams(-1, -2));
        Portrait = new View(context) { ContentDescription = $"Retrato de {characterName}" };
        var portraitFrame = new global::Android.Graphics.Drawables.GradientDrawable();
        portraitFrame.SetColor(Color.Transparent);
        portraitFrame.SetStroke(Dp(2), MobileHudTheme.Gold);
        Portrait.Foreground = portraitFrame;
        _life = AddMeter("PV", Color.Rgb(177, 53, 45));
        _mana = AddMeter("PM", Color.Rgb(61, 116, 201));
        _stamina = AddMeter("RES", Color.Rgb(86, 146, 63));
        var experience = MakeMeter("EXP", Color.Rgb(73, 150, 214), 14);
        _experience = experience.Meter;
        Experience = experience.View;

        Target = new TextView(context) { TextSize = 14, Visibility = global::Android.Views.ViewStates.Gone };
        Target.SetTextColor(Color.Rgb(246, 226, 181));
        MobileHudTheme.Card(Target);
        Target.SetPadding(12, 6, 12, 6);
        Journal = MobileHudTheme.Button(context, "Missões");
        MobileHudTheme.Icon(Journal, HudGlyph.Journal);

        Meter AddMeter(string caption, Color tint)
        {
            var meter = MakeMeter(caption, tint, 13);
            Status.AddView(meter.View, new LinearLayout.LayoutParams(-1, Dp(12)) { TopMargin = Dp(2) });
            return meter.Meter;
        }

        (FrameLayout View, Meter Meter) MakeMeter(string caption, Color tint, int height)
        {
            var row = new FrameLayout(context);
            row.SetMinimumHeight((int)(height * density));
            row.SetBackgroundColor(Color.Argb(235, 15, 19, 23));
            var bar = new ProgressBar(context, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
            {
                Max = 1000,
                ProgressTintList = global::Android.Content.Res.ColorStateList.ValueOf(tint),
                ProgressBackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(Color.Rgb(33, 36, 42))
            };
            row.AddView(bar, new FrameLayout.LayoutParams(-1, -1));
            var label = new TextView(context) { Text = caption, TextSize = 9, Gravity = GravityFlags.Center };
            label.SetIncludeFontPadding(false);
            label.SetTextColor(Color.White);
            label.SetShadowLayer(1f, 0f, 1f, Color.Black);
            row.AddView(label, new FrameLayout.LayoutParams(-1, -1));
            return (row, new Meter(caption, label, bar));
        }
    }

    public void UpdateOwner(OwnerState owner)
    {
        _level.Text = $"Nv. {owner.Level}";
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
            Label.Text = Caption == "EXP" ? $"EXP  {(max > 0 ? value / max : 0):P2}" : $"{Caption}  {value:0}/{max:0}";
            Bar.Progress = max > 0 ? (int)Math.Clamp(value / max * Bar.Max, 0d, Bar.Max) : 0;
        }
    }
}
