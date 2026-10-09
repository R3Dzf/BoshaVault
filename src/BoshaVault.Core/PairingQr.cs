using QRCoder;

namespace BoshaVault.Core;

public static class PairingQr
{
    // The QR carries the same temporary capability as manual pairing, never a passphrase.
    // Render locally; do not send pairing codes to an online QR service.
    public static byte[] Render(string pairingCode)
    {
        if (!pairingCode.StartsWith("BV1:", StringComparison.Ordinal) || pairingCode.Length > 4096)
            throw new VaultException("A BoshaVault pairing code is required.");
        using var data = QRCodeGenerator.GenerateQrCode(pairingCode, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(8, drawQuietZones: true);
    }
}
