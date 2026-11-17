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
    public const int MaxEmailLength = 254;
    public const int MaxReferralCodeLength = 32;

    // An address of the form local@domain.tld without spaces: the registration keeps it for the account's support and
    // recovery; nothing is sent to it, so the check only rejects what cannot be an address.
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrEmpty(email) || email.Length > MaxEmailLength)
        {
            return false;
        }

        var at = email.IndexOf('@');
        var dot = email.LastIndexOf('.');
        if (at < 1 || at != email.LastIndexOf('@') || dot < at + 2 || dot == email.Length - 1)
        {
            return false;
        }

        foreach (var character in email)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    // The optional code of who invited the player: empty, or letters, digits, '-' and '_'.
    public static bool IsValidReferralCode(string? code)
    {
        if (code is null || code.Length > MaxReferralCodeLength)
        {
            return false;
        }

        foreach (var character in code)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

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
