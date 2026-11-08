using System;
using System.Windows;
using System.Windows.Threading;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Owns the overlay windows. Everything here runs on SimHub's UI thread: a timer per overlay reads the latest data snapshot
    /// (OverlayData.Current) at the overlay's update rate. A switched-on overlay shows when there is data, and always in edit
    /// mode, so it can be placed without a session.
    /// </summary>
    public static class OverlayHost
    {
        private static Dispatcher _dispatcher;
        private static OverlayWindow _standingsWindow;
        private static StandingsOverlay _standingsView;
        private static DispatcherTimer _standingsTimer;
        private static bool _edit;

        /// <summary>Raised on the UI thread after edit mode was switched.</summary>
        public static event Action EditModeChanged;

        public static bool EditMode
        {
            get { return _edit; }
        }

        /// <summary>Called once from Init. Safe when there is no WPF application.</summary>
        public static void Start()
        {
            _dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (_dispatcher == null)
            {
                return;
            }

            _dispatcher.BeginInvoke(new Action(() =>
            {
                OverlaySettings.Changed += () => OnUi(Sync);
                _standingsTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher);
                _standingsTimer.Tick += (sender, args) => TickStandings();
                Sync();
            }));
        }

        /// <summary>Called from End: closes the windows.</summary>
        public static void Stop()
        {
            if (_dispatcher == null)
            {
                return;
            }

            try
            {
                _dispatcher.Invoke(new Action(() =>
                {
                    if (_standingsTimer != null)
                    {
                        _standingsTimer.Stop();
                    }

                    if (_standingsWindow != null)
                    {
                        _standingsWindow.Close();
                        _standingsWindow = null;
                    }
                }));
            }
            catch (Exception ex)
            {
                PluginLog.Info("Closing the overlays failed: " + ex.Message);
            }
        }

        public static void ToggleEditMode()
        {
            SetEditMode(!_edit);
        }

        public static void SetEditMode(bool edit)
        {
            OnUi(() =>
            {
                if (_edit == edit)
                {
                    return;
                }

                _edit = edit;
                PluginLog.Info("Overlay edit mode " + (edit ? "on" : "off"));
                if (_standingsWindow != null)
                {
                    _standingsWindow.SetEditMode(edit);
                }

                Sync();
                Action handler = EditModeChanged;
                if (handler != null)
                {
                    handler();
                }
            });
        }

        /// <summary>Forgets all saved positions and puts the overlays back to their default places.</summary>
        public static void ResetPositions()
        {
            OverlaySettings.ResetPlacements();
            OnUi(() =>
            {
                if (_standingsWindow != null && _standingsWindow.IsVisible)
                {
                    _standingsWindow.RestorePlacement();
                }
            });
        }

        /// <summary>Applies the settings: timer rate, and whether the window should exist.</summary>
        private static void Sync()
        {
            if (_standingsTimer == null)
            {
                return;
            }

            bool enabled = OverlaySettings.IsEnabled(OverlayData.StandingsKey);
            if (!enabled)
            {
                _standingsTimer.Stop();
                if (_standingsWindow != null)
                {
                    _standingsWindow.Hide();
                }

                return;
            }

            if (_standingsView != null)
            {
                _standingsView.ApplyOptions(StandingsOptions.Load());
                if (_standingsWindow != null)
                {
                    _standingsWindow.SetFixedSize(_standingsView.DesignSize);
                }
            }

            _standingsTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / OverlaySettings.RateFor(OverlayData.StandingsKey));
            _standingsTimer.Start();
            TickStandings();
        }

        private static void TickStandings()
        {
            try
            {
                bool enabled = OverlaySettings.IsEnabled(OverlayData.StandingsKey);
                StandingsSnapshot snapshot = OverlayData.Current;
                bool inGame = snapshot.HasData && (!OverlaySettings.OnlyWhenGameInFront || GameFocus.IsGameInFront("IRacing"));
                bool show = enabled && (_edit || inGame);
                if (!show)
                {
                    if (_standingsWindow != null && _standingsWindow.IsVisible)
                    {
                        _standingsWindow.Hide();
                    }

                    return;
                }

                if (_standingsWindow == null)
                {
                    _standingsView = new StandingsOverlay();
                    _standingsWindow = new OverlayWindow(OverlayData.StandingsKey, "Standings", _standingsView, _standingsView.DesignSize);
                    _standingsWindow.SetEditMode(_edit);
                    _standingsWindow.SetFixedSize(_standingsView.DesignSize);
                    _standingsWindow.HeightDragging += StandingsHeightDragging;
                    _standingsWindow.HeightDragged += StandingsHeightDragged;
                    _standingsWindow.WidthDragging += StandingsWidthDragging;
                    _standingsWindow.WidthDragged += StandingsWidthDragged;
                }

                // In edit mode without a session the overlay shows invented data, so it can be sized and placed.
                _standingsView.Update(snapshot.HasData ? snapshot : StandingsSample.Create(true));
                if (!_standingsWindow.IsVisible)
                {
                    _standingsWindow.Show();
                }
            }
            catch (Exception ex)
            {
                _standingsTimer.Stop();
                PluginLog.Info("Standings overlay stopped: " + ex);
            }
        }

        private static int _draggedRows;

        /// <summary>Pulling the bottom edge shows more or fewer rows right away; they are saved when the edge is released.</summary>
        private static void StandingsHeightDragging(double height)
        {
            int rows = _standingsView.RowsForHeight(height);
            if (rows == _draggedRows)
            {
                return;
            }

            _draggedRows = rows;
            _standingsView.ApplyOptions(StandingsOptions.Load().WithRows(rows));
            RefreshStandingsNow();
        }

        private static double _scaleAtDragStart;
        private static double _draggedScale;

        /// <summary>Pulling the right edge makes everything larger or smaller; the size is saved when the edge is released.</summary>
        private static void StandingsWidthDragging(double factor)
        {
            if (_scaleAtDragStart <= 0)
            {
                _scaleAtDragStart = StandingsOptions.Load().Scale;
            }

            double scale = Math.Max(0.5, Math.Min(3, _scaleAtDragStart * factor));
            if (Math.Abs(scale - _draggedScale) < 0.01)
            {
                return;
            }

            _draggedScale = scale;
            _standingsView.ApplyOptions(StandingsOptions.Load().WithScale(scale));
            RefreshStandingsNow();
        }

        private static void StandingsWidthDragged()
        {
            double scale = _draggedScale > 0 ? _draggedScale : StandingsOptions.Load().Scale;
            _scaleAtDragStart = 0;
            _draggedScale = 0;
            StandingsOptions.SetScale(scale);         // raises Changed: the window snaps to the saved (rounded) size
            _standingsWindow.SetFixedSize(_standingsView.DesignSize);
        }

        private static void RefreshStandingsNow()
        {
            StandingsSnapshot snapshot = OverlayData.Current;
            _standingsView.Update(snapshot.HasData ? snapshot : StandingsSample.Create(true));
            _standingsWindow.SetFixedSize(_standingsView.DesignSize);
        }

        private static void StandingsHeightDragged(double height)
        {
            int rows = _standingsView.RowsForHeight(height);
            _draggedRows = 0;
            StandingsOptions.SetRows(rows);           // raises Changed: the window snaps to the exact size of these rows
            _standingsWindow.SetFixedSize(_standingsView.DesignSize);
        }

        private static void OnUi(Action action)
        {
            if (_dispatcher == null)
            {
                return;
            }

            if (_dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _dispatcher.BeginInvoke(action);
            }
        }
    }
}
