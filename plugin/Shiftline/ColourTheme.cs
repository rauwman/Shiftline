using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Shiftline.Plugin
{
    /// <summary>One colour of a colour theme, stored as "#RRGGBB".</summary>
    public class ColourStop : INotifyPropertyChanged
    {
        private static readonly Regex HexColour = new Regex("^#[0-9A-Fa-f]{6}$");

        private string _hex = "#FFFFFF";

        public event PropertyChangedEventHandler PropertyChanged;

        public ColourStop()
        {
        }

        public ColourStop(string hex)
        {
            string normalised = Normalise(hex);
            if (normalised != null)
            {
                _hex = normalised;
            }
        }

        public string Hex
        {
            get { return _hex; }
            set
            {
                // Text that is not a #RRGGBB colour is ignored; the last valid colour stays.
                string normalised = Normalise(value);
                if (normalised == null || normalised == _hex)
                {
                    return;
                }

                _hex = normalised;
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null)
                {
                    handler(this, new PropertyChangedEventArgs("Hex"));
                }
            }
        }

        /// <summary>Returns the colour in upper case, or null when the text is not "#RRGGBB".</summary>
        public static string Normalise(string text)
        {
            if (text == null || !HexColour.IsMatch(text))
            {
                return null;
            }

            return text.ToUpperInvariant();
        }
    }

    /// <summary>
    /// A named list of 2 to 5 colours. A theme is picked for the rev bar and for the idle animation;
    /// the LED script samples the colours from first to last.
    /// </summary>
    public class ColourTheme : INotifyPropertyChanged
    {
        public const int MinStops = 2;
        public const int MaxStops = 5;

        private string _name = "New theme";

        public event PropertyChangedEventHandler PropertyChanged;

        public ColourTheme()
        {
            Id = Guid.NewGuid().ToString("N");
            Stops = new ObservableCollection<ColourStop>();
        }

        public ColourTheme(string id, string name, params string[] colours) : this()
        {
            Id = id;
            _name = name;
            foreach (string colour in colours)
            {
                Stops.Add(new ColourStop(colour));
            }
        }

        /// <summary>Stable identifier; renaming a theme does not break the selection that points to it.</summary>
        public string Id { get; set; }

        public string Name
        {
            get { return _name; }
            set
            {
                if (value == null || value == _name)
                {
                    return;
                }

                _name = value;
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null)
                {
                    handler(this, new PropertyChangedEventArgs("Name"));
                }
            }
        }

        public ObservableCollection<ColourStop> Stops { get; set; }

        /// <summary>The built-in themes cannot be deleted or changed; duplicate one to make your own version.</summary>
        [JsonIgnore]
        public bool IsBuiltIn
        {
            get { return Settings.IsBuiltInId(Id); }
        }

        [JsonIgnore]
        public bool IsEditable
        {
            get { return !IsBuiltIn; }
        }

        /// <summary>The colours as "#RRGGBB,#RRGGBB,..." (the format the LED script reads).</summary>
        public string ToColourList()
        {
            return string.Join(",", Stops.ToArray().Select(stop => stop.Hex).ToArray());
        }

        /// <summary>Copy with a new id and name.</summary>
        public ColourTheme Duplicate(string name)
        {
            ColourTheme copy = new ColourTheme();
            copy.Name = name;
            foreach (ColourStop stop in Stops)
            {
                copy.Stops.Add(new ColourStop(stop.Hex));
            }

            return copy;
        }
    }
}
