using System.Security.Cryptography;
using PasswordManager.Models;

namespace PasswordManager.Services;

public static class PasswordGeneratorService
{
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Numbers = "23456789";
    private const string Symbols = "!@$%&*?-_+=#";

    private const string UppercaseAll = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseAll = "abcdefghijklmnopqrstuvwxyz";
    private const string NumbersAll = "0123456789";

    public static string Generate(PasswordGenerationOptions options)
    {
        if (options.Length < 4 || options.Length > 128)
        {
            throw new InvalidOperationException("Password length must be between 4 and 128 characters.");
        }

        List<string> characterSets = [];

        if (options.IncludeUppercase)
        {
            characterSets.Add(options.ExcludeAmbiguousCharacters ? Uppercase : UppercaseAll);
        }

        if (options.IncludeLowercase)
        {
            characterSets.Add(options.ExcludeAmbiguousCharacters ? Lowercase : LowercaseAll);
        }

        if (options.IncludeNumbers)
        {
            characterSets.Add(options.ExcludeAmbiguousCharacters ? Numbers : NumbersAll);
        }

        if (options.IncludeSymbols)
        {
            characterSets.Add(Symbols);
        }

        if (characterSets.Count == 0)
        {
            throw new InvalidOperationException("Select at least one character type for password generation.");
        }

        if (options.Length < characterSets.Count)
        {
            throw new InvalidOperationException("Length must be at least the number of enabled character groups.");
        }

        List<char> passwordCharacters = [];

        foreach (string set in characterSets)
        {
            passwordCharacters.Add(GetRandomCharacter(set));
        }

        string allCharacters = string.Concat(characterSets);
        while (passwordCharacters.Count < options.Length)
        {
            passwordCharacters.Add(GetRandomCharacter(allCharacters));
        }

        Shuffle(passwordCharacters);
        return new string([.. passwordCharacters]);
    }

    private static char GetRandomCharacter(string characters)
    {
        int index = RandomNumberGenerator.GetInt32(characters.Length);
        return characters[index];
    }

    private static void Shuffle(IList<char> items)
    {
        for (int index = items.Count - 1; index > 0; index--)
        {
            int swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }
    }
}
