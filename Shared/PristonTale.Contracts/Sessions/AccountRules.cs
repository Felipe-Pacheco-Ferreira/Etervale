using System.Text;

namespace PristonTale.Contracts.Sessions;

// What a login may carry and what a new account may be named and protected with. The byte limits are the login
// message's text fields (UTF-8); names are ASCII letters and digits so they are typed alike on any keyboard and compared
// without case.
public static class AccountRules
{
    public const int MaxAccountBytes = 64;
    public const int MaxPasswordBytes = 128;
    public const int MinNameLength = 4;
    public const int MaxNameLength = 16;
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 64;

    public static bool IsValidName(string? name)
    {
        if (name is null || name.Length is < MinNameLength or > MaxNameLength || !char.IsAsciiLetter(name[0]))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    // A password a new account may have: its length, no control characters, within the login field, and not the name.
    public static bool IsValidPassword(string? password, string name)
    {
        if (password is null || password.Length is < MinPasswordLength or > MaxPasswordLength
            || Encoding.UTF8.GetByteCount(password) > MaxPasswordBytes || string.Equals(password, name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var character in password)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }
}
