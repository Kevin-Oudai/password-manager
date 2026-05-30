# Password Manager

Local-only encrypted password and card manager for Windows.

## Vault Storage

The app stores the active encrypted vault in OneDrive when available:

```text
%OneDrive%\PasswordManager\vault.pmvault
```

The vault file is intentionally ignored by git. Do not commit `.pmvault` files.

## Build

```powershell
dotnet publish .\PasswordManager.csproj -p:PublishProfile=Distribution
```

The publish profile creates `PasswordManager.exe` in the project root. The executable is a generated artifact and is not tracked by git.
