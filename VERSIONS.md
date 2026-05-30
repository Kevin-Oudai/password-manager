# Versions

This file tracks user-facing fixes, behavior changes, and feature additions.

## Unreleased

- Added OneDrive-first vault storage with fallback migration from the previous local AppData vault location.
- Updated publish flow so the root `PasswordManager.exe` is copied from a staged single-file publish output.
- Flattened the project structure so source files live directly in the repository root.
- Added repository safety rules to prevent vault data, backups, generated binaries, logs, and secrets from being committed.
