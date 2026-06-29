namespace ClippyPlus.App.Services;

public interface IClipboardSecurityService
{
    bool IsPasswordConfigured { get; }

    bool IsUnlocked { get; }

    event EventHandler? Unlocked;

    void SetPassword(string password);

    bool TryUnlock(string password);

    string ProtectString(string value);

    string UnprotectString(string protectedValue);

    byte[] ProtectBytes(byte[] value);

    byte[] UnprotectBytes(byte[] protectedValue);

    string ComputeProtectedContentHash(string contentHash);
}
