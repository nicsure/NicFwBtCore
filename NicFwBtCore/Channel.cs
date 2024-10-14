using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NicFwBtCore
{
    public class Channel
    {

        // STATICS

        private static readonly List<Channel> bank = [];

        private static readonly Channel DummyChannel = new(250);

        static Channel()
        {
            for (int i = 1; i < 199; i++)
            {
                bank.Add(new(i));
            }
        }

        public static Channel Get(int channelNum)
        {
            return channelNum > 0 && channelNum < 199 ? bank[channelNum - 1] : DummyChannel;
        }

        private static string SubTone2String(int st)
        {
            if (st == 0) return "Off";
            if (st < 0x8000) return $"{st / 10.0:F1}";
            return $"D{Convert.ToString(st & 0x3fff, 8).PadLeft(3, '0')}{((st & 0x4000) == 0 ? 'N' : 'I')}";
        }

        private static int String2SubTone(string? sts)
        {
            if(string.IsNullOrEmpty(sts)) return 0;
            sts = sts.ToLower().Trim();
            if (string.IsNullOrEmpty(sts)) return 0;
            if (sts.StartsWith('d'))
            {
                int r;
                if (sts.EndsWith('n'))
                    r = 0x8000;
                else
                if (sts.EndsWith('i'))
                    r = 0xc000;
                else
                    return 0;
                sts = sts.Replace("d", "").Replace("n", "").Replace("i", "");
                int len = sts.Length;
                if(len==0 || len>3) return 0;
                int mul = 1;
                for (int i = sts.Length - 1; i >= 0; i--)
                {
                    char c = sts[i];
                    if (c < '0' || c > '7') return 0;
                    r += (c - '0') * mul;
                    mul *= 8;
                }
                return r;
            }
            else
            if (double.TryParse(sts, out double d))
            {
                return d <= 300 ? (int)Math.Round(d * 10) : 0;
            }
            else
                return 0;
        }

        private static string GroupW2String(int groupw)
        {
            string s = string.Empty;
            for (int i = 0; i < 4; i++)
            {
                int nyb = groupw & 0xf;
                groupw >>= 4;
                if (nyb > 0)
                    s += (char)(nyb + 64);
            }
            return s;
        }

        private static int String2GroupW(string gString)
        {
            int r = 0;
            gString = gString.ToUpper().Trim().PadRight(4)[..4];
            for (int i = gString.Length - 1; i >= 0; i--)
            {
                char c = gString[i];
                if (c >= 'A' && c <= 'O')
                {
                    int g = (c - 'A') + 1;
                    r <<= 4;
                    r += g;
                }
            }
            return r;
        }

        private static string Modulation2String(int mod)
        {
            return mod switch
            {
                1 => "FM",
                2 => "AM",
                3 => "USB",
                _ => "Auto",
            };
        }

        private static int String2Modulation(string mods)
        {
            return mods.ToUpper().Trim() switch
            {
                "FM" => 1,
                "AM" => 2,
                "USB" => 3,
                _ => 0
            };
        }

        // NON STATICS

        public byte[] Data { get; } = new byte[32];

        public int BlockNumber { get; private set; }

        public int ChannelNumber { get; private set; }

        public bool Active
        {
            get => RxFrequency >= 18 && RxFrequency <= 1300;
            set
            {
                RxFrequency = value ? 144 : 0;
                TxFrequency = value ? 144 : 0;
            }
        }

        public double RxFrequency
        {
            get => BitConverter.ToInt32(Data, 0) / 100000.0;
            set
            {
                BitConverter.GetBytes((int)Math.Round(value * 100000.0)).CopyTo(Data, 0);
            }
        }

        public double TxFrequency
        {
            get => BitConverter.ToInt32(Data, 4) / 100000.0;
            set
            {
                BitConverter.GetBytes((int)Math.Round(value * 100000.0)).CopyTo(Data, 4);
            }
        }

        public string RxTone
        {
            get => SubTone2String(BitConverter.ToUInt16(Data, 8));
            set => BitConverter.GetBytes((ushort)String2SubTone(value)).CopyTo(Data, 8);
        }

        public string TxTone
        {
            get => SubTone2String(BitConverter.ToUInt16(Data, 10));
            set => BitConverter.GetBytes((ushort)String2SubTone(value)).CopyTo(Data, 10);
        }

        public int TxPower
        {
            get => Data[12];
            set => Data[12] = (byte)value.Clamp(0, 254);
        }

        public string Groups
        {
            get => GroupW2String(BitConverter.ToUInt16(Data, 13));
            set => BitConverter.GetBytes((ushort)String2GroupW(value)).CopyTo(Data, 13);
        }

        public string Modulation
        {
            get => Modulation2String((Data[15] >> 1) & 3);
            set
            {
                Data[15] &= 0xf9;
                Data[15] |= (byte)(String2Modulation(value) << 1);
            }
        }

        public string Bandwidth
        {
            get => (Data[15] & 1) == 0 ? "Wide" : "Narrow";
            set
            {
                Data[15] &= 0xfe;
                if (value.ToLower().Trim().Equals("narrow"))
                    Data[15] |= 1;
            }
        }

        public string Name
        {
            get => Encoding.ASCII.GetString(Data, 20, 12).Trim('\0');
            set => Encoding.ASCII.GetBytes(value.PadRight(12, '\0')[..12]).CopyTo(Data, 20);
        }

        private Channel(int number)
        {
            BlockNumber = number + 1;
            ChannelNumber = number;
        }


    }
}
