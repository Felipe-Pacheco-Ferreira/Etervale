using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Tests;

// Line wrapping (HudTextLayout): words break at spaces; Chinese and Japanese break between characters, never before a
// closing mark, and their lines join without a space.
internal static class TextLayoutTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("text wraps at spaces, and Chinese between characters but never before a closing mark", WrapAsync);
    }

    private static Task WrapAsync()
    {
        static float Measure(string text) => text.Length;
        Assert.True(HudTextLayout.Wrap("one two three", 7, Measure) is ["one two", "three"], "Latin text did not wrap at spaces");
        Assert.True(HudTextLayout.Wrap("点击地面，即可走到该处。", 5, Measure) is ["点击地面，", "即可走到该", "处。"],
            "Chinese did not wrap between characters, or a line started with a closing mark");
        Assert.True(HudTextLayout.Pieces("Lv 100 的任务") is [("Lv", false), ("100", true), ("的", true), ("任", false), ("务", false)],
            "mixed Latin and Chinese did not split into pieces with their spaces");
        Assert.True(HudTextLayout.Pieces("说“你好”!") is [("说", false), ("“你", false), ("好”!", false)],
            "an opening mark was not kept with the next character, or an ASCII mark after Chinese started a piece");
        Assert.True(HudQuestDocuments.Paragraphs(["你好", "世界", "", "one", "two"]) is ["你好世界", "", "one two"],
            "Chinese lines joined with a space, or Latin lines without one");
        return Task.CompletedTask;
    }
}
