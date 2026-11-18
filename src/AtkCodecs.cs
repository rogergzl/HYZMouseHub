using System;
using System.Collections.Generic;

namespace HyzMouseHub
{
    // Ported from the public OpenMouse ATK codec (MIT).
    // ATK stores DPI stages in EEPROM; every value/checksum pair sums to 0x55.
    internal static class AtkCodecs
    {
        private const int ChecksumTotal = 0x55;

        internal enum Family
        {
            Ultra,
            Step50,
            Stepped,
            Paw3395Se,
            Paw3955Master
        }

        internal sealed class Profile
        {
            public Family Family;
            public int Min;
            public int Max;
            public int XStep = 1;
            public int YStep = 1;
        }

        private static readonly Dictionary<string, Profile> Profiles = BuildProfiles();

        private static Dictionary<string, Profile> BuildProfiles()
        {
            Dictionary<string, Profile> d = new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase);
            d["PAW3950Ultra"] = new Profile { Family = Family.Ultra, Min = 10, Max = 42000 };
            d["PAW3950"] = new Profile { Family = Family.Step50, Min = 50, Max = 36000 };
            d["PAW3950DM"] = new Profile { Family = Family.Step50, Min = 50, Max = 36000 };
            d["PAW3395Ultra"] = new Profile { Family = Family.Step50, Min = 100, Max = 30000 };
            d["PAW3395"] = new Profile { Family = Family.Step50, Min = 100, Max = 30000 };
            d["PAW3395SE"] = new Profile { Family = Family.Paw3395Se, Min = 200, Max = 18000 };
            d["PAW3315"] = new Profile { Family = Family.Stepped, Min = 200, Max = 16000, XStep = 100, YStep = 100 };
            d["PAW3311"] = new Profile { Family = Family.Stepped, Min = 200, Max = 15000, XStep = 100, YStep = 100 };
            d["PAW3320"] = new Profile { Family = Family.Stepped, Min = 200, Max = 4000, XStep = 39, YStep = 38 };
            d["CORE26K"] = new Profile { Family = Family.Step50, Min = 50, Max = 26000 };
            d["PAW3955Master"] = new Profile { Family = Family.Paw3955Master, Min = 50, Max = 40000 };
            return d;
        }

        internal static Profile GetProfile(string sensor)
        {
            Profile p;
            if (!string.IsNullOrEmpty(sensor) && Profiles.TryGetValue(sensor, out p)) return p;
            return null;
        }

        internal static int StageLength(string sensor)
        {
            Profile p = GetProfile(sensor);
            return p != null && p.Family == Family.Paw3955Master ? 6 : 4;
        }

        internal static bool IsValid(string sensor, int x, int y)
        {
            Profile p = GetProfile(sensor);
            if (p == null) return false;
            if (x < p.Min || x > p.Max || y < p.Min || y > p.Max) return false;
            if (p.Family == Family.Stepped)
            {
                return (x - p.Min) % p.XStep == 0 && (y - p.Min) % p.YStep == 0;
            }
            return IsDpiOption(sensor, x) && IsDpiOption(sensor, y);
        }

        internal static int Clamp(string sensor, int dpi)
        {
            Profile p = GetProfile(sensor);
            if (p == null) return dpi;
            if (dpi < p.Min) return p.Min;
            if (dpi > p.Max) return p.Max;
            if (p.Family == Family.Stepped)
            {
                int steps = (dpi - p.Min) / p.XStep;
                int value = p.Min + steps * p.XStep;
                if (value > p.Max) value -= p.XStep;
                return value;
            }
            int best = p.Min;
            int diff = int.MaxValue;
            for (int v = p.Min; v <= p.Max; v += p.Family == Family.Ultra ? 10 : 50)
            {
                int d = Math.Abs(v - dpi);
                if (d < diff) { diff = d; best = v; }
            }
            return best;
        }

        private static bool IsDpiOption(string sensor, int dpi)
        {
            Profile p = GetProfile(sensor);
            if (p == null) return false;
            switch (p.Family)
            {
                case Family.Ultra:
                    if (dpi >= p.Min && dpi <= 10000) return dpi % 10 == 0;
                    if (dpi > 10000 && dpi <= 30000) return dpi >= 10050 && (dpi - 10050) % 50 == 0;
                    return dpi > 30000 && dpi <= p.Max && (dpi - 30100) % 100 == 0;
                case Family.Step50:
                    if (dpi > 30000) return (dpi / 2) % 50 == 0;
                    return dpi % 50 == 0;
                case Family.Paw3395Se:
                    if (dpi <= 10000) return dpi % 50 == 0;
                    return (dpi - 10100) % 100 == 0 && dpi % 2 == 0;
                case Family.Paw3955Master:
                    return dpi % 10 == 0; // UI snaps by 10; 1-DPI values are also representable.
                default:
                    return false;
            }
        }

        private sealed class AxisCode
        {
            public int Byte;
            public int Nibble;
        }

        private static AxisCode EncodeUltra(int dpi)
        {
            int value = dpi;
            int doubled = 0;
            if (value > 30000) { doubled = 1; value = value / 2; }
            AxisCode c = new AxisCode();
            if (value <= 10000)
            {
                int code = value / 10 - 1;
                c.Byte = code & 0xff;
                c.Nibble = ((code >> 8) << 2) | doubled;
                return c;
            }
            int code2 = (value - 10050) / 50;
            c.Byte = code2 & 0xff;
            c.Nibble = ((code2 >> 8) << 2) | 2 | doubled;
            return c;
        }

        private static int DecodeUltra(int b, int nibble)
        {
            int code = (((nibble >> 2) & 0x03) << 8) | (b & 0xff);
            int baseValue = (nibble & 2) != 0 ? 10050 + code * 50 : (code + 1) * 10;
            return (nibble & 1) != 0 ? baseValue * 2 : baseValue;
        }

        private static AxisCode EncodeStep50(int dpi)
        {
            int doubled = dpi > 30000 ? 1 : 0;
            int count = dpi / (doubled == 1 ? 100 : 50) - 1;
            AxisCode c = new AxisCode();
            c.Byte = count & 0xff;
            c.Nibble = (((count >> 8) & 0x03) << 2) | doubled;
            return c;
        }

        private static int DecodeStep50(int b, int nibble)
        {
            int count = (b & 0xff) | (((nibble >> 2) & 0x03) << 8);
            int dpi = (count + 1) * 50;
            return (nibble & 1) != 0 ? dpi * 2 : dpi;
        }

        private static AxisCode EncodeStepped(int dpi, int min, int step)
        {
            int count = (dpi - min) / step;
            AxisCode c = new AxisCode();
            c.Byte = count & 0xff;
            c.Nibble = ((count >> 8) & 0x03) << 2;
            return c;
        }

        private static int DecodeStepped(int b, int nibble, int min, int step)
        {
            int count = (b & 0xff) | (((nibble >> 2) & 0x03) << 8);
            return min + count * step;
        }

        private static readonly int[] Paw3395SeInvalidCodes = new int[]
        {
            7, 13, 20, 26, 33, 40, 46, 53, 60, 66, 73, 80, 86, 93, 100, 106, 113,
            120, 126, 133, 140, 146, 153, 160, 166, 173, 180, 186, 193, 200, 206,
            213, 220, 226, 233
        };

        private static readonly int[] Paw3395SeCodes = BuildSeCodes();

        private static int[] BuildSeCodes()
        {
            List<int> codes = new List<int>();
            for (int i = 1; i <= 235; i++)
            {
                bool invalid = false;
                for (int j = 0; j < Paw3395SeInvalidCodes.Length; j++)
                {
                    if (Paw3395SeInvalidCodes[j] == i) { invalid = true; break; }
                }
                if (!invalid) codes.Add(i);
            }
            return codes.ToArray();
        }

        private static AxisCode EncodeSe(int dpi)
        {
            int doubled = dpi > 10000 ? 1 : 0;
            int baseDpi = doubled == 1 ? dpi / 2 : dpi;
            if (baseDpi < 50 || baseDpi > 10000 || baseDpi % 50 != 0) return null;
            int index = baseDpi / 50 - 1;
            if (index < 0 || index >= Paw3395SeCodes.Length) return null;
            AxisCode c = new AxisCode();
            c.Byte = Paw3395SeCodes[index];
            c.Nibble = doubled == 1 ? 2 : 0;
            return c;
        }

        private static int DecodeSe(int b, int nibble)
        {
            if ((nibble & ~2) != 0) return -1;
            int index = -1;
            for (int i = 0; i < Paw3395SeCodes.Length; i++)
            {
                if (Paw3395SeCodes[i] == (b & 0xff)) { index = i; break; }
            }
            if (index < 0) return -1;
            int baseDpi = (index + 1) * 50;
            if ((nibble & 2) != 0) return baseDpi > 5000 ? baseDpi * 2 : -1;
            return baseDpi;
        }

        internal static byte[] Pack(string sensor, int x, int y)
        {
            Profile p = GetProfile(sensor);
            if (p == null || !IsValid(sensor, x, y)) return null;

            if (p.Family == Family.Paw3955Master)
            {
                int xb = x - 1;
                int yb = y - 1;
                byte[] row = new byte[6];
                row[0] = (byte)(xb & 0xff);
                row[1] = (byte)((xb >> 8) & 0xff);
                row[2] = (byte)(yb & 0xff);
                row[3] = (byte)((yb >> 8) & 0xff);
                row[4] = 0;
                row[5] = (byte)((ChecksumTotal - ((row[0] + row[1] + row[2] + row[3] + row[4]) & 0xff)) & 0xff);
                return row;
            }

            AxisCode cx;
            AxisCode cy;
            if (p.Family == Family.Stepped)
            {
                cx = EncodeStepped(x, p.Min, p.XStep);
                cy = EncodeStepped(y, p.Min, p.YStep);
            }
            else if (p.Family == Family.Paw3395Se)
            {
                cx = EncodeSe(x);
                cy = EncodeSe(y);
            }
            else if (p.Family == Family.Step50)
            {
                cx = EncodeStep50(x);
                cy = EncodeStep50(y);
            }
            else
            {
                cx = EncodeUltra(x);
                cy = EncodeUltra(y);
            }
            if (cx == null || cy == null) return null;
            int mode = ((cy.Nibble & 0x0f) << 4) | (cx.Nibble & 0x0f);
            int sum = (cx.Byte + cy.Byte + mode) & 0xff;
            return new byte[]
            {
                (byte)(cx.Byte & 0xff),
                (byte)(cy.Byte & 0xff),
                (byte)(mode & 0xff),
                (byte)((ChecksumTotal - sum) & 0xff)
            };
        }

        internal static bool Unpack(string sensor, byte[] data, out int x, out int y)
        {
            x = -1;
            y = -1;
            Profile p = GetProfile(sensor);
            if (p == null || data == null) return false;

            if (p.Family == Family.Paw3955Master)
            {
                if (data.Length < 6) return false;
                int sum = 0;
                for (int i = 0; i < 6; i++) sum += data[i];
                if ((sum & 0xff) != ChecksumTotal) return false;
                int flags = data[4];
                x = ((data[0] | (data[1] << 8)) + 1) * (((flags & 0x01) != 0) ? 2 : 1);
                y = ((data[2] | (data[3] << 8)) + 1) * (((flags & 0x10) != 0) ? 2 : 1);
                return true;
            }

            if (data.Length < 4) return false;
            int total = (data[0] + data[1] + data[2] + data[3]) & 0xff;
            if (total != ChecksumTotal) return false;

            if (p.Family == Family.Stepped)
            {
                x = DecodeStepped(data[0], data[2] & 0x0f, p.Min, p.XStep);
                y = DecodeStepped(data[1], (data[2] >> 4) & 0x0f, p.Min, p.YStep);
            }
            else if (p.Family == Family.Paw3395Se)
            {
                x = DecodeSe(data[0], data[2] & 0x0f);
                y = DecodeSe(data[1], (data[2] >> 4) & 0x0f);
            }
            else if (p.Family == Family.Step50)
            {
                x = DecodeStep50(data[0], data[2] & 0x0f);
                y = DecodeStep50(data[1], (data[2] >> 4) & 0x0f);
            }
            else
            {
                x = DecodeUltra(data[0], data[2] & 0x0f);
                y = DecodeUltra(data[1], (data[2] >> 4) & 0x0f);
            }
            return x > 0 && y > 0;
        }
    }
}
