using System.Security.Cryptography;
using Guardian.Core.Security;

namespace Guardian.Service;

/// <summary>Wraps the config data key with DPAPI (LocalMachine scope).</summary>
public sealed class DpapiKeyProtector : IKeyProtector
{
    private static readonly byte[] Entropy = "Guardian.ConfigKey.v1"u8.ToArray();
    public byte[] Protect(byte[] d) => ProtectedData.Protect(d, Entropy, DataProtectionScope.LocalMachine);
    public byte[] Unprotect(byte[] d) => ProtectedData.Unprotect(d, Entropy, DataProtectionScope.LocalMachine);
}
