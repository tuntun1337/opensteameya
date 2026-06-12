using System.Text;

namespace SteamEyaWinUI.Services;

internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static string ComputeSteamAccountKey(string accountName)
    {
        var value = Compute(Encoding.UTF8.GetBytes(accountName));
        var hex = value.ToString("x8").TrimStart('0');
        return $"{hex}1";
    }

    private static uint Compute(byte[] bytes)
    {
        var crc = 0xffffffffu;

        foreach (var b in bytes)
        {
            crc = Table[(crc ^ b) & 0xff] ^ (crc >> 8);
        }

        return crc ^ 0xffffffffu;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 1 ? 0xedb88320u ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
