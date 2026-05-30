using System.Security.Cryptography;
using System.Text.Json;
using System.IO;
using PasswordManager.Models;

namespace PasswordManager.Services;

public sealed class VaultService
{
    private const int CurrentVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int KeySize = 32;
    private const int TagSize = 16;
    private const int Iterations = 210_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public VaultService()
    {
        string localAppDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PasswordManager");

        AppDirectory = GetPreferredAppDirectory(localAppDirectory);
        VaultPath = Path.Combine(AppDirectory, "vault.pmvault");
        string legacyVaultPath = Path.Combine(localAppDirectory, "vault.pmvault");

        CopyLegacyVaultIfNeeded(legacyVaultPath, VaultPath);
    }

    public string AppDirectory { get; }

    public string VaultPath { get; }

    public bool VaultExists => File.Exists(VaultPath);

    public VaultData CreateNewVault(string masterPassword)
    {
        VaultData vault = new();
        Save(vault, masterPassword);
        return vault;
    }

    public VaultData Unlock(string masterPassword)
    {
        return LoadFromFile(VaultPath, masterPassword);
    }

    public VaultData LoadFromFile(string path, string masterPassword)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Vault file was not found.", path);
        }

        EncryptedVaultFile? file = JsonSerializer.Deserialize<EncryptedVaultFile>(File.ReadAllText(path), JsonOptions);
        if (file is null || file.Version != CurrentVersion)
        {
            throw new InvalidOperationException("Vault file format is not supported.");
        }

        byte[] salt = Convert.FromBase64String(file.Salt);
        byte[] nonce = Convert.FromBase64String(file.Nonce);
        byte[] cipherAndTag = Convert.FromBase64String(file.Ciphertext);

        if (cipherAndTag.Length <= TagSize)
        {
            throw new InvalidOperationException("Vault file is corrupt.");
        }

        byte[] cipherText = cipherAndTag[..^TagSize];
        byte[] tag = cipherAndTag[^TagSize..];
        byte[] plaintext = new byte[cipherText.Length];
        byte[] key = DeriveKey(masterPassword, salt, file.Iterations);

        try
        {
            using AesGcm aes = new(key, TagSize);
            aes.Decrypt(nonce, cipherText, tag, plaintext);

            VaultData? vault = JsonSerializer.Deserialize<VaultData>(plaintext, JsonOptions);
            if (vault is null)
            {
                throw new InvalidOperationException("Vault data could not be read.");
            }

            return vault;
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Unlock failed. Check the master password or restore a valid backup.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Save(VaultData vault, string masterPassword)
    {
        Directory.CreateDirectory(AppDirectory);
        WriteToPath(VaultPath, vault, masterPassword);
    }

    public void ExportBackup(VaultData vault, string masterPassword, string targetPath)
    {
        WriteToPath(targetPath, vault, masterPassword);
    }

    public void RestoreBackup(string backupPath)
    {
        Directory.CreateDirectory(AppDirectory);
        File.Copy(backupPath, VaultPath, overwrite: true);
    }

    private static void WriteToPath(string path, VaultData vault, string masterPassword)
    {
        vault.Touch();

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = DeriveKey(masterPassword, salt, Iterations);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(vault, JsonOptions);
        byte[] cipherText = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        try
        {
            using AesGcm aes = new(key, TagSize);
            aes.Encrypt(nonce, plaintext, cipherText, tag);

            byte[] payload = [.. cipherText, .. tag];
            EncryptedVaultFile file = new()
            {
                Version = CurrentVersion,
                Iterations = Iterations,
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(payload)
            };

            string serializedFile = JsonSerializer.Serialize(file, JsonOptions);
            File.WriteAllText(path, serializedFile);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(cipherText);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    private static byte[] DeriveKey(string masterPassword, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(masterPassword, salt, iterations, HashAlgorithmName.SHA512, KeySize);
    }

    private static string GetPreferredAppDirectory(string fallbackDirectory)
    {
        string? oneDriveRoot = GetOneDriveRoot();
        if (string.IsNullOrWhiteSpace(oneDriveRoot))
        {
            return fallbackDirectory;
        }

        return Path.Combine(oneDriveRoot, "PasswordManager");
    }

    private static string? GetOneDriveRoot()
    {
        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("OneDrive"),
            Environment.GetEnvironmentVariable("OneDriveConsumer"),
            Environment.GetEnvironmentVariable("OneDriveCommercial"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive")
        ];

        return candidates.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
    }

    private static void CopyLegacyVaultIfNeeded(string legacyVaultPath, string activeVaultPath)
    {
        if (string.Equals(legacyVaultPath, activeVaultPath, StringComparison.OrdinalIgnoreCase) ||
            File.Exists(activeVaultPath) ||
            !File.Exists(legacyVaultPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(activeVaultPath)!);
        File.Copy(legacyVaultPath, activeVaultPath, overwrite: false);
    }
}
