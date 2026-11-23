namespace PristonTale.Gameplay.UI.Quests;

public enum TouchQuestStep
{
    Pause,
    Guide,
    Hunt
}

// What Ir does on the touch HUD's quest card and journal (the Android client's automatic quest button): while the
// journey's guide or the quest hunt runs it pauses them; otherwise the guide where the quest has a destination, else the
// quest hunt (which refuses on its own in town or without a hunt).
public static class TouchQuestGo
{
    public static TouchQuestStep Next(bool guiding, bool hunting, bool canGuide) =>
        guiding || hunting ? TouchQuestStep.Pause : canGuide ? TouchQuestStep.Guide : TouchQuestStep.Hunt;
}
