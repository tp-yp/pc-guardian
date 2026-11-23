using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Guardian.Core.Config;
using Guardian.Core.Security;

namespace Guardian.Core.Storage;

/// <summary>
/// Stores the config encrypted with a random data key; the data key itself is
/// wrapped by an <see cref="IKeyProtector"/> (DPAPI machine scope on Windows).
/// </summary>
public sealed class EncryptedConfigStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(), new HhMmTimeOnlyConverter() },
    };

    private readonly string _configPath, _keyPath;
    private readonly IKeyProtector _protector;

    public EncryptedConfigStore(string directory, IKeyProtector protector)
    {
        Directory.CreateDirectory(directory);
        _configPath = Path.Combine(directory, "config.bin");
        _keyPath = Path.Combine(directory, "key.bin");
        _protector = protector;
    }

    public bool Exists => File.Exists(_configPath);

    public DateTime LastWriteUtc => File.GetLastWriteTimeUtc(_configPath);

    public GuardianConfig Load()
    {
        var plain = ConfigCipher.Decrypt(LoadOrCreateKey(), File.ReadAllBytes(_configPath));
        return JsonSerializer.Deserialize<GuardianConfig>(plain, Json)
               ?? throw new InvalidDataException("Empty config.");
    }

    public void Save(GuardianConfig config)
    {
        var blob = ConfigCipher.Encrypt(LoadOrCreateKey(), JsonSerializer.SerializeToUtf8Bytes(config, Json));
        WriteAtomic(_configPath, blob);
    }

    /// <summary>Applies a change only if the password is the admin or recovery password.</summary>
    public AuthResult Update(AdminGate gate, string password, Action<GuardianConfig> change)
    {
        var cfg = Load();
        var auth = gate.Authenticate(cfg, password);
        if (auth is AuthResult.Admin or AuthResult.Recovery) { change(cfg); Save(cfg); }
        return auth;
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(_keyPath)) return _protector.Unprotect(File.ReadAllBytes(_keyPath));
        var key = RandomNumberGenerator.GetBytes(32);
        WriteAtomic(_keyPath, _protector.Protect(key));
        return key;
    }

    private static void WriteAtomic(string path, byte[] data)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}
