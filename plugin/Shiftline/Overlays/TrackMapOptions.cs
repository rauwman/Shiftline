using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Shiftline.Plugin
{
    /// <summary>The design choices of the Track Map, read from and written to OverlaySettings under its own key.</summary>
    internal sealed class TrackMapOptions
    {
        public const string Key = OverlayData.TrackMapKey;

        public const int MinLength = 100;
        public const int MaxLength = 1500;
        public const int LengthStep = 50;
        public const int DefaultLength = 850;
        public const int MinRing = 150;
        public const int MaxRing = 800;
        public const int RingStep = 25;
        public const int DefaultRing = 300;

        private static readonly string[] OnOff = { "1", "On", "0", "Off" };

        /// <summary>The lengths as the page offers them: 100 px to 1500 px in steps of 50 (pulling the window edge also lands on these).</summary>
        private static string[] LengthChoices()
        {
            List<string> pairs = new List<string>();
            for (int length = MinLength; length <= MaxLength; length += LengthStep)
            {
                string text = length.ToString(CultureInfo.InvariantCulture);
                pairs.Add(text);
                pairs.Add(text + " px");
            }

            return pairs.ToArray();
        }

        /// <summary>The circle sizes as the page offers them: 14 px to 32 px in steps of 1.</summary>
        private static string[] DotChoices()
        {
            List<string> pairs = new List<string>();
            for (int size = 14; size <= 32; size++)
            {
                string text = size.ToString(CultureInfo.InvariantCulture);
                pairs.Add(text);
                pairs.Add(text + " px");
            }

            return pairs.ToArray();
        }

        /// <summary>The ring sizes as the page offers them: 150 px to 800 px across in steps of 25.</summary>
        private static string[] RingChoices()
        {
            List<string> pairs = new List<string>();
            for (int size = MinRing; size <= MaxRing; size += RingStep)
            {
                string text = size.ToString(CultureInfo.InvariantCulture);
                pairs.Add(text);
                pairs.Add(text + " px");
            }

            return pairs.ToArray();
        }

        public static readonly StandingsChoice[] AllChoices =
        {
            new StandingsChoice("Map", "Shape", "Shape", "Line", "Line", "Line", "Ring", "Ring"),
            new StandingsChoice("Map", "Orientation", "Orientation", "Horizontal", "Horizontal", "Horizontal", "Vertical", "Vertical"),
            new StandingsChoice("Map", "Length", "Length (How long the line is. In edit mode you can also pull the edge of the window.)", DefaultLength.ToString(CultureInfo.InvariantCulture), LengthChoices()),
            new StandingsChoice("Map", "RingSize", "Ring size (How wide the ring is. In edit mode you can also pull the edge of the window.)", DefaultRing.ToString(CultureInfo.InvariantCulture), RingChoices()),
            new StandingsChoice("Map", "StartAt", "Start/finish on the ring", "Top", "Top", "Top", "Right", "Right", "Bottom", "Bottom", "Left", "Left"),
            new StandingsChoice("Map", "Direction", "Direction on the ring", "Clockwise", "Clockwise", "Clockwise", "Anticlockwise", "Anticlockwise"),
            new StandingsChoice("Map", "Mode", "Mode (On the ring, Centred on me turns the ring so that you stay at the top.)", "Whole", "Whole", "Whole lap", "Centred", "Centred on me"),
            new StandingsChoice("Map", "Range", "Range (How much of the lap is shown on each side of you. Only on the line in the mode Centred on me.)", "20",
                "10", "± 10 %", "15", "± 15 %", "20", "± 20 %", "25", "± 25 %", "30", "± 30 %", "40", "± 40 %"),
            new StandingsChoice("Cars", "Dot", "Circle size (Your own circle is drawn larger.)", "20", DotChoices()),
            new StandingsChoice("Cars", "Label", "Number in the circle", "Number",
                "None", "None", "Number", "Car number", "Position", "Position", "ClassPosition", "Class position",
                "Auto", "Auto (In races the position, in the class when there are several classes. Otherwise the car number.)"),
            new StandingsChoice("Cars", "Show", "Show", "All", "All", "All cars", "Class", "Only my class"),
            new StandingsChoice("Cars", "Pit", "Cars in the pit", "Dim", "Dim", "Dimmed and hollow", "Hide", "Hidden"),
            new StandingsChoice("Cars", "Fastest", "Fastest lap (A violet ring around the car with the fastest lap in your class. A lap ring goes first.)", "1", OnOff),
            new StandingsChoice("Cars", "LapRings", "Lap rings (Red ring: a lap ahead of you. Blue ring: a lap behind you. Races only.)", "1", OnOff),
            new StandingsChoice("Line", "LineStyle", "Style", "Road",
                "Line", "Thin line", "Road", "Road (Two parallel lines.)", "Strip", "Strip (A rounded strip the cars sit in.)"),
            new StandingsChoice("Line", "Thickness", "Thickness (Road: the distance between the two lines. Not used by the strip.)", "4", "1", "1 px", "2", "2 px", "3", "3 px", "4", "4 px", "6", "6 px", "8", "8 px"),
            new StandingsChoice("Line", "LineColour", "Line colour", "Grey",
                "Grey", "Grey", "White", "White", "LightBlue", "Light blue", "Orange", "Orange", "Green", "Green", "Red", "Red", "Yellow", "Yellow"),
            new StandingsChoice("Line", "StartLine", "Start/finish line", "Shiftline",
                "Off", "Off", "Shiftline", "Shiftline (animated)", "White", "White", "Grey", "Grey", "Black", "Black"),
            LookSettings.ForOverlay("Opacity", "Background opacity of the map", LookSettings.FollowGeneral),
            LookSettings.ForOverlay("Font", "Font", LookSettings.FollowGeneral),
            LookSettings.ForOverlay("Corners", "Boxes", LookSettings.FollowGeneral)
        };

        private Dictionary<string, string> _values;

        public static TrackMapOptions Load()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (StandingsChoice choice in AllChoices)
            {
                // a look setting that follows General gets the value from there
                values[choice.Key] = LookSettings.Resolve(choice.Key, Raw(choice));
            }

            return new TrackMapOptions { _values = values };
        }

        /// <summary>The same options with another length of the line or size of the ring (while the window edge is being pulled).</summary>
        public TrackMapOptions WithSize(int size)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(_values, StringComparer.Ordinal);
            values[Ring ? "RingSize" : "Length"] = ClampSize(size).ToString(CultureInfo.InvariantCulture);
            return new TrackMapOptions { _values = values };
        }

        /// <summary>What is chosen, as it is stored: for a look setting that follows General this is "Global".</summary>
        public static string Raw(StandingsChoice choice)
        {
            string value = OverlaySettings.GetOption(Key, choice.Key, choice.Fallback);
            return choice.Values.Contains(value) ? value : choice.Fallback;
        }

        public string Get(string choice)
        {
            string value;
            return _values.TryGetValue(choice, out value) ? value : string.Empty;
        }

        public int GetInt(string choice)
        {
            int value;
            return int.TryParse(Get(choice), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        public bool GetBool(string choice)
        {
            return Get(choice) == "1";
        }

        /// <summary>Length of the line in pixels, always one of the offered steps.</summary>
        public int Length
        {
            get { return ClampLength(GetInt("Length")); }
        }

        public bool Vertical
        {
            get { return Get("Orientation") == "Vertical"; }
        }

        public bool Centred
        {
            get { return Get("Mode") == "Centred"; }
        }

        public bool Ring
        {
            get { return Get("Shape") == "Ring"; }
        }

        /// <summary>Width of the ring in pixels (across the middle of the track), always one of the offered steps.</summary>
        public int RingSize
        {
            get { return ClampRing(GetInt("RingSize")); }
        }

        /// <summary>The size that pulling the window edge changes: the length of the line or the size of the ring.</summary>
        public int Size
        {
            get { return Ring ? RingSize : Length; }
        }

        public int ClampSize(int size)
        {
            return Ring ? ClampRing(size) : ClampLength(size);
        }

        public static int ClampRing(int size)
        {
            int rounded = (int)(Math.Round(size / (double)RingStep) * RingStep);
            return Math.Max(MinRing, Math.Min(MaxRing, rounded));
        }

        public static int ClampLength(int length)
        {
            int rounded = (int)(Math.Round(length / (double)LengthStep) * LengthStep);
            return Math.Max(MinLength, Math.Min(MaxLength, rounded));
        }

        // ---------------------------------------------------------------- writing

        public static void SetChoice(string choice, string value)
        {
            OverlaySettings.SetOption(Key, choice, value);
        }

        /// <summary>Stores the length of the line or the size of the ring; raises Changed, so the window snaps to the new size.</summary>
        public void SetSize(int size)
        {
            OverlaySettings.SetOption(Key, Ring ? "RingSize" : "Length", ClampSize(size).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Puts every choice of one group (such as "Cars") back to its default.</summary>
        public static void ResetGroup(string group)
        {
            foreach (StandingsChoice choice in AllChoices)
            {
                if (choice.Column == group)
                {
                    OverlaySettings.SetOption(Key, choice.Key, choice.Fallback);
                }
            }
        }

        public static void Reset()
        {
            OverlaySettings.ClearOptions(Key);
        }
    }
}
