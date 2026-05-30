# Agent Instructions

This repository is public. Treat it as source-only and assume anything committed will be visible to everyone.

## Sensitive Data Rules

- Never commit vault files, vault backups, exported vault CSVs, logs, generated binaries, user-specific settings, private keys, tokens, or environment files.
- Keep the active encrypted vault outside the repository. The expected runtime location is `%OneDrive%\PasswordManager\vault.pmvault`.
- Keep generated artifacts out of git, including `PasswordManager.exe`, `bin/`, `obj/`, and `artifacts/`.
- Do not add real passwords, card details, recovery phrases, API keys, tokens, email inbox data, machine-specific logs, or personal absolute paths to examples, tests, docs, or screenshots.
- If a sensitive file is staged or committed locally, stop and fix the history before pushing. Do not push and then try to clean it up later.

## Required Checks Before Commit Or Push

Run these checks before committing or pushing:

```powershell
git status --short --ignored
git diff --cached --name-only
git ls-files | Select-String -Pattern '\.pmvault$|\.csv$|\.log$|\.exe$|\.dll$|\.pdb$|\.key$|\.pem$|\.pfx$|\.env$|vault-entries-|password-vault-backup-'
```

The final command should not show any tracked sensitive files. Source files that contain words like `Vault`, `Password`, or `EncryptedVaultFile` are expected; real user vault data is not.

## Version Tracking

Update `VERSIONS.md` for each user-facing fix, behavior change, or feature addition. Keep entries concise and date them.
