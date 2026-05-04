namespace MyAgentChat.Nostr;

/// <summary>
/// Minimal Bech32 encoder/decoder for NIP-19 (npub, nsec, etc.).
/// </summary>
public static class Bech32
{
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

    public static string Encode(string hrp, byte[] data)
    {
        var values = ConvertBits(data, 8, 5, true);
        var checksum = CreateChecksum(hrp, values);
        var combined = values.Concat(checksum).ToArray();
        return hrp + "1" + string.Concat(combined.Select(v => Charset[v]));
    }

    public static byte[]? Decode(string bech32, out string? hrp)
    {
        hrp = null;
        var pos = bech32.LastIndexOf('1');
        if (pos < 1 || pos + 7 > bech32.Length)
            return null;

        hrp = bech32[..pos];
        var data = new byte[bech32.Length - pos - 1];
        for (int i = 0; i < data.Length; i++)
        {
            var idx = Charset.IndexOf(bech32[pos + 1 + i]);
            if (idx < 0) return null;
            data[i] = (byte)idx;
        }

        // Strip 6-byte checksum
        var values = data[..^6];
        return ConvertBits(values, 5, 8, false);
    }

    private static byte[] ConvertBits(byte[] data, int fromBits, int toBits, bool pad)
    {
        var result = new List<byte>();
        int acc = 0;
        int bits = 0;
        int maxv = (1 << toBits) - 1;

        foreach (var value in data)
        {
            acc = (acc << fromBits) | value;
            bits += fromBits;
            while (bits >= toBits)
            {
                bits -= toBits;
                result.Add((byte)((acc >> bits) & maxv));
            }
        }

        if (pad && bits > 0)
        {
            result.Add((byte)((acc << (toBits - bits)) & maxv));
        }

        return result.ToArray();
    }

    private static byte[] CreateChecksum(string hrp, byte[] values)
    {
        var enc = HrpExpand(hrp).Concat(values).Concat(new byte[6]).ToArray();
        var polymod = Polymod(enc) ^ 1;
        var result = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            result[i] = (byte)((polymod >> (5 * (5 - i))) & 31);
        }
        return result;
    }

    private static byte[] HrpExpand(string hrp)
    {
        var result = new byte[hrp.Length * 2 + 1];
        for (int i = 0; i < hrp.Length; i++)
        {
            result[i] = (byte)(hrp[i] >> 5);
            result[i + hrp.Length + 1] = (byte)(hrp[i] & 31);
        }
        result[hrp.Length] = 0;
        return result;
    }

    private static uint Polymod(byte[] values)
    {
        uint[] generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];
        uint chk = 1;
        foreach (var v in values)
        {
            var top = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ v;
            for (int i = 0; i < 5; i++)
            {
                if (((top >> i) & 1) == 1)
                    chk ^= generator[i];
            }
        }
        return chk;
    }
}
