namespace PasswordManager.Models;

public sealed class EncryptedVaultFile
{
    public int Version { get; set; }

    public int Iterations { get; set; }

    public string Salt { get; set; } = string.Empty;

    public string Nonce { get; set; } = string.Empty;

    public string Ciphertext { get; set; } = string.Empty;
}
