using Android.App;
using Android.Content;
using Android.Text;
using Android.Widget;
using PristonTale.Gameplay.Combat;
using ClipboardManager = Android.Content.ClipboardManager;

internal sealed partial class VulkanFreeHuntPanel
{
    private void ProfileTransfer()
    {
        var profile = Card("Copiar perfil para outro aparelho");
        Note(profile, "O código leva as opções da caça e a ordem das habilidades. Importe no aparelho de destino.");
        var actions = new LinearLayout(activity);
        profile.AddView(actions);
        Action(actions, "Copiar código", CopyProfile);
        Action(actions, "Importar código", ImportProfile);
    }

    private void CopyProfile()
    {
        var code = FreeHuntProfile.Encode(Settings);
        if (code.Length > FreeHuntProfile.MaximumCodeLength)
        {
            Toast.MakeText(activity, "Reduza os filtros de texto antes de copiar.", ToastLength.Long)?.Show();
            return;
        }

        if (activity.GetSystemService(Context.ClipboardService) is ClipboardManager clipboard)
        {
            clipboard.PrimaryClip = ClipData.NewPlainText("Perfil de Auto-caça", code);
            Toast.MakeText(activity, "Código do perfil copiado.", ToastLength.Short)?.Show();
        }
    }

    private void ImportProfile()
    {
        var code = Editor("", "Cole o código do perfil", InputTypes.ClassText | InputTypes.TextFlagNoSuggestions);
        code.SetFilters([new InputFilterLengthFilter(FreeHuntProfile.MaximumCodeLength)]);
        using var builder = new AlertDialog.Builder(activity, Android.Resource.Style.ThemeMaterialDialogAlert);
        builder.SetTitle("Importar perfil de Auto-caça");
        builder.SetMessage(Settings.ShareAccount
            ? "Substitui as opções compartilhadas da conta neste aparelho e pausa a caça para você revisar."
            : "Substitui as opções do personagem e pausa a caça para você revisar.");
        builder.SetView(code);
        builder.SetNegativeButton("Cancelar", (_, _) => { });
        builder.SetPositiveButton("Importar", (_, _) => { });
        var dialog = builder.Show();
        dialog.GetButton((int)DialogButtonType.Positive).Click += (_, _) =>
        {
            if (!FreeHuntProfile.TryDecode(code.Text, out var profile))
            {
                code.Error = "Código inválido ou incompatível com esta versão.";
                return;
            }

            session.SetFreeHunt(false, "Perfil importado");
            var shareAccount = Settings.ShareAccount;
            session.HuntSettings = profile with { ShareAccount = shareAccount };
            editor.KeepFreeHuntSettings(session.Character.Name, session.HuntProfileScope, Settings);
            dialog.Dismiss();
            Show(tab);
        };
    }
}
