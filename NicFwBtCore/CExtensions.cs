using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NicFwBtCore
{
    public static class CExtensions
    {
        public static int Clamp(this int value, int min, int max)
            => value < min ? min : value > max ? max : value;

        public static double Clamp(this double value, double min, double max)
            => value < min ? min : value > max ? max : value;

    }
}
