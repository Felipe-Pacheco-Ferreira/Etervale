using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.Input;

// The skill buttons of the touch HUD, around the attack button.
public static class TouchSkillSlots
{
    public const int Count = 4;

    // A button the player chose keeps its skill while it is learned (chosen is cleared where it is not); every other
    // button takes, in order, the next learned skill no chosen button holds, or 0.
    public static void Fill(IReadOnlyList<OwnedSkillView> skills, Span<uint> codes, Span<bool> chosen)
    {
        for (var slot = 0; slot < codes.Length; slot++)
        {
            chosen[slot] &= Learned(skills, codes[slot]);
        }

        var next = 0;
        for (var slot = 0; slot < codes.Length; slot++)
        {
            if (chosen[slot])
            {
                continue;
            }

            codes[slot] = 0;
            while (next < skills.Count)
            {
                var skill = skills[next++];
                if (Usable(skill) && !HeldByChosen(codes, chosen, skill.Code))
                {
                    codes[slot] = skill.Code;
                    break;
                }
            }
        }
    }

    // A skill the player may put on a button: learned, and cast (not passive, used with a mouse button), as the server
    // would let it be cast (SkillFormulas.CheckCaster).
    public static bool Eligible(Skill skill, int rank) =>
        rank > 0 && skill.MouseUse != SkillMouseUse.None && SkillRuleCatalog.TryGet(skill.Code, out var rule) && rule.Kind != SkillKind.Passive;

    // A skill's name on its button: the first word, cut to seven characters.
    public static string ButtonCaption(string name)
    {
        var start = 0;
        while (start < name.Length && name[start] == ' ')
        {
            start++;
        }

        var end = name.IndexOf(' ', start);
        var word = end < 0 ? name[start..] : name[start..end];
        return word.Length <= 7 ? word : word[..6] + "…";
    }

    private static bool Usable(OwnedSkillView skill) => skill.Rank > 0 && SkillRuleCatalog.TryGet(skill.Code, out _);

    private static bool Learned(IReadOnlyList<OwnedSkillView> skills, uint code)
    {
        for (var index = 0; index < skills.Count; index++)
        {
            if (skills[index].Code == code && Usable(skills[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HeldByChosen(ReadOnlySpan<uint> codes, ReadOnlySpan<bool> chosen, uint code)
    {
        for (var slot = 0; slot < codes.Length; slot++)
        {
            if (chosen[slot] && codes[slot] == code)
            {
                return true;
            }
        }

        return false;
    }
}
