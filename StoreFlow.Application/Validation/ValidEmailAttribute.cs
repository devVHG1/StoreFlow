using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Mail;

namespace StoreFlow.Application.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ValidEmailAttribute : ValidationAttribute
{
    public ValidEmailAttribute() => ErrorMessage = "Вкажіть коректну адресу email.";

    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        if (value is not string raw) return false;
        if (raw.Any(char.IsControl)) return false;
        var email = raw.Trim();
        if (email.Length == 0) return true;
        if (email.Length > 254 || email.Any(char.IsWhiteSpace) || email.Any(char.IsControl)) return false;
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email) return false;
        var parts = email.Split('@');
        if (parts.Length != 2) return false;
        var local = parts[0];
        if (local.Length is < 1 or > 64 || local.StartsWith('.') || local.EndsWith('.') || local.Contains("..")) return false;
        const string symbols = "!#$%&'*+-/=?^_`{|}~.";
        if (local.Any(c => !char.IsLetterOrDigit(c) && !symbols.Contains(c))) return false;
        string domain;
        try { domain = new IdnMapping().GetAscii(parts[1]); }
        catch (ArgumentException) { return false; }
        var labels = domain.Split('.');
        return domain.Length <= 253 && labels.Length >= 2 && labels.All(label =>
            label.Length is >= 1 and <= 63 && !label.StartsWith('-') && !label.EndsWith('-') &&
            label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) &&
            labels[^1].Any(char.IsAsciiLetter);
    }
}
