using QRCoder;

namespace BoshaVault.Core;

public static class PairingQr
{
    // New-device invitations contain only short-lived public ECDH material.
    // Keep older QR formats for users needing offline or recovery pairing.
    public static byte[] Render(string pairingCode)
    {
        if (string.IsNullOrEmpty(pairingCode) || pairingCode.Length > 4096)
            throw new VaultException("A supported Lockvyn pairing code is required.");
        if (pairingCode.StartsWith(DevicePairing.Prefix, StringComparison.Ordinal))
            DevicePairing.ParseInvitation(pairingCode);
        else if (!pairingCode.StartsWith("BV1:", StringComparison.Ordinal) &&
                 !pairingCode.StartsWith("BVCLOUD1:", StringComparison.Ordinal))
            throw new VaultException("A supported Lockvyn pairing code is required.");
        using var data = QRCodeGenerator.GenerateQrCode(pairingCode, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(8, drawQuietZones: true);
    }
}
