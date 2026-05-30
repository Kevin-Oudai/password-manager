namespace PasswordManager.Models;

public sealed class PasswordGenerationOptions
{
    public int Length { get; set; } = 20;

    public bool IncludeUppercase { get; set; } = true;

    public bool IncludeLowercase { get; set; } = true;

    public bool IncludeNumbers { get; set; } = true;

    public bool IncludeSymbols { get; set; } = true;

    public bool ExcludeAmbiguousCharacters { get; set; } = true;
}
