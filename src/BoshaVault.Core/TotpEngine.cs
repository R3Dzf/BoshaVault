using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BoshaVault.Core;

/// <summary>RFC 6238 TOTP core; not yet enabled for saved logins in the UI.</summary>
public static class TotpEngine
{
    public static byte[] DecodeBase32(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length>512)
            throw new VaultException("Invalid TOTP secret.");
        string value=encoded.Trim().ToUpperInvariant().TrimEnd('=');
        if(value.Length==0 || value.Any(c=>!((c>='A'&&c<='Z')||(c>='2'&&c<='7'))))
            throw new VaultException("Invalid TOTP Base32 secret.");
        int bits=0,buffer=0;
        List<byte> data=[];
        foreach(char c in value)
        {
            int n=c<='Z'?c-'A':c-'2'+26;
            buffer=(buffer<<5)|n;
            bits+=5;
            if(bits>=8)
            {
                bits-=8;
                data.Add((byte)((buffer>>bits)&0xff));
                buffer &= (1<<bits)-1;
            }
        }
        if(data.Count<10 || data.Count>128 || (bits!=0 && buffer!=0))
            throw new VaultException("Invalid TOTP secret length or padding.");
        return data.ToArray();
    }
    public static string Calculate(ReadOnlySpan<byte> key,DateTimeOffset timestamp,
        int digits=6,int periodSeconds=30,string algorithm="SHA1")
    {
        if(key.Length is < 10 or > 128 || digits is not (6 or 8) ||
           periodSeconds is < 15 or > 120)
            throw new VaultException("Invalid TOTP configuration.");
        long step=timestamp.ToUnixTimeSeconds()/periodSeconds;
        if(step<0)throw new VaultException("Invalid TOTP timestamp.");
        Span<byte> counter=stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter,step);
        byte[] digest=algorithm switch
        {
            "SHA1"=>HMACSHA1.HashData(key,counter),
            "SHA256"=>HMACSHA256.HashData(key,counter),
            "SHA512"=>HMACSHA512.HashData(key,counter),
            _=>throw new VaultException("Unsupported TOTP algorithm.")
        };
        try
        {
            int offset=digest[^1]&0x0f;
            int binary=((digest[offset]&0x7f)<<24)|((digest[offset+1]&0xff)<<16)|
                ((digest[offset+2]&0xff)<<8)|(digest[offset+3]&0xff);
            int modulus=digits==8?100_000_000:1_000_000;
            return (binary%modulus).ToString(digits==8?"D8":"D6",
                System.Globalization.CultureInfo.InvariantCulture);
        }
        finally {CryptographicOperations.ZeroMemory(digest);}
    }
}
