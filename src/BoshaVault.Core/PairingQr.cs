using QRCoder;

namespace BoshaVault.Core;

public static class PairingQr
{
    // Wi-Fi codes are temporary. Cloud connection codes contain the recovery
    // key, so show them only when explicitly requested and render locally.
    public static byte[] Render(string pairingCode)
    {
        if (!(pairingCode.StartsWith("BV1:", StringComparison.Ordinal) || pairingCode.StartsWith("BVCLOUD1:", StringComparison.Ordinal)) || pairingCode.Length > 4096)
            throw new VaultException("A Lockvyn pairing code is required.");
        using var data = QRCodeGenerator.GenerateQrCode(pairingCode, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(8, drawQuietZones: true);
    }
}
