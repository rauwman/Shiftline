using System;
using System.Collections.Generic;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Three-letter make codes as used in TV graphics, taken from the car's screen name (for example "Porsche 911 GT3 R" gives POR).
    /// Plain text only: no logos. Unknown makes fall back to the first three letters of the name.
    /// </summary>
    internal static class CarMakes
    {
        private static readonly Dictionary<string, string> Codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "acura", "ACU" }, { "alfa", "ALF" }, { "aston", "AST" }, { "audi", "AUD" }, { "bmw", "BMW" },
            { "cadillac", "CAD" }, { "chevrolet", "CHE" }, { "chevy", "CHE" }, { "dallara", "DAL" }, { "ferrari", "FER" },
            { "ford", "FOR" }, { "honda", "HON" }, { "hyundai", "HYU" }, { "kia", "KIA" }, { "lamborghini", "LAM" },
            { "lexus", "LEX" }, { "ligier", "LIG" }, { "lotus", "LOT" }, { "mazda", "MAZ" }, { "mclaren", "MCL" },
            { "mercedes", "MER" }, { "mercedes-amg", "MER" }, { "mini", "MIN" }, { "nissan", "NIS" }, { "oreca", "ORE" },
            { "pontiac", "PON" }, { "porsche", "POR" }, { "radical", "RAD" }, { "renault", "REN" }, { "riley", "RIL" },
            { "subaru", "SUB" }, { "toyota", "TOY" }, { "volkswagen", "VWG" }, { "vw", "VWG" }, { "williams", "WIL" },
            { "honda-acura", "ACU" }, { "skip", "SKB" }, { "solstice", "PON" }, { "superformula", "SFL" }
        };

        private static readonly char[] Separators = { ' ', '-', '_', '/', '(', ')' };

        public static string CodeFor(string carScreenName)
        {
            if (string.IsNullOrWhiteSpace(carScreenName))
            {
                return string.Empty;
            }

            string[] words = carScreenName.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (string word in words)
            {
                string code;
                if (Codes.TryGetValue(word, out code))
                {
                    return code;
                }
            }

            string first = words.Length > 0 ? words[0] : carScreenName.Trim();
            return (first.Length <= 3 ? first : first.Substring(0, 3)).ToUpperInvariant();
        }
    }
}
