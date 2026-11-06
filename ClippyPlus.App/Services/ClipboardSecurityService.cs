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
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT AutomaticKeys FROM ClipboardSecurityAccess WHERE Id = 1";
        if (command.ExecuteScalar() is byte[] automaticKeys)
        {
            SetActiveKeys(automaticKeys);
            IsPasswordEnabled = false;
        }
    }

    public bool IsPasswordConfigured => _metadata is not null;

    public bool IsPasswordEnabled { get; private set; } = true;

    public bool IsUnlocked => _encryptionKey is not null && _hashKey is not null;

    public event EventHandler? Unlocked;

    public void SetPassword(string password)
    {
        if (IsPasswordConfigured)
        {
            throw new InvalidOperationException("A password is already configured.");
        }
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

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT WrappedKeys FROM ClipboardSecurityAccess WHERE Id = 1";
        var keys = command.ExecuteScalar() is byte[] wrapped
            ? Decrypt(wrapped, derived[..32])
            : derived[..64];
        CryptographicOperations.ZeroMemory(derived);
        SetActiveKeys(keys);
        return true;
    }

    public bool SetPasswordProtection(string currentPassword, string? newPassword)
    {
        if (newPassword is not null)
        {
            ValidatePassword(newPassword);
        }
        if (!TryUnlock(currentPassword))
        {
            return false;
        }

        var keys = RequireKey(_encryptionKey).Concat(RequireKey(_hashKey)).ToArray();
        byte[]? derived = null;
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            SecurityMetadata? updatedMetadata = null;
            byte[]? wrapped = null;
            if (newPassword is not null)
            {
                var salt = RandomNumberGenerator.GetBytes(SaltSize);
                derived = DeriveKeys(newPassword, salt, Pbkdf2Iterations);
                updatedMetadata = new SecurityMetadata(salt, Pbkdf2Iterations, ComputeVerifier(derived));
                wrapped = Encrypt(keys, derived[..32]);
                using var metadataCommand = connection.CreateCommand();
                metadataCommand.Transaction = transaction;
                metadataCommand.CommandText = "UPDATE ClipboardSecurityMetadata SET Salt=$salt, Iterations=$iterations, Verifier=$verifier WHERE Key=$key";
                metadataCommand.Parameters.AddWithValue("$salt", salt);
                metadataCommand.Parameters.AddWithValue("$iterations", Pbkdf2Iterations);
                metadataCommand.Parameters.AddWithValue("$verifier", updatedMetadata.Verifier);
                metadataCommand.Parameters.AddWithValue("$key", MetadataKey);
                metadataCommand.ExecuteNonQuery();
            }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // Preserve the existing password wrapper when disabling protection.
            command.CommandText = newPassword is null
                ? "INSERT INTO ClipboardSecurityAccess (Id, AutomaticKeys) VALUES (1, $automatic) ON CONFLICT(Id) DO UPDATE SET AutomaticKeys=$automatic"
                : "INSERT INTO ClipboardSecurityAccess (Id, WrappedKeys, AutomaticKeys) VALUES (1, $wrapped, NULL) ON CONFLICT(Id) DO UPDATE SET WrappedKeys=$wrapped, AutomaticKeys=NULL";
            if (newPassword is null)
                command.Parameters.AddWithValue("$automatic", keys);
            else
                command.Parameters.AddWithValue("$wrapped", wrapped!);
            command.ExecuteNonQuery();
            transaction.Commit();
            if (updatedMetadata is not null) _metadata = updatedMetadata;
            IsPasswordEnabled = newPassword is not null;
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keys);
            if (derived is not null) CryptographicOperations.ZeroMemory(derived);
        }
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
        return Encrypt(value, RequireKey(_encryptionKey));
    }

    private static byte[] Encrypt(byte[] value, byte[] key)
    {
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
        return Decrypt(protectedValue, RequireKey(_encryptionKey));
    }

    private static byte[] Decrypt(byte[] protectedValue, byte[] key)
    {
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
            CREATE TABLE IF NOT EXISTS ClipboardSecurityAccess (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                WrappedKeys BLOB,
                AutomaticKeys BLOB
            );
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
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete = ON";
        command.ExecuteNonQuery();
        return connection;
    }

    private void SetActiveKeys(byte[] derived)
    {
        if (_encryptionKey is not null) CryptographicOperations.ZeroMemory(_encryptionKey);
        if (_hashKey is not null) CryptographicOperations.ZeroMemory(_hashKey);
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
