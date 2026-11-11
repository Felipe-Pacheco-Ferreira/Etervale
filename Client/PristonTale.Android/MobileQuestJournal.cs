using System.Text;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Simulation.Quests;

namespace PristonTale.Mobile;

internal sealed class MobileQuestJournal(Activity activity)
{
    private static readonly Color Panel = Color.Rgb(26, 21, 16);
    private static readonly Color Raised = Color.Rgb(47, 37, 25);
    private static readonly Color Gold = Color.Rgb(231, 197, 126);
    private static readonly Color Text = Color.Rgb(231, 221, 203);
    private QuestView? _view;

    public void Update(QuestView view) => _view = view;

    public void Open()
    {
        var panel = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        panel.SetBackgroundColor(Panel);
        panel.SetPadding(Dp(16), Dp(8), Dp(16), Dp(12));
        var bar = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        panel.AddView(bar);
        bar.AddView(Label("Missões", 20, Gold), new LinearLayout.LayoutParams(0, -2, 1f));
        var close = Action("Fechar");
        bar.AddView(close);

        var body = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        panel.AddView(body, new LinearLayout.LayoutParams(-1, Dp(220)));
        var list = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        var sidebar = new ScrollView(activity) { FillViewport = true };
        sidebar.AddView(list);
        body.AddView(sidebar, new LinearLayout.LayoutParams(0, -1, 1f));
        var detail = Label("", 15, Text);
        detail.SetPadding(Dp(16), Dp(8), Dp(4), Dp(8));
        var reader = new ScrollView(activity);
        reader.AddView(detail);
        body.AddView(reader, new LinearLayout.LayoutParams(0, -1, 2f));

        using var builder = new AlertDialog.Builder(activity);
        builder.SetView(panel);
        var dialog = builder.Create()!;
        close.Click += (_, _) => dialog.Dismiss();
        dialog.DismissEvent += (_, _) => dialog.Dispose();
        ShowList(active: true);
        dialog.Show();
        dialog.Window?.SetBackgroundDrawableResource(global::Android.Resource.Color.Transparent);
        dialog.Window?.SetLayout(Math.Min(Dp(720), activity.Resources!.DisplayMetrics!.WidthPixels - Dp(24)), -2);

        void ShowList(bool active)
        {
            var view = _view;
            var entries = (active ? view?.Progress : view?.Finished) ?? [];
            list.RemoveAllViews();
            var running = Action("Em andamento");
            running.Enabled = !active;
            running.Click += (_, _) => ShowList(active: true);
            list.AddView(running);
            var finished = Action("Concluídas");
            finished.Enabled = active;
            finished.Click += (_, _) => ShowList(active: false);
            list.AddView(finished);
            foreach (var entry in entries)
            {
                var button = Action(entry.Title);
                button.Click += (_, _) => ShowEntry(entry, active ? view : null);
                list.AddView(button);
            }

            if (entries.Count == 0)
            {
                detail.Text = "Nenhuma missão.";
            }
            else
            {
                ShowEntry(entries[0], active ? view : null);
            }
        }

        void ShowEntry(QuestBoardEntry entry, QuestView? view)
        {
            var path = entry.Description.Replace('\\', '/').ToLowerInvariant();
            var content = ReadText(path);
            if (view is { KillsNeeded: > 0 })
            {
                content += $"\n\n{view.TargetName}  {view.Kills}/{view.KillsNeeded}";
            }
            if (view is { TimerMs: > 0 })
            {
                content += $"\nTempo restante: {TimeSpan.FromMilliseconds(view.TimerMs):hh\\:mm\\:ss}";
            }

            detail.Text = $"{entry.Title}\n\n{content}";
            reader.ScrollTo(0, 0);
        }
    }

    private TextView Label(string text, float size, Color color)
    {
        var label = new TextView(activity) { Text = text, TextSize = size };
        label.SetTextColor(color);
        return label;
    }

    private Button Action(string text)
    {
        var button = new Button(activity) { Text = text, TextSize = 14 };
        button.SetAllCaps(false);
        button.SetTextColor(Gold);
        button.BackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(Raised);
        return button;
    }

    private int Dp(float value) => (int)MathF.Round(value * activity.Resources!.DisplayMetrics!.Density);

    private string ReadText(string path)
    {
        try
        {
            using var stream = activity.Assets!.Open($"QuestJournal/{path}");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var reader = new StreamReader(stream, Encoding.GetEncoding(1252));
            return reader.ReadToEnd();
        }
        catch (Java.IO.IOException)
        {
            return "Descrição indisponível.";
        }
    }
}
