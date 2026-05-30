using System.Globalization;
using System.Text;
using System.IO;
using PasswordManager.Models;

namespace PasswordManager.Services;

public sealed class CsvVaultService
{
    public IReadOnlyList<VaultEntry> Import(string path)
    {
        string csv = File.ReadAllText(path, Encoding.UTF8);
        List<List<string>> rows = ParseCsv(csv);

        if (rows.Count == 0)
        {
            return [];
        }

        Dictionary<int, string> headers = rows[0]
            .Select((header, index) => new { header, index })
            .ToDictionary(item => item.index, item => NormalizeHeader(item.header));

        List<VaultEntry> entries = [];

        foreach (List<string> row in rows.Skip(1))
        {
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            Dictionary<string, string> values = [];
            for (int index = 0; index < headers.Count; index++)
            {
                string value = index < row.Count ? row[index].Trim() : string.Empty;
                values[headers[index]] = value;
            }

            string entryTypeValue = First(values, "entrytype", "type", "itemtype", "recordtype");
            string platform = First(values, "platform", "website", "site", "app", "application", "domain");
            string url = First(values, "url", "websiteurl", "loginuri", "uri");
            string title = First(values, "title", "name", "item", "label");
            string userName = First(values, "username", "user", "useremail", "email", "login");
            string password = First(values, "password", "pass");
            string cardholderName = First(values, "cardholdername", "cardholder", "nameoncard");
            string cardNumber = First(values, "cardnumber", "pan");
            string cardExpiration = First(values, "cardexpiration", "expiration", "expiry", "exp", "expdate", "expirationdate");
            string cardSecurityCode = First(values, "cardsecuritycode", "securitycode", "cvv", "cvc", "cvn");
            string cardPin = First(values, "cardpin", "pin");
            string notes = First(values, "notes", "note", "comments");
            bool isFavorite = ParseBool(First(values, "favorite", "isfavorite", "starred"));
            VaultEntryType entryType = ParseEntryType(
                entryTypeValue,
                cardNumber,
                cardExpiration,
                cardSecurityCode,
                cardPin,
                cardholderName);

            if (string.IsNullOrWhiteSpace(cardExpiration))
            {
                cardExpiration = CombineExpiration(
                    First(values, "cardexpirationmonth", "expirationmonth", "expmonth"),
                    First(values, "cardexpirationyear", "expirationyear", "expyear"));
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = entryType == VaultEntryType.Card
                    ? DeriveCardTitle(platform, cardNumber, cardholderName)
                    : DeriveTitle(platform, url, userName);
            }

            bool hasMeaningfulData = entryType == VaultEntryType.Card
                ? !string.IsNullOrWhiteSpace(cardNumber) || !string.IsNullOrWhiteSpace(cardholderName)
                : !string.IsNullOrWhiteSpace(password);

            if (string.IsNullOrWhiteSpace(title) && !hasMeaningfulData)
            {
                continue;
            }

            DateTime created = ParseDate(First(values, "createdutc", "created", "datecreated"));
            DateTime updated = ParseDate(First(values, "updatedutc", "updated", "lastmodified"));

            entries.Add(new VaultEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                EntryType = entryType,
                Title = title,
                Platform = platform,
                Url = url,
                UserName = userName,
                Password = password,
                CardholderName = cardholderName,
                CardNumber = cardNumber,
                CardExpiration = cardExpiration,
                CardSecurityCode = cardSecurityCode,
                CardPin = cardPin,
                Notes = notes,
                IsFavorite = isFavorite,
                CreatedAtUtc = created == default ? DateTime.UtcNow : created,
                UpdatedAtUtc = updated == default ? DateTime.UtcNow : updated
            });
        }

        return entries;
    }

    public void Export(string path, IEnumerable<VaultEntry> entries)
    {
        StringBuilder builder = new();
        builder.AppendLine("EntryType,Title,Platform,Url,Username,Password,CardholderName,CardNumber,CardExpiration,CardSecurityCode,CardPin,Notes,Favorite,CreatedUtc,UpdatedUtc");

        foreach (VaultEntry entry in entries)
        {
            string[] columns =
            [
                Escape(entry.EntryType.ToString()),
                Escape(entry.Title),
                Escape(entry.Platform),
                Escape(entry.Url),
                Escape(entry.UserName),
                Escape(entry.Password),
                Escape(entry.CardholderName),
                Escape(entry.CardNumber),
                Escape(entry.CardExpiration),
                Escape(entry.CardSecurityCode),
                Escape(entry.CardPin),
                Escape(entry.Notes),
                Escape(entry.IsFavorite.ToString(CultureInfo.InvariantCulture)),
                Escape(entry.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                Escape(entry.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture))
            ];

            builder.AppendLine(string.Join(",", columns));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string DeriveTitle(string platform, string url, string userName)
    {
        if (!string.IsNullOrWhiteSpace(platform))
        {
            return platform.Trim();
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri))
        {
            return parsedUri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName.Trim();
        }

        return "Imported Entry";
    }

    private static string DeriveCardTitle(string platform, string cardNumber, string cardholderName)
    {
        if (!string.IsNullOrWhiteSpace(platform))
        {
            return platform.Trim();
        }

        string lastFour = LastFourDigits(cardNumber);
        if (!string.IsNullOrWhiteSpace(lastFour))
        {
            return $"Card ending in {lastFour}";
        }

        if (!string.IsNullOrWhiteSpace(cardholderName))
        {
            return cardholderName.Trim();
        }

        return "Imported Card";
    }

    private static DateTime ParseDate(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime parsed)
            ? parsed.ToUniversalTime()
            : default;
    }

    private static bool ParseBool(string value)
    {
        return bool.TryParse(value, out bool parsed) && parsed;
    }

    private static VaultEntryType ParseEntryType(
        string value,
        string cardNumber,
        string cardExpiration,
        string cardSecurityCode,
        string cardPin,
        string cardholderName)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized.Contains("card", StringComparison.Ordinal))
            {
                return VaultEntryType.Card;
            }
        }

        return !string.IsNullOrWhiteSpace(cardNumber)
            || !string.IsNullOrWhiteSpace(cardExpiration)
            || !string.IsNullOrWhiteSpace(cardSecurityCode)
            || !string.IsNullOrWhiteSpace(cardPin)
            || !string.IsNullOrWhiteSpace(cardholderName)
                ? VaultEntryType.Card
                : VaultEntryType.Password;
    }

    private static string CombineExpiration(string month, string year)
    {
        if (string.IsNullOrWhiteSpace(month) && string.IsNullOrWhiteSpace(year))
        {
            return string.Empty;
        }

        month = month.Trim();
        year = year.Trim();

        if (string.IsNullOrWhiteSpace(month))
        {
            return year;
        }

        if (string.IsNullOrWhiteSpace(year))
        {
            return month;
        }

        return $"{month}/{year}";
    }

    private static string First(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string NormalizeHeader(string header)
    {
        StringBuilder builder = new(header.Length);
        foreach (char character in header.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        string normalized = value.Replace("\"", "\"\"");
        return $"\"{normalized}\"";
    }

    private static string LastFourDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] digits = value.Where(char.IsDigit).ToArray();
        return digits.Length >= 4 ? new string(digits[^4..]) : string.Empty;
    }

    private static List<List<string>> ParseCsv(string csv)
    {
        List<List<string>> rows = [];
        List<string> currentRow = [];
        StringBuilder currentValue = new();
        bool inQuotes = false;

        for (int index = 0; index < csv.Length; index++)
        {
            char character = csv[index];

            if (inQuotes)
            {
                if (character == '"')
                {
                    bool escapedQuote = index + 1 < csv.Length && csv[index + 1] == '"';
                    if (escapedQuote)
                    {
                        currentValue.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentValue.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    currentRow.Add(currentValue.ToString());
                    currentValue.Clear();
                    break;
                case '\n':
                    currentRow.Add(currentValue.ToString());
                    currentValue.Clear();
                    rows.Add(currentRow);
                    currentRow = [];
                    break;
                case '\r':
                    break;
                default:
                    currentValue.Append(character);
                    break;
            }
        }

        if (currentValue.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentValue.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }
}
