using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The country flags of the drivers. iRacing reports the flag of a driver as a name (FlairName, for example "Spain"); this finds
    /// the picture for it. The pictures are the 4:3 flags of the open "flag-icons" collection (MIT licence, see
    /// Assets/Flags/LICENSE-flag-icons.txt), built into the plugin as small PNG files named after the ISO country code.
    /// </summary>
    internal static class FlagImages
    {
        private static readonly Uri Folder = new Uri("pack://application:,,,/Shiftline.Plugin;component/Assets/Flags/");
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>();
        private static readonly Dictionary<string, string> Codes;

        static FlagImages()
        {
            Codes = BuildCodes();       // after the name list below: static fields are set up in the order they are written
        }

        /// <summary>The picture for a flair name, or null when there is none (no flair, or a name that is not a country).</summary>
        public static ImageSource For(string flair)
        {
            string code = CodeOf(flair);
            if (code == null)
            {
                return null;
            }

            lock (Cache)
            {
                ImageSource image;
                if (!Cache.TryGetValue(code, out image))
                {
                    image = Load(code);
                    Cache[code] = image;
                }

                return image;
            }
        }

        /// <summary>The country code of a flair name ("es" for "Spain"), or null when the name is not known.</summary>
        public static string CodeOf(string flair)
        {
            string code;
            return Codes.TryGetValue(Key(flair), out code) ? code : null;
        }

        /// <summary>Only the plain letters, in lower case: so accents, spaces and a wrongly decoded umlaut do not matter.</summary>
        private static string Key(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            StringBuilder key = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                char lower = char.ToLowerInvariant(c);
                if (lower >= 'a' && lower <= 'z')
                {
                    key.Append(lower);
                }
            }

            return key.ToString();
        }

        private static ImageSource Load(string code)
        {
            try
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(Folder, code + ".png");
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Dictionary<string, string> BuildCodes()
        {
            Dictionary<string, string> codes = new Dictionary<string, string>();
            for (int i = 0; i + 1 < Names.Length; i += 2)
            {
                codes[Names[i]] = Names[i + 1];
            }

            return codes;
        }

        // Country names (English, letters only) and their ISO codes; made from the country list of flag-icons plus common other spellings.
        private static readonly string[] Names =
        {
            "afghanistan", "af", "alandislands", "ax", "albania", "al",
            "algeria", "dz", "americansamoa", "as", "andorra", "ad",
            "angola", "ao", "anguilla", "ai", "antarctica", "aq",
            "antigua", "ag", "antiguaandbarbuda", "ag", "argentina", "ar",
            "armenia", "am", "aruba", "aw", "australia", "au",
            "austria", "at", "azerbaijan", "az", "bahamas", "bs",
            "bahrain", "bh", "bangladesh", "bd", "barbados", "bb",
            "belarus", "by", "belgium", "be", "belize", "bz",
            "benin", "bj", "bermuda", "bm", "bhutan", "bt",
            "bolivia", "bo", "bonairesinteustatiusandsaba", "bq", "bosnia", "ba",
            "bosniaandherzegovina", "ba", "bosniaherzegovina", "ba", "botswana", "bw",
            "bouvetisland", "bv", "brazil", "br", "britain", "gb",
            "britishindianoceanterritory", "io", "brunei", "bn", "bruneidarussalam", "bn",
            "bulgaria", "bg", "burkinafaso", "bf", "burma", "mm",
            "burundi", "bi", "caboverde", "cv", "cambodia", "kh",
            "cameroon", "cm", "canada", "ca", "capeverde", "cv",
            "caymanislands", "ky", "centralafricanrepublic", "cf", "chad", "td",
            "chile", "cl", "china", "cn", "christmasisland", "cx",
            "cocoskeelingislands", "cc", "colombia", "co", "comoros", "km",
            "congo", "cg", "congodemocraticrepublicofthe", "cd", "cookislands", "ck",
            "costarica", "cr", "cotedivoire", "ci", "croatia", "hr",
            "ctedivoire", "ci", "cuba", "cu", "curaao", "cw",
            "curacao", "cw", "cyprus", "cy", "czechia", "cz",
            "czechrepublic", "cz", "democraticrepublicofthecongo", "cd", "denmark", "dk",
            "djibouti", "dj", "dominica", "dm", "dominicanrepublic", "do",
            "drcongo", "cd", "easttimor", "tl", "ecuador", "ec",
            "egypt", "eg", "elsalvador", "sv", "england", "gb-eng",
            "equatorialguinea", "gq", "eritrea", "er", "estonia", "ee",
            "eswatini", "sz", "ethiopia", "et", "falklandislands", "fk",
            "falklands", "fk", "faroeislands", "fo", "federatedstatesofmicronesia", "fm",
            "fiji", "fj", "finland", "fi", "france", "fr",
            "frenchguiana", "gf", "frenchpolynesia", "pf", "frenchsouthernterritories", "tf",
            "gabon", "ga", "gambia", "gm", "georgia", "ge",
            "germany", "de", "ghana", "gh", "gibraltar", "gi",
            "greatbritain", "gb", "greece", "gr", "greenland", "gl",
            "grenada", "gd", "guadeloupe", "gp", "guam", "gu",
            "guatemala", "gt", "guernsey", "gg", "guinea", "gn",
            "guineabissau", "gw", "guyana", "gy", "haiti", "ht",
            "heardislandandmcdonaldislands", "hm", "holland", "nl", "holysee", "va",
            "honduras", "hn", "hongkong", "hk", "hungary", "hu",
            "iceland", "is", "india", "in", "indonesia", "id",
            "iran", "ir", "iraq", "iq", "ireland", "ie",
            "isleofman", "im", "israel", "il", "italy", "it",
            "ivorycoast", "ci", "jamaica", "jm", "japan", "jp",
            "jersey", "je", "jordan", "jo", "kazakhstan", "kz",
            "kenya", "ke", "kiribati", "ki", "korea", "kr",
            "korearepublicof", "kr", "kosovo", "xk", "kuwait", "kw",
            "kyrgyzstan", "kg", "laos", "la", "latvia", "lv",
            "lebanon", "lb", "lesotho", "ls", "liberia", "lr",
            "libya", "ly", "liechtenstein", "li", "lithuania", "lt",
            "luxembourg", "lu", "macao", "mo", "macau", "mo",
            "macedonia", "mk", "madagascar", "mg", "malawi", "mw",
            "malaysia", "my", "maldives", "mv", "mali", "ml",
            "malta", "mt", "marshallislands", "mh", "martinique", "mq",
            "mauritania", "mr", "mauritius", "mu", "mayotte", "yt",
            "mexico", "mx", "micronesia", "fm", "moldova", "md",
            "monaco", "mc", "mongolia", "mn", "montenegro", "me",
            "montserrat", "ms", "morocco", "ma", "mozambique", "mz",
            "myanmar", "mm", "namibia", "na", "nauru", "nr",
            "nepal", "np", "netherlands", "nl", "newcaledonia", "nc",
            "newzealand", "nz", "nicaragua", "ni", "niger", "ne",
            "nigeria", "ng", "niue", "nu", "norfolkisland", "nf",
            "northernireland", "gb-nir", "northernmarianaislands", "mp", "northkorea", "kp",
            "northmacedonia", "mk", "norway", "no", "oman", "om",
            "pakistan", "pk", "palau", "pw", "palestine", "ps",
            "panama", "pa", "papuanewguinea", "pg", "paraguay", "py",
            "peru", "pe", "philippines", "ph", "pitcairn", "pn",
            "poland", "pl", "portugal", "pt", "puertorico", "pr",
            "qatar", "qa", "republicofkorea", "kr", "republicofthecongo", "cg",
            "reunion", "re", "romania", "ro", "runion", "re",
            "russia", "ru", "russianfederation", "ru", "rwanda", "rw",
            "saintbarthlemy", "bl", "sainthelenaascensionandtristandacunha", "sh", "saintkittsandnevis", "kn",
            "saintlucia", "lc", "saintmartin", "mf", "saintpierreandmiquelon", "pm",
            "saintvincentandthegrenadines", "vc", "samoa", "ws", "sanmarino", "sm",
            "saotomeandprincipe", "st", "saudiarabia", "sa", "scotland", "gb-sct",
            "senegal", "sn", "serbia", "rs", "seychelles", "sc",
            "sierraleone", "sl", "singapore", "sg", "sintmaarten", "sx",
            "slovakia", "sk", "slovenia", "si", "solomonislands", "sb",
            "somalia", "so", "southafrica", "za", "southgeorgiaandthesouthsandwichislands", "gs",
            "southkorea", "kr", "southsudan", "ss", "spain", "es",
            "srilanka", "lk", "stateofpalestine", "ps", "sudan", "sd",
            "suriname", "sr", "svalbardandjanmayen", "sj", "swaziland", "sz",
            "sweden", "se", "switzerland", "ch", "syria", "sy",
            "taiwan", "tw", "tajikistan", "tj", "tanzania", "tz",
            "thailand", "th", "thenetherlands", "nl", "timorleste", "tl",
            "togo", "tg", "tokelau", "tk", "tonga", "to",
            "trinidad", "tt", "trinidadandtobago", "tt", "trkiye", "tr",
            "tunisia", "tn", "turkey", "tr", "turkiye", "tr",
            "turkmenistan", "tm", "turksandcaicosislands", "tc", "tuvalu", "tv",
            "uae", "ae", "uganda", "ug", "uk", "gb",
            "ukraine", "ua", "unitedarabemirates", "ae", "unitedkingdom", "gb",
            "unitedstates", "us", "unitedstatesminoroutlyingislands", "um", "unitedstatesofamerica", "us",
            "uruguay", "uy", "usa", "us", "uzbekistan", "uz",
            "vanuatu", "vu", "vatican", "va", "venezuela", "ve",
            "vietnam", "vn", "virginislandsbritish", "vg", "virginislandsus", "vi",
            "wales", "gb-wls", "wallisandfutuna", "wf", "westernsahara", "eh",
            "yemen", "ye", "zambia", "zm", "zimbabwe", "zw"
        };
    }
}
