namespace DocPrep.Domain.Common;

public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class Guard
{
    public static string Required(string? value, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("validation.required", $"{field} is required.");
        var result = value.Trim();
        if (result.Length > max) throw new DomainException("validation.too_long", $"{field} exceeds {max} characters.");
        return result;
    }
}
