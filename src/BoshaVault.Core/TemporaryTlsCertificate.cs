using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BoshaVault.Core;

public static class TemporaryTlsCertificate
{
    public static X509Certificate2 Create()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=BoshaVault temporary pairing", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        byte[] pfx = generated.Export(X509ContentType.Pfx);
        try
        {
            // Schannel cannot reliably use EphemeralKeySet TLS certificates. Import to the
            // user's temporary key container on Windows, without PersistKeySet. Disposal
            // lets .NET clean up that container. No machine store or root trust is changed.
            var flags = OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet;
            return X509CertificateLoader.LoadPkcs12(pfx, null, flags);
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
}
