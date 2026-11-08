using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shiftline.Plugin
{
    /// <summary>
    /// First, plain version of the standings overlay, to test the window: position, number, name and last lap of the leaders,
    /// and the cars around the player when the player is further back. The text blocks are made once and only their text changes.
    /// The content scales with the window size.
    /// </summary>
    internal sealed class StandingsOverlay : Border
    {
        public const int Rows = 12;
        private const int AroundPlayer = 3;          // rows kept for the player and the cars right before and after
        private const double DesignWidth = 300;
        private const double RowHeight = 22;

        private static readonly Brush TextBrush = Frozen(Color.FromArgb(0xFF, 0xE1, 0xE5, 0xEB));
        private static readonly Brush MutedBrush = Frozen(Color.FromArgb(0xFF, 0x8A, 0x93, 0xA0));
        private static readonly Brush PlayerBrush = Frozen(Color.FromArgb(0xFF, 0x3F, 0xA9, 0xF5));
        private static readonly Brush PanelBrush = Frozen(Color.FromArgb(0xCC, 0x10, 0x14, 0x1C));

        private readonly TextBlock[,] _cells = new TextBlock[Rows, 4];

        public StandingsOverlay()
        {
            Background = PanelBrush;
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(8, 6, 8, 6);

            Grid grid = new Grid { Width = DesignWidth };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            for (int r = 0; r < Rows; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
                for (int c = 0; c < 4; c++)
                {
                    TextBlock cell = new TextBlock
                    {
                        Foreground = c == 0 || c == 1 ? MutedBrush : TextBrush,
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextAlignment = c == 3 ? TextAlignment.Right : TextAlignment.Left
                    };
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    grid.Children.Add(cell);
                    _cells[r, c] = cell;
                }
            }

            Child = new Viewbox { Stretch = Stretch.Uniform, Child = grid };
        }

        /// <summary>The window size that fits the design.</summary>
        public static Size DefaultSize
        {
            get { return new Size(DesignWidth + 16, Rows * RowHeight + 12); }
        }

        public void Update(StandingsSnapshot snapshot, bool edit)
        {
            if (!snapshot.HasData)
            {
                for (int r = 0; r < Rows; r++)
                {
                    SetRow(r, null, false);
                }

                Set(_cells[0, 2], edit ? "Standings (no session)" : string.Empty, TextBrush);
                return;
            }

            List<StandingsRow> shown = Choose(snapshot.Rows);
            for (int r = 0; r < Rows; r++)
            {
                SetRow(r, r < shown.Count ? shown[r] : null, true);
            }
        }

        /// <summary>The leaders; when the player is not among them, the last rows show the player and the cars around.</summary>
        private static List<StandingsRow> Choose(StandingsRow[] rows)
        {
            List<StandingsRow> list = new List<StandingsRow>(Rows);
            int player = Array.FindIndex(rows, row => row.IsPlayer);
            if (player < Rows || rows.Length <= Rows)
            {
                for (int i = 0; i < rows.Length && i < Rows; i++)
                {
                    list.Add(rows[i]);
                }

                return list;
            }

            for (int i = 0; i < Rows - AroundPlayer; i++)
            {
                list.Add(rows[i]);
            }

            int start = Math.Min(player - 1, rows.Length - AroundPlayer);
            for (int i = start; i < start + AroundPlayer && i < rows.Length; i++)
            {
                list.Add(rows[i]);
            }

            return list;
        }

        private void SetRow(int r, StandingsRow row, bool hasData)
        {
            if (row == null)
            {
                for (int c = 0; c < 4; c++)
                {
                    Set(_cells[r, c], string.Empty, TextBrush);
                }

                return;
            }

            Brush name = row.IsPlayer ? PlayerBrush : TextBrush;
            Set(_cells[r, 0], row.Position > 0 ? row.Position.ToString(CultureInfo.InvariantCulture) : "-", MutedBrush);
            Set(_cells[r, 1], row.Number ?? string.Empty, MutedBrush);
            Set(_cells[r, 2], row.Name ?? string.Empty, name);
            Set(_cells[r, 3], Lap(row.LastLap), name);
        }

        private static void Set(TextBlock cell, string text, Brush brush)
        {
            if (!string.Equals(cell.Text, text, StringComparison.Ordinal))
            {
                cell.Text = text;
            }

            if (!ReferenceEquals(cell.Foreground, brush))
            {
                cell.Foreground = brush;
            }
        }

        private static string Lap(double seconds)
        {
            if (seconds <= 0)
            {
                return "-";
            }

            int minutes = (int)(seconds / 60);
            return minutes + ":" + (seconds - minutes * 60).ToString("00.000", CultureInfo.InvariantCulture);
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
