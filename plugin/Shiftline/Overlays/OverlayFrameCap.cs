using System.Diagnostics;
using System.Windows.Media.Animation;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The overlay frame rate (setting in General) for the WPF overlays: the most frames per second they draw, 0 = as often as
    /// WPF wants. Every drawn frame costs the game a little, above all with GPU priority realtime. Animations get it as their
    /// desired frame rate when they start (looping animations keep the rate they started with until the overlay is built
    /// again), per-frame handlers skip the frames in between.
    /// </summary>
    internal static class OverlayFrameCap
    {
        public static int Cap
        {
            get { return OverlaySettings.FrameRate; }
        }

        /// <summary>Gives an animation the cap (call before it starts); returns it.</summary>
        public static T Limit<T>(T timeline) where T : Timeline
        {
            int cap = Cap;
            if (cap > 0)
            {
                Timeline.SetDesiredFrameRate(timeline, cap);
            }

            return timeline;
        }

        /// <summary>True when a per-frame handler should skip this frame; <paramref name="last"/> keeps the last drawn time.</summary>
        public static bool Skip(ref long last)
        {
            int cap = Cap;
            if (cap <= 0)
            {
                return false;
            }

            long now = Stopwatch.GetTimestamp();
            if (last != 0 && now - last < Stopwatch.Frequency / cap - Stopwatch.Frequency / 500)
            {
                return true;
            }

            last = now;
            return false;
        }
    }
}
