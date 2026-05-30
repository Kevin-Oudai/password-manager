using Microsoft.Win32;
using PasswordManager.Models;
using PasswordManager.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using Forms = System.Windows.Forms;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using InputKeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace PasswordManager;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly VaultService _vaultService = new();
    private readonly CsvVaultService _csvVaultService = new();
    private readonly ObservableCollection<VaultEntry> _entries = [];
    private readonly DispatcherTimer _sessionTimer;
    private readonly DispatcherTimer _clipboardTimer;
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(15);
    private readonly DrawingIcon _trayIconGraphic;
    private readonly Forms.NotifyIcon _trayIcon;

    private VaultData _vaultData = new();
    private VaultEntry? _selectedEntry;
    private VaultEntry? _editorEntry;
    private bool _isLocked = true;
    private bool _hasVaultFile;
    private bool _isEditingEntry;
    private bool _creatingNewEntry;
    private bool _sensitiveDataVisible;
    private string _searchText = string.Empty;
    private string _sortMode = "Recently Updated";
    private string _generatorLength = "20";
    private bool _includeUppercase = true;
    private bool _includeLowercase = true;
    private bool _includeNumbers = true;
    private bool _includeSymbols = true;
    private bool _excludeAmbiguousCharacters = true;
    private string _generatedPassword = string.Empty;
    private string _statusMessage = "Create a local vault to get started.";
    private string _lockHeading = string.Empty;
    private string _lockMessage = string.Empty;
    private string? _currentMasterPassword;
    private string _lastClipboardValue = string.Empty;
    private DateTime _lastInteractionUtc = DateTime.UtcNow;
    private bool _allowExit;
    private bool _trayHintShown;

    public MainWindow()
    {
        EntriesView = CollectionViewSource.GetDefaultView(_entries);
        EntriesView.Filter = FilterEntries;

        InitializeComponent();
        _trayIconGraphic = LoadTrayIconGraphic();
        _trayIcon = CreateTrayIcon();
        Icon = Imaging.CreateBitmapSourceFromHIcon(
            _trayIconGraphic.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(64, 64));

        _sessionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _sessionTimer.Tick += SessionTimer_Tick;
        _sessionTimer.Start();

        _clipboardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _clipboardTimer.Tick += ClipboardTimer_Tick;

        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;

        Closed += MainWindow_Closed;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;

        GeneratePassword(updateStatus: false);
        UpdateCounts();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICollectionView EntriesView { get; }

    public int EntryCount => _entries.Count;

    public int FavoriteCount => _entries.Count(entry => entry.IsFavorite);

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (SetProperty(ref _isLocked, value))
            {
                OnPropertyChanged(nameof(SessionStatus));
                OnPropertyChanged(nameof(ShowCreateVault));
                OnPropertyChanged(nameof(ShowUnlockVault));
            }
        }
    }

    public bool HasVaultFile
    {
        get => _hasVaultFile;
        set
        {
            if (SetProperty(ref _hasVaultFile, value))
            {
                OnPropertyChanged(nameof(ShowCreateVault));
                OnPropertyChanged(nameof(ShowUnlockVault));
            }
        }
    }

    public bool ShowCreateVault => IsLocked && !HasVaultFile;

    public bool ShowUnlockVault => IsLocked && HasVaultFile;

    public VaultEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                _sensitiveDataVisible = false;
                NotifySelectionStateChanged();
            }
        }
    }

    public VaultEntry? EditorEntry
    {
        get => _editorEntry;
        set
        {
            if (ReferenceEquals(_editorEntry, value))
            {
                return;
            }

            if (_editorEntry is not null)
            {
                _editorEntry.PropertyChanged -= EditorEntry_PropertyChanged;
            }

            if (SetProperty(ref _editorEntry, value))
            {
                if (_editorEntry is not null)
                {
                    _editorEntry.PropertyChanged += EditorEntry_PropertyChanged;
                }

                OnPropertyChanged(nameof(EditorHeading));
                OnPropertyChanged(nameof(EditorSubheading));
            }
        }
    }

    public bool IsEditingEntry
    {
        get => _isEditingEntry;
        set
        {
            if (SetProperty(ref _isEditingEntry, value))
            {
                OnPropertyChanged(nameof(HasSelectionAndNotEditing));
                OnPropertyChanged(nameof(HasNoSelectionAndNotEditing));
            }
        }
    }

    public bool HasSelectionAndNotEditing => SelectedEntry is not null && !IsEditingEntry;

    public bool HasNoSelectionAndNotEditing => SelectedEntry is null && !IsEditingEntry;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                EntriesView.Refresh();
            }
        }
    }

    public string SortMode
    {
        get => _sortMode;
        set
        {
            if (SetProperty(ref _sortMode, value))
            {
                ApplySort();
            }
        }
    }

    public string GeneratorLength
    {
        get => _generatorLength;
        set => SetProperty(ref _generatorLength, value);
    }

    public bool IncludeUppercase
    {
        get => _includeUppercase;
        set => SetProperty(ref _includeUppercase, value);
    }

    public bool IncludeLowercase
    {
        get => _includeLowercase;
        set => SetProperty(ref _includeLowercase, value);
    }

    public bool IncludeNumbers
    {
        get => _includeNumbers;
        set => SetProperty(ref _includeNumbers, value);
    }

    public bool IncludeSymbols
    {
        get => _includeSymbols;
        set => SetProperty(ref _includeSymbols, value);
    }

    public bool ExcludeAmbiguousCharacters
    {
        get => _excludeAmbiguousCharacters;
        set => SetProperty(ref _excludeAmbiguousCharacters, value);
    }

    public string GeneratedPassword
    {
        get => _generatedPassword;
        set => SetProperty(ref _generatedPassword, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string LockHeading
    {
        get => _lockHeading;
        set => SetProperty(ref _lockHeading, value);
    }

    public string LockMessage
    {
        get => _lockMessage;
        set => SetProperty(ref _lockMessage, value);
    }

    public string SessionStatus
    {
        get
        {
            if (IsLocked)
            {
                return "Locked";
            }

            TimeSpan remaining = GetRemainingSessionTime();
            if (remaining <= TimeSpan.Zero)
            {
                return "Locking now";
            }

            return $"Auto-locks in {remaining:mm\\:ss}";
        }
    }

    public string VaultLocationLabel => $"Stored at {_vaultService.VaultPath}";

    public string LogLocationLabel => $"Logs at {LogService.CurrentLogPath}";

    public string SelectedPasswordDisplay
    {
        get
        {
            if (SelectedEntry is null)
            {
                return "No entry selected";
            }

            if (SelectedEntry.IsCardEntry)
            {
                return "Not available for card entries";
            }

            if (_sensitiveDataVisible)
            {
                return string.IsNullOrWhiteSpace(SelectedEntry.Password) ? "(empty)" : SelectedEntry.Password;
            }

            return MaskPassword(SelectedEntry.Password);
        }
    }

    public string SelectedCardNumberDisplay
    {
        get
        {
            if (SelectedEntry is null)
            {
                return "No entry selected";
            }

            if (SelectedEntry.IsPasswordEntry)
            {
                return "Not available for password entries";
            }

            if (_sensitiveDataVisible)
            {
                return string.IsNullOrWhiteSpace(SelectedEntry.CardNumber) ? "(empty)" : SelectedEntry.CardNumber;
            }

            return MaskCardNumber(SelectedEntry.CardNumber);
        }
    }

    public string SelectedCardSecurityCodeDisplay
    {
        get
        {
            if (SelectedEntry is null)
            {
                return "No entry selected";
            }

            if (SelectedEntry.IsPasswordEntry)
            {
                return "Not available for password entries";
            }

            if (_sensitiveDataVisible)
            {
                return string.IsNullOrWhiteSpace(SelectedEntry.CardSecurityCode) ? "(empty)" : SelectedEntry.CardSecurityCode;
            }

            return MaskShortSecret(SelectedEntry.CardSecurityCode);
        }
    }

    public string SelectedCardPinDisplay
    {
        get
        {
            if (SelectedEntry is null)
            {
                return "No entry selected";
            }

            if (SelectedEntry.IsPasswordEntry)
            {
                return "Not available for password entries";
            }

            if (_sensitiveDataVisible)
            {
                return string.IsNullOrWhiteSpace(SelectedEntry.CardPin) ? "(empty)" : SelectedEntry.CardPin;
            }

            return MaskShortSecret(SelectedEntry.CardPin);
        }
    }

    public string SelectedSensitiveToggleLabel => _sensitiveDataVisible ? "Hide Sensitive Data" : "Reveal Sensitive Data";

    public string FavoriteActionLabel => SelectedEntry?.IsFavorite == true ? "Unfavorite" : "Favorite";

    public string EditorHeading
    {
        get
        {
            bool isCardEntry = EditorEntry?.IsCardEntry == true;
            return _creatingNewEntry
                ? isCardEntry ? "Add Card" : "Add Password"
                : isCardEntry ? "Edit Card" : "Edit Password";
        }
    }

    public string EditorSubheading
    {
        get
        {
            bool isCardEntry = EditorEntry?.IsCardEntry == true;

            if (_creatingNewEntry)
            {
                return isCardEntry
                    ? "Fill in the payment card details, then save them into the encrypted vault."
                    : "Fill in the login details, then save them into the encrypted vault.";
            }

            return isCardEntry
                ? "Update the selected payment card and save the changes back into the encrypted vault."
                : "Update the selected login entry and save the changes back into the encrypted vault.";
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LogService.Info("MainWindow", $"Main window loaded. Vault exists: {_vaultService.VaultExists}.");
        HasVaultFile = _vaultService.VaultExists;
        PrepareLockScreen();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        _sessionTimer.Stop();
        _clipboardTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIconGraphic.Dispose();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowExit)
        {
            return;
        }

        e.Cancel = true;
        HideToTray(showBalloonTip: true, "Password Vault is still running in the system tray.");
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray(showBalloonTip: true, "Password Vault minimized to the system tray.");
        }
    }

    private void SessionTimer_Tick(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SessionStatus));

        if (!IsLocked && GetRemainingSessionTime() <= TimeSpan.Zero)
        {
            LockVault("Vault locked after 15 minutes of inactivity.");
        }
    }

    private void ClipboardTimer_Tick(object? sender, EventArgs e)
    {
        _clipboardTimer.Stop();

        try
        {
            if (!string.IsNullOrEmpty(_lastClipboardValue)
                && Clipboard.ContainsText()
                && string.Equals(Clipboard.GetText(), _lastClipboardValue, StringComparison.Ordinal))
            {
                Clipboard.Clear();
                StatusMessage = "Clipboard cleared after 30 seconds.";
            }
        }
        catch (Exception exception)
        {
            LogService.Error("ClipboardAutoClear", exception);
            StatusMessage = "Clipboard could not be cleared automatically.";
        }
        finally
        {
            _lastClipboardValue = string.Empty;
        }
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock && !IsLocked)
        {
            Dispatcher.Invoke(() => LockVault("Vault locked because Windows was locked."));
        }
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend && !IsLocked)
        {
            Dispatcher.Invoke(() => LockVault("Vault locked before sleep."));
        }
    }

    private void EditorEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VaultEntry.EntryType))
        {
            OnPropertyChanged(nameof(EditorHeading));
            OnPropertyChanged(nameof(EditorSubheading));
        }
    }

    private void NotifySelectionStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectionAndNotEditing));
        OnPropertyChanged(nameof(HasNoSelectionAndNotEditing));
        OnPropertyChanged(nameof(SelectedPasswordDisplay));
        OnPropertyChanged(nameof(SelectedCardNumberDisplay));
        OnPropertyChanged(nameof(SelectedCardSecurityCodeDisplay));
        OnPropertyChanged(nameof(SelectedCardPinDisplay));
        OnPropertyChanged(nameof(SelectedSensitiveToggleLabel));
        OnPropertyChanged(nameof(FavoriteActionLabel));
    }

    private bool FilterEntries(object item)
    {
        return item is VaultEntry entry && entry.Matches(SearchText);
    }

    private void ApplySort()
    {
        using (EntriesView.DeferRefresh())
        {
            EntriesView.SortDescriptions.Clear();

            if (SortMode == "Title A-Z")
            {
                EntriesView.SortDescriptions.Add(new SortDescription(nameof(VaultEntry.Title), ListSortDirection.Ascending));
            }
            else if (SortMode == "Favorites First")
            {
                EntriesView.SortDescriptions.Add(new SortDescription(nameof(VaultEntry.IsFavorite), ListSortDirection.Descending));
                EntriesView.SortDescriptions.Add(new SortDescription(nameof(VaultEntry.Title), ListSortDirection.Ascending));
            }
            else
            {
                EntriesView.SortDescriptions.Add(new SortDescription(nameof(VaultEntry.UpdatedAtUtc), ListSortDirection.Descending));
            }
        }
    }

    private void PrepareLockScreen(string? customMessage = null)
    {
        HasVaultFile = _vaultService.VaultExists;
        LockHeading = HasVaultFile ? "Unlock your vault" : "Create your private vault";

        string defaultMessage = HasVaultFile
            ? "Enter the master password. The app stays unlocked only for the current session and auto-locks after 15 minutes of inactivity."
            : "Create a strong master password. It will be required every time the app launches.";

        LockMessage = string.IsNullOrWhiteSpace(customMessage)
            ? defaultMessage
            : $"{customMessage} {defaultMessage}";

        IsLocked = true;
        StatusMessage = !string.IsNullOrWhiteSpace(customMessage)
            ? customMessage
            : HasVaultFile
                ? "Vault is locked."
                : "Create a master password to start your local vault.";

        ClearPasswordBoxes();
        FocusActivePasswordBox();
    }

    private void FocusActivePasswordBox()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (HasVaultFile)
            {
                UnlockPasswordBox.Focus();
            }
            else
            {
                CreatePasswordBox.Focus();
            }
        }));
    }

    private void ClearPasswordBoxes()
    {
        if (CreatePasswordBox is not null)
        {
            CreatePasswordBox.Password = string.Empty;
        }

        if (ConfirmPasswordBox is not null)
        {
            ConfirmPasswordBox.Password = string.Empty;
        }

        if (UnlockPasswordBox is not null)
        {
            UnlockPasswordBox.Password = string.Empty;
        }
    }

    private void UnlockWithVault(VaultData vault, string masterPassword, string successMessage)
    {
        _vaultData = vault;
        _currentMasterPassword = masterPassword;
        _lastInteractionUtc = DateTime.UtcNow;

        _entries.Clear();
        foreach (VaultEntry entry in vault.Entries)
        {
            entry.RaiseDerivedPropertiesChanged();
            _entries.Add(entry);
        }

        ApplySort();
        EntriesView.Refresh();

        IsLocked = false;
        IsEditingEntry = false;
        EditorEntry = null;
        _creatingNewEntry = false;
        _sensitiveDataVisible = false;
        SelectedEntry = _entries.FirstOrDefault();

        UpdateCounts();
        ClearPasswordBoxes();
        StatusMessage = successMessage;
        LogService.Info("UnlockWithVault", $"{successMessage} Entry count: {_entries.Count}.");
        OnPropertyChanged(nameof(SessionStatus));
    }

    private void LockVault(string reasonMessage)
    {
        _currentMasterPassword = null;
        _vaultData = new VaultData();
        _entries.Clear();
        SelectedEntry = null;
        EditorEntry = null;
        IsEditingEntry = false;
        _creatingNewEntry = false;
        _sensitiveDataVisible = false;
        _lastClipboardValue = string.Empty;
        _clipboardTimer.Stop();
        LogService.Info("LockVault", reasonMessage);
        PrepareLockScreen(reasonMessage);
        UpdateCounts();
    }

    private static DrawingIcon LoadTrayIconGraphic()
    {
        StreamResourceInfo? iconResource = Application.GetResourceStream(new Uri("Assets/vault-logo.ico", UriKind.Relative));
        if (iconResource is null)
        {
            return (DrawingIcon)DrawingSystemIcons.Shield.Clone();
        }

        using Stream iconStream = iconResource.Stream;
        using DrawingIcon icon = new(iconStream);
        return (DrawingIcon)icon.Clone();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        Forms.ToolStripMenuItem openItem = new("Open Password Vault");
        openItem.Click += (_, _) => ShowFromTray();

        Forms.ToolStripMenuItem lockItem = new("Lock And Hide");
        lockItem.Click += (_, _) =>
        {
            if (!IsLocked)
            {
                LockVault("Vault locked from the system tray.");
            }

            HideToTray(showBalloonTip: false, "Password Vault is running in the system tray.");
        };

        Forms.ToolStripSeparator separator = new();

        Forms.ToolStripMenuItem exitItem = new("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        Forms.ContextMenuStrip trayMenu = new();
        trayMenu.Items.Add(openItem);
        trayMenu.Items.Add(lockItem);
        trayMenu.Items.Add(separator);
        trayMenu.Items.Add(exitItem);

        Forms.NotifyIcon trayIcon = new()
        {
            Icon = _trayIconGraphic,
            Text = "Password Vault",
            Visible = true,
            ContextMenuStrip = trayMenu
        };

        trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                ShowFromTray();
            }
        };
        return trayIcon;
    }

    private void HideToTray(bool showBalloonTip, string statusMessage)
    {
        ShowInTaskbar = false;
        Hide();
        StatusMessage = statusMessage;
        LogService.Info("Tray", statusMessage);

        if (showBalloonTip && !_trayHintShown)
        {
            _trayIcon.ShowBalloonTip(
                2500,
                "Password Vault",
                "The app is still running here. Double-click the tray icon to reopen it.",
                Forms.ToolTipIcon.Info);
            _trayHintShown = true;
        }
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        StatusMessage = "Password Vault restored from the system tray.";
        LogService.Info("Tray", "Password Vault restored from the system tray.");
    }

    private void ExitApplication()
    {
        _allowExit = true;
        _trayIcon.Visible = false;
        LogService.Info("Tray", "Password Vault exiting from the tray menu.");
        Application.Current.Shutdown();
    }

    private void SaveVault()
    {
        if (IsLocked || string.IsNullOrWhiteSpace(_currentMasterPassword))
        {
            return;
        }

        try
        {
            _vaultData.Entries = [.. _entries];
            _vaultService.Save(_vaultData, _currentMasterPassword);
            UpdateCounts();
        }
        catch (Exception exception)
        {
            LogService.Error("SaveVault", exception);
            MessageBox.Show(this, exception.Message, "Save Vault", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Vault save failed.";
        }
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(FavoriteCount));
    }

    private TimeSpan GetRemainingSessionTime()
    {
        return _sessionTimeout - (DateTime.UtcNow - _lastInteractionUtc);
    }

    private void TouchSession()
    {
        if (IsLocked)
        {
            return;
        }

        _lastInteractionUtc = DateTime.UtcNow;
        OnPropertyChanged(nameof(SessionStatus));
    }

    private void CopyToClipboard(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            StatusMessage = $"No {description.ToLowerInvariant()} available to copy.";
            return;
        }

        try
        {
            Clipboard.SetText(value);
            _lastClipboardValue = value;
            _clipboardTimer.Stop();
            _clipboardTimer.Start();
            StatusMessage = $"{description} copied. Clipboard will clear in 30 seconds.";
        }
        catch (Exception exception)
        {
            LogService.Error($"CopyToClipboard:{description}", exception);
            StatusMessage = "Clipboard could not be updated.";
        }
    }

    private void GeneratePassword(bool updateStatus = true)
    {
        PasswordGenerationOptions options = new()
        {
            Length = ParseGeneratorLength(),
            IncludeUppercase = IncludeUppercase,
            IncludeLowercase = IncludeLowercase,
            IncludeNumbers = IncludeNumbers,
            IncludeSymbols = IncludeSymbols,
            ExcludeAmbiguousCharacters = ExcludeAmbiguousCharacters
        };

        GeneratedPassword = PasswordGeneratorService.Generate(options);

        if (updateStatus)
        {
            StatusMessage = "Generated a new password.";
        }
    }

    private int ParseGeneratorLength()
    {
        if (!int.TryParse(GeneratorLength, out int length))
        {
            throw new InvalidOperationException("Password length must be a number.");
        }

        return length;
    }

    private void HandleMouseInteraction(object sender, MouseButtonEventArgs e)
    {
        TouchSession();
    }

    private void HandleKeyboardInteraction(object sender, InputKeyEventArgs e)
    {
        TouchSession();
    }

    private void GeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            GeneratePassword();
        }
        catch (Exception exception)
        {
            LogService.Error("GeneratePassword_Click", exception);
            StatusMessage = exception.Message;
            MessageBox.Show(this, exception.Message, "Password Generator", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyGeneratedPassword_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(GeneratedPassword, "Generated password");
    }

    private void UseGeneratedPassword_Click(object sender, RoutedEventArgs e)
    {
        if (EditorEntry is null)
        {
            return;
        }

        if (EditorEntry.IsCardEntry)
        {
            StatusMessage = "Password generation is available only for password entries.";
            return;
        }

        if (string.IsNullOrWhiteSpace(GeneratedPassword))
        {
            try
            {
                GeneratePassword(updateStatus: false);
            }
            catch (Exception exception)
            {
                LogService.Error("UseGeneratedPassword_Click", exception);
                MessageBox.Show(this, exception.Message, "Use Generated Password", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusMessage = exception.Message;
                return;
            }
        }

        EditorEntry.Password = GeneratedPassword;
        StatusMessage = "Inserted the generated password into the editor.";
    }

    private void AddPasswordEntry_Click(object sender, RoutedEventArgs e)
    {
        BeginAddEntry(VaultEntryType.Password);
    }

    private void AddCardEntry_Click(object sender, RoutedEventArgs e)
    {
        BeginAddEntry(VaultEntryType.Card);
    }

    private void EditEntry_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null || IsLocked)
        {
            return;
        }

        _creatingNewEntry = false;
        IsEditingEntry = true;
        EditorEntry = SelectedEntry.Clone();
        StatusMessage = $"Editing {SelectedEntry.Title}.";
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        EditorEntry = null;
        IsEditingEntry = false;
        _creatingNewEntry = false;
        StatusMessage = "Edit cancelled.";
    }

    private void SaveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (EditorEntry is null || IsLocked)
        {
            return;
        }

        EditorEntry.Title = EditorEntry.Title.Trim();
        EditorEntry.Platform = EditorEntry.Platform.Trim();
        EditorEntry.Url = EditorEntry.Url.Trim();
        EditorEntry.UserName = EditorEntry.UserName.Trim();
        EditorEntry.CardholderName = EditorEntry.CardholderName.Trim();
        EditorEntry.CardNumber = EditorEntry.CardNumber.Trim();
        EditorEntry.CardExpiration = EditorEntry.CardExpiration.Trim();
        EditorEntry.CardSecurityCode = EditorEntry.CardSecurityCode.Trim();
        EditorEntry.CardPin = EditorEntry.CardPin.Trim();
        EditorEntry.Notes = EditorEntry.Notes.Trim();

        if (EditorEntry.IsPasswordEntry)
        {
            EditorEntry.CardholderName = string.Empty;
            EditorEntry.CardNumber = string.Empty;
            EditorEntry.CardExpiration = string.Empty;
            EditorEntry.CardSecurityCode = string.Empty;
            EditorEntry.CardPin = string.Empty;
        }
        else
        {
            EditorEntry.UserName = string.Empty;
            EditorEntry.Password = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(EditorEntry.Title))
        {
            EditorEntry.Title = DeriveEntryTitle(EditorEntry);
        }

        if (EditorEntry.IsPasswordEntry && string.IsNullOrWhiteSpace(EditorEntry.Password))
        {
            MessageBox.Show(this, "Password entries require a password.", "Save Entry", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (EditorEntry.IsCardEntry && string.IsNullOrWhiteSpace(EditorEntry.CardNumber))
        {
            MessageBox.Show(this, "Card entries require a card number.", "Save Entry", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_creatingNewEntry)
        {
            EditorEntry.CreatedAtUtc = DateTime.UtcNow;
            EditorEntry.Touch();
            _entries.Add(EditorEntry);
            SelectedEntry = EditorEntry;
            StatusMessage = $"Saved {EditorEntry.Title}.";
        }
        else if (SelectedEntry is not null)
        {
            SelectedEntry.ApplyFrom(EditorEntry);
            StatusMessage = $"Updated {SelectedEntry.Title}.";
        }

        EditorEntry = null;
        IsEditingEntry = false;
        _creatingNewEntry = false;

        ApplySort();
        EntriesView.Refresh();
        SaveVault();
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null || IsLocked)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            $"Delete {SelectedEntry.Title}? This removes it from the local vault.",
            "Delete Entry",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        string deletedTitle = SelectedEntry.Title;
        _entries.Remove(SelectedEntry);
        SelectedEntry = _entries.FirstOrDefault();
        SaveVault();
        EntriesView.Refresh();
        StatusMessage = $"Deleted {deletedTitle}.";
    }

    private void ToggleSensitiveVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null)
        {
            return;
        }

        _sensitiveDataVisible = !_sensitiveDataVisible;
        OnPropertyChanged(nameof(SelectedPasswordDisplay));
        OnPropertyChanged(nameof(SelectedCardNumberDisplay));
        OnPropertyChanged(nameof(SelectedCardSecurityCodeDisplay));
        OnPropertyChanged(nameof(SelectedCardPinDisplay));
        OnPropertyChanged(nameof(SelectedSensitiveToggleLabel));
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null || IsLocked)
        {
            return;
        }

        SelectedEntry.IsFavorite = !SelectedEntry.IsFavorite;
        SelectedEntry.Touch();
        ApplySort();
        EntriesView.Refresh();
        SaveVault();
        UpdateCounts();
        OnPropertyChanged(nameof(FavoriteActionLabel));
        StatusMessage = SelectedEntry.IsFavorite
            ? $"{SelectedEntry.Title} added to favorites."
            : $"{SelectedEntry.Title} removed from favorites.";
    }

    private void CopySelectedUsername_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null || SelectedEntry.IsCardEntry)
        {
            return;
        }

        CopyToClipboard(SelectedEntry.UserName, "Username");
    }

    private void CopySelectedPassword_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null || SelectedEntry.IsCardEntry)
        {
            return;
        }

        CopyToClipboard(SelectedEntry.Password, "Password");
    }

    private void CopySelectedCardNumber_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null || SelectedEntry.IsPasswordEntry)
        {
            return;
        }

        CopyToClipboard(SelectedEntry.CardNumber, "Card number");
    }

    private void CopySelectedCardSecurityCode_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null || SelectedEntry.IsPasswordEntry)
        {
            return;
        }

        CopyToClipboard(SelectedEntry.CardSecurityCode, "Security code");
    }

    private void CopySelectedCardPin_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is null || SelectedEntry.IsPasswordEntry)
        {
            return;
        }

        CopyToClipboard(SelectedEntry.CardPin, "Card PIN");
    }

    private void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (IsLocked)
        {
            return;
        }

        OpenFileDialog dialog = new()
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Import Vault Entries"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            IReadOnlyList<VaultEntry> importedEntries = _csvVaultService.Import(dialog.FileName);
            if (importedEntries.Count == 0)
            {
                MessageBox.Show(this, "No entries were found in that CSV file.", "Import CSV", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (VaultEntry entry in importedEntries)
            {
                entry.RaiseDerivedPropertiesChanged();
                _entries.Add(entry);
            }

            SelectedEntry = importedEntries.Last();
            ApplySort();
            EntriesView.Refresh();
            SaveVault();
            LogService.Info("ImportCsv_Click", $"Imported {importedEntries.Count} entries from {dialog.FileName}.");
            StatusMessage = $"Imported {importedEntries.Count} entries from CSV.";
        }
        catch (Exception exception)
        {
            LogService.Error("ImportCsv_Click", exception);
            MessageBox.Show(this, exception.Message, "Import CSV", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "CSV import failed.";
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (IsLocked)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "CSV files (*.csv)|*.csv",
            Title = "Export Vault Entries",
            FileName = $"vault-entries-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _csvVaultService.Export(dialog.FileName, _entries);
            LogService.Info("ExportCsv_Click", $"Exported {_entries.Count} entries to {dialog.FileName}.");
            StatusMessage = "CSV export completed.";
        }
        catch (Exception exception)
        {
            LogService.Error("ExportCsv_Click", exception);
            MessageBox.Show(this, exception.Message, "Export CSV", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "CSV export failed.";
        }
    }

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (IsLocked || string.IsNullOrWhiteSpace(_currentMasterPassword))
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "Encrypted vault backup (*.pmvault)|*.pmvault",
            Title = "Export Encrypted Backup",
            FileName = $"password-vault-backup-{DateTime.Now:yyyyMMdd-HHmm}.pmvault"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _vaultData.Entries = [.. _entries];
            _vaultService.ExportBackup(_vaultData, _currentMasterPassword, dialog.FileName);
            LogService.Info("ExportBackup_Click", $"Exported encrypted backup to {dialog.FileName}.");
            StatusMessage = "Encrypted backup exported.";
        }
        catch (Exception exception)
        {
            LogService.Error("ExportBackup_Click", exception);
            MessageBox.Show(this, exception.Message, "Export Backup", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Backup export failed.";
        }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "Encrypted vault backup (*.pmvault)|*.pmvault|All files (*.*)|*.*",
            Title = "Restore Encrypted Backup"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(_vaultService.VaultPath), StringComparison.OrdinalIgnoreCase))
        {
            LockVault("Selected backup is already the active vault file.");
            return;
        }

        if (_vaultService.VaultExists)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Restore this backup and replace the current local vault file?",
                "Restore Backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            _vaultService.RestoreBackup(dialog.FileName);
            HasVaultFile = true;
            LogService.Info("RestoreBackup_Click", $"Restored encrypted backup from {dialog.FileName}.");
            LockVault("Backup restored. Unlock with that backup's master password.");
        }
        catch (Exception exception)
        {
            LogService.Error("RestoreBackup_Click", exception);
            MessageBox.Show(this, exception.Message, "Restore Backup", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Backup restore failed.";
        }
    }

    private void LockVault_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLocked)
        {
            LockVault("Vault locked manually.");
        }
    }

    private void CreateVault_Click(object sender, RoutedEventArgs e)
    {
        string password = CreatePasswordBox.Password;
        string confirmation = ConfirmPasswordBox.Password;

        if (password.Length < 10)
        {
            MessageBox.Show(this, "Use a master password with at least 10 characters.", "Create Vault", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "The master password confirmation does not match.", "Create Vault", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            VaultData vault = _vaultService.CreateNewVault(password);
            HasVaultFile = true;
            LogService.Info("CreateVault_Click", "Created a new vault.");
            UnlockWithVault(vault, password, "Vault created and unlocked.");
        }
        catch (Exception exception)
        {
            LogService.Error("CreateVault_Click", exception);
            MessageBox.Show(this, exception.Message, "Create Vault", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Vault creation failed.";
        }
    }

    private void UnlockVault_Click(object sender, RoutedEventArgs e)
    {
        string password = UnlockPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show(this, "Enter the master password to unlock the vault.", "Unlock Vault", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            VaultData vault = _vaultService.Unlock(password);
            UnlockWithVault(vault, password, "Vault unlocked.");
        }
        catch (Exception exception)
        {
            LogService.Error("UnlockVault_Click", exception);
            MessageBox.Show(this, exception.Message, "Unlock Vault", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Unlock failed.";
            UnlockPasswordBox.SelectAll();
            UnlockPasswordBox.Focus();
        }
    }

    private void UnlockPasswordBox_KeyDown(object sender, InputKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            UnlockVault_Click(sender, e);
        }
    }

    private void SortMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ApplySort();
    }

    private static string MaskPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "(empty)";
        }

        return new string('*', Math.Min(password.Length, 18));
    }

    private static string MaskCardNumber(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return "(empty)";
        }

        string digits = new(cardNumber.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return "****";
        }

        if (digits.Length <= 4)
        {
            return new string('*', digits.Length);
        }

        string lastFour = digits[^4..];
        int maskedGroups = Math.Max(1, (digits.Length - 4 + 3) / 4);
        string maskedPrefix = string.Join(" ", Enumerable.Repeat("****", maskedGroups));
        return $"{maskedPrefix} {lastFour}";
    }

    private static string MaskShortSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        return new string('*', Math.Min(value.Length, 8));
    }

    private void BeginAddEntry(VaultEntryType entryType)
    {
        if (IsLocked)
        {
            return;
        }

        _creatingNewEntry = true;
        IsEditingEntry = true;

        EditorEntry = new VaultEntry
        {
            EntryType = entryType,
            Password = entryType == VaultEntryType.Password ? GeneratedPassword : string.Empty
        };

        StatusMessage = entryType == VaultEntryType.Card
            ? "Adding a new card."
            : "Adding a new password entry.";
    }

    private static string DeriveEntryTitle(VaultEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Platform))
        {
            return entry.Platform;
        }

        if (entry.IsCardEntry)
        {
            if (!string.IsNullOrWhiteSpace(entry.CardholderName))
            {
                return entry.CardholderName;
            }

            string lastFour = LastFourDigits(entry.CardNumber);
            return !string.IsNullOrWhiteSpace(lastFour)
                ? $"Card ending in {lastFour}"
                : "Payment Card";
        }

        return "Untitled Entry";
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

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
