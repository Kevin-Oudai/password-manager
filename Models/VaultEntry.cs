using PasswordManager.Infrastructure;

namespace PasswordManager.Models;

public sealed class VaultEntry : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private VaultEntryType _entryType = VaultEntryType.Password;
    private string _title = string.Empty;
    private string _platform = string.Empty;
    private string _url = string.Empty;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _cardholderName = string.Empty;
    private string _cardNumber = string.Empty;
    private string _cardExpiration = string.Empty;
    private string _cardSecurityCode = string.Empty;
    private string _cardPin = string.Empty;
    private string _notes = string.Empty;
    private bool _isFavorite;
    private DateTime _createdAtUtc = DateTime.UtcNow;
    private DateTime _updatedAtUtc = DateTime.UtcNow;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public VaultEntryType EntryType
    {
        get => _entryType;
        set
        {
            if (SetProperty(ref _entryType, value))
            {
                RaiseDerivedPropertiesChanged();
            }
        }
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Platform
    {
        get => _platform;
        set => SetProperty(ref _platform, value);
    }

    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string CardholderName
    {
        get => _cardholderName;
        set => SetProperty(ref _cardholderName, value);
    }

    public string CardNumber
    {
        get => _cardNumber;
        set => SetProperty(ref _cardNumber, value);
    }

    public string CardExpiration
    {
        get => _cardExpiration;
        set => SetProperty(ref _cardExpiration, value);
    }

    public string CardSecurityCode
    {
        get => _cardSecurityCode;
        set => SetProperty(ref _cardSecurityCode, value);
    }

    public string CardPin
    {
        get => _cardPin;
        set => SetProperty(ref _cardPin, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public DateTime CreatedAtUtc
    {
        get => _createdAtUtc;
        set => SetProperty(ref _createdAtUtc, value);
    }

    public DateTime UpdatedAtUtc
    {
        get => _updatedAtUtc;
        set => SetProperty(ref _updatedAtUtc, value);
    }

    public bool IsPasswordEntry => EntryType == VaultEntryType.Password;

    public bool IsCardEntry => EntryType == VaultEntryType.Card;

    public string TypeLabel => IsCardEntry ? "Card" : "Password";

    public string Subtitle
    {
        get
        {
            if (IsCardEntry)
            {
                return BuildCardSubtitle();
            }

            if (!string.IsNullOrWhiteSpace(Platform) && !string.IsNullOrWhiteSpace(UserName))
            {
                return $"{Platform} - {UserName}";
            }

            if (!string.IsNullOrWhiteSpace(Platform))
            {
                return Platform;
            }

            if (!string.IsNullOrWhiteSpace(UserName))
            {
                return UserName;
            }

            return "No secondary details";
        }
    }

    public string ModifiedLabel => $"Updated {UpdatedAtUtc.ToLocalTime():MMM d, yyyy h:mm tt}";

    public VaultEntry Clone()
    {
        return new VaultEntry
        {
            Id = Id,
            EntryType = EntryType,
            Title = Title,
            Platform = Platform,
            Url = Url,
            UserName = UserName,
            Password = Password,
            CardholderName = CardholderName,
            CardNumber = CardNumber,
            CardExpiration = CardExpiration,
            CardSecurityCode = CardSecurityCode,
            CardPin = CardPin,
            Notes = Notes,
            IsFavorite = IsFavorite,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc
        };
    }

    public void ApplyFrom(VaultEntry source)
    {
        EntryType = source.EntryType;
        Title = source.Title.Trim();
        Platform = source.Platform.Trim();
        Url = source.Url.Trim();
        UserName = source.UserName.Trim();
        Password = source.Password;
        CardholderName = source.CardholderName.Trim();
        CardNumber = source.CardNumber.Trim();
        CardExpiration = source.CardExpiration.Trim();
        CardSecurityCode = source.CardSecurityCode.Trim();
        CardPin = source.CardPin.Trim();
        Notes = source.Notes.Trim();
        IsFavorite = source.IsFavorite;
        UpdatedAtUtc = DateTime.UtcNow;
        RaiseDerivedPropertiesChanged();
    }

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return Contains(Title, query)
            || Contains(TypeLabel, query)
            || Contains(Platform, query)
            || Contains(Url, query)
            || Contains(UserName, query)
            || Contains(CardholderName, query)
            || Contains(CardNumber, query)
            || Contains(CardExpiration, query)
            || Contains(CardSecurityCode, query)
            || Contains(CardPin, query)
            || Contains(Notes, query);
    }

    public void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        RaiseDerivedPropertiesChanged();
    }

    public void RaiseDerivedPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsPasswordEntry));
        OnPropertyChanged(nameof(IsCardEntry));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ModifiedLabel));
    }

    private string BuildCardSubtitle()
    {
        string lastFour = CardLastFour(CardNumber);

        if (!string.IsNullOrWhiteSpace(Platform) && !string.IsNullOrWhiteSpace(lastFour))
        {
            return $"{Platform} - ending in {lastFour}";
        }

        if (!string.IsNullOrWhiteSpace(CardholderName) && !string.IsNullOrWhiteSpace(lastFour))
        {
            return $"{CardholderName} - ending in {lastFour}";
        }

        if (!string.IsNullOrWhiteSpace(Platform))
        {
            return Platform;
        }

        if (!string.IsNullOrWhiteSpace(CardholderName))
        {
            return CardholderName;
        }

        if (!string.IsNullOrWhiteSpace(lastFour))
        {
            return $"Card ending in {lastFour}";
        }

        return "No secondary details";
    }

    private static string CardLastFour(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] digits = value.Where(char.IsDigit).ToArray();
        return digits.Length >= 4 ? new string(digits[^4..]) : string.Empty;
    }

    private static bool Contains(string? value, string query)
    {
        return value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    }
}
