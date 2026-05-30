namespace PasswordManager.Models;

public sealed class VaultData
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<VaultEntry> Entries { get; set; } = [];

    public void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
