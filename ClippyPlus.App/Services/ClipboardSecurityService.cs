using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ClippyPlus.App.Services;

public sealed class ClipboardSecurityService : IClipboardSecurityService
{
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int DerivedKeySize = 96;
    private const int Pbkdf2Iterations = 600_000;
    private const string MetadataKey = "Password";
    private static readonly byte[] VerifierMessage = Encoding.UTF8.GetBytes("ClippyPlus clipboard history password verifier v1");

    private readonly string _databasePath;
    private SecurityMetadata? _metadata;
    private byte[]? _encryptionKey;
    private byte[]? _hashKey;

    public ClipboardSecurityService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClippyPlus",
            "clipboard-history.db"))
    {
    }

    public ClipboardSecurityService(string databasePath)
    {
        _databasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        Initialize();
        _metadata = LoadMetadata();
    }

    public bool IsPasswordConfigured => _metadata is not null;

    public bool IsUnlocked => _encryptionKey is not null && _hashKey is not null;

    public event EventHandler? Unlocked;

    public void SetPassword(string password)
    {
        ValidatePassword(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var derived = DeriveKeys(password, salt, Pbkdf2Iterations);
        var verifier = ComputeVerifier(derived);
        SaveMetadata(salt, Pbkdf2Iterations, verifier);
        SetActiveKeys(derived);
    }

    public bool TryUnlock(string password)
    {
        if (_metadata is not { } metadata)
        {
            return false;
        }

        var derived = DeriveKeys(password, metadata.Salt, metadata.Iterations);
        var verifier = ComputeVerifier(derived);
        if (!CryptographicOperations.FixedTimeEquals(verifier, metadata.Verifier))
        {
            CryptographicOperations.ZeroMemory(derived);
            return false;
        }

        SetActiveKeys(derived);
        return true;
    }

    public string ProtectString(string value)
    {
        return Convert.ToBase64String(ProtectBytes(Encoding.UTF8.GetBytes(value)));
    }

    public string UnprotectString(string protectedValue)
    {
        return Encoding.UTF8.GetString(UnprotectBytes(Convert.FromBase64String(protectedValue)));
    }

    public byte[] ProtectBytes(byte[] value)
    {
        var key = RequireKey(_encryptionKey);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[value.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, value, ciphertext, tag);

        var protectedValue = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, protectedValue, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, protectedValue, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, protectedValue, NonceSize + TagSize, ciphertext.Length);
        return protectedValue;
    }

    public byte[] UnprotectBytes(byte[] protectedValue)
    {
        var key = RequireKey(_encryptionKey);
        if (protectedValue.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Protected clipboard value is invalid.");
        }

        var nonce = protectedValue.AsSpan(0, NonceSize);
        var tag = protectedValue.AsSpan(NonceSize, TagSize);
        var ciphertext = protectedValue.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    public string ComputeProtectedContentHash(string contentHash)
    {
        var key = RequireKey(_hashKey);
        using var hmac = new HMACSHA256(key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(contentHash)));
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ClipboardSecurityMetadata (
                Key TEXT PRIMARY KEY,
                Salt BLOB NOT NULL,
                Iterations INTEGER NOT NULL,
                Verifier BLOB NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private SecurityMetadata? LoadMetadata()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Salt, Iterations, Verifier
            FROM ClipboardSecurityMetadata
            WHERE Key = $key;
            """;
        command.Parameters.AddWithValue("$key", MetadataKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new SecurityMetadata(
            (byte[])reader["Salt"],
            reader.GetInt32(1),
            (byte[])reader["Verifier"]);
    }

    private void SaveMetadata(byte[] salt, int iterations, byte[] verifier)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ClipboardSecurityMetadata (Key, Salt, Iterations, Verifier)
            VALUES ($key, $salt, $iterations, $verifier)
            ON CONFLICT(Key) DO UPDATE SET
                Salt = excluded.Salt,
                Iterations = excluded.Iterations,
                Verifier = excluded.Verifier;
            """;
        command.Parameters.AddWithValue("$key", MetadataKey);
        command.Parameters.AddWithValue("$salt", salt);
        command.Parameters.AddWithValue("$iterations", iterations);
        command.Parameters.AddWithValue("$verifier", verifier);
        command.ExecuteNonQuery();
        _metadata = new SecurityMetadata(salt, iterations, verifier);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private void SetActiveKeys(byte[] derived)
    {
        _encryptionKey = derived[..32].ToArray();
        _hashKey = derived[32..64].ToArray();
        CryptographicOperations.ZeroMemory(derived);
        Unlocked?.Invoke(this, EventArgs.Empty);
    }

    private static byte[] DeriveKeys(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            DerivedKeySize);
    }

    private static byte[] ComputeVerifier(byte[] derived)
    {
        using var hmac = new HMACSHA256(derived[64..96]);
        return hmac.ComputeHash(VerifierMessage);
    }

    private static byte[] RequireKey(byte[]? key)
    {
        return key ?? throw new InvalidOperationException(LocalizationService.T("Security.Locked"));
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new ArgumentException(LocalizationService.T("Security.PasswordTooShort"), nameof(password));
        }
    }

    private sealed record SecurityMetadata(byte[] Salt, int Iterations, byte[] Verifier);
}
