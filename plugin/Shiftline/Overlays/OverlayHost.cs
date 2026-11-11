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
        private static OverlayWindow _battleWindow;
        private static BattleBarOverlay _battleView;
        private static DispatcherTimer _battleTimer;
        private static StandingsFeatures _battleFeatures = StandingsFeatures.All;
        private static OverlayWindow _relativesWindow;
        private static StandingsOverlay _relativesView;
        private static DispatcherTimer _relativesTimer;
        private static StandingsFeatures _relativesFeatures = StandingsFeatures.All;
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
                _battleTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher);
                _battleTimer.Tick += (sender, args) => TickBattleBar();
                _relativesTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher);
                _relativesTimer.Tick += (sender, args) => TickRelatives();
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

                    if (_battleTimer != null)
                    {
                        _battleTimer.Stop();
                    }

                    if (_battleWindow != null)
                    {
                        _battleWindow.Close();
                        _battleWindow = null;
                    }

                    if (_relativesTimer != null)
                    {
                        _relativesTimer.Stop();
                    }

                    if (_relativesWindow != null)
                    {
                        _relativesWindow.Close();
                        _relativesWindow = null;
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

                if (_battleWindow != null)
                {
                    _battleWindow.SetEditMode(edit);
                }

                if (_relativesWindow != null)
                {
                    _relativesWindow.SetEditMode(edit);
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

                if (_battleWindow != null && _battleWindow.IsVisible)
                {
                    _battleWindow.RestorePlacement();
                }

                if (_relativesWindow != null && _relativesWindow.IsVisible)
                {
                    _relativesWindow.RestorePlacement();
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

            SyncBattleBar();
            SyncRelatives();

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
                _standingsView.ApplyOptions(StandingsOptions.Load().ForFeatures(_standingsFeatures));
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
                bool inGame = snapshot.HasData
                    && (!OverlaySettings.OnlyWhenGameInFront || GameFocus.IsGameInFront(snapshot.Game))
                    && (OverlaySettings.ShowInReplays || !snapshot.IsReplay)
                    && (OverlaySettings.ShowInGarage || !snapshot.IsInGarage);
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
                StandingsSnapshot shown = snapshot.HasData
                    ? snapshot
                    : StandingsSample.Create(true).As(OverlayData.PreviewFeatures(), OverlayData.SelectedGame);
                if (shown.Features != _standingsFeatures)
                {
                    // Another game: hide the columns it cannot fill (or show them again).
                    _standingsFeatures = shown.Features;
                    _standingsView.ApplyOptions(StandingsOptions.Load().ForFeatures(_standingsFeatures));
                    _standingsWindow.SetFixedSize(_standingsView.DesignSize);
                }

                _standingsView.Update(shown);
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
        private static StandingsFeatures _standingsFeatures = StandingsFeatures.All;

        /// <summary>Pulling the bottom edge shows more or fewer rows right away; they are saved when the edge is released.</summary>
        private static void StandingsHeightDragging(double height)
        {
            int rows = _standingsView.RowsForHeight(height);
            if (rows == _draggedRows)
            {
                return;
            }

            _draggedRows = rows;
            _standingsView.ApplyOptions(StandingsOptions.Load().ForFeatures(_standingsFeatures).WithRows(rows));
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
            _standingsView.ApplyOptions(StandingsOptions.Load().ForFeatures(_standingsFeatures).WithScale(scale));
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
            _standingsView.Update(snapshot.HasData ? snapshot : StandingsSample.Create(true).As(OverlayData.PreviewFeatures(), OverlayData.SelectedGame));
            _standingsWindow.SetFixedSize(_standingsView.DesignSize);
        }

        private static void StandingsHeightDragged(double height)
        {
            int rows = _standingsView.RowsForHeight(height);
            _draggedRows = 0;
            StandingsOptions.SetRows(rows);           // raises Changed: the window snaps to the exact size of these rows
            _standingsWindow.SetFixedSize(_standingsView.DesignSize);
        }

        // ---------------------------------------------------------------- Battle Bar

        private static void SyncBattleBar()
        {
            if (!OverlaySettings.IsEnabled(OverlayData.BattleBarKey))
            {
                _battleTimer.Stop();
                if (_battleWindow != null)
                {
                    _battleWindow.Hide();
                }

                return;
            }

            if (_battleView != null)
            {
                _battleView.ApplyOptions(BattleBarOptions.Load().ForFeatures(_battleFeatures));
                if (_battleWindow != null)
                {
                    _battleWindow.SetFixedSize(_battleView.DesignSize);
                }
            }

            _battleTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / OverlaySettings.RateFor(OverlayData.BattleBarKey));
            _battleTimer.Start();
            TickBattleBar();
        }

        private static StandingsSnapshot BattleSnapshot()
        {
            StandingsSnapshot snapshot = OverlayData.Current;
            return snapshot.HasData ? snapshot : StandingsSample.Create(true).As(OverlayData.PreviewFeatures(), OverlayData.SelectedGame);
        }

        private static void TickBattleBar()
        {
            try
            {
                bool enabled = OverlaySettings.IsEnabled(OverlayData.BattleBarKey);
                StandingsSnapshot snapshot = OverlayData.Current;
                bool inGame = snapshot.HasData
                    && (!OverlaySettings.OnlyWhenGameInFront || GameFocus.IsGameInFront(snapshot.Game))
                    && (OverlaySettings.ShowInReplays || !snapshot.IsReplay)
                    && (OverlaySettings.ShowInGarage || !snapshot.IsInGarage)
                    && BattleBarOverlay.ShowsIn(BattleBarOptions.Load(), snapshot);
                if (!enabled || !(_edit || inGame))
                {
                    if (_battleWindow != null && _battleWindow.IsVisible)
                    {
                        _battleWindow.Hide();
                    }

                    return;
                }

                if (_battleWindow == null)
                {
                    _battleView = new BattleBarOverlay();
                    _battleWindow = new OverlayWindow(OverlayData.BattleBarKey, "Battle Bar", _battleView, _battleView.DesignSize);
                    _battleWindow.SetEditMode(_edit);
                    _battleWindow.SetFixedSize(_battleView.DesignSize);
                    _battleWindow.WidthDragging += factor => BattleScaleDragging(factor);
                    _battleWindow.WidthDragged += BattleScaleDragged;
                    _battleWindow.HeightDragging += height =>
                    {
                        if (_battleHeightAtStart <= 0)
                        {
                            _battleHeightAtStart = _battleWindow.Height;
                        }

                        BattleScaleDragging(height / Math.Max(1, _battleHeightAtStart));
                    };
                    _battleWindow.HeightDragged += height => BattleScaleDragged();
                }

                StandingsSnapshot shown = BattleSnapshot();
                if (shown.Features != _battleFeatures)
                {
                    _battleFeatures = shown.Features;
                    _battleView.ApplyOptions(BattleBarOptions.Load().ForFeatures(_battleFeatures));
                    _battleWindow.SetFixedSize(_battleView.DesignSize);
                }

                _battleView.Update(shown);
                if (!_battleWindow.IsVisible)
                {
                    _battleWindow.Show();
                }
            }
            catch (Exception ex)
            {
                _battleTimer.Stop();
                PluginLog.Info("Battle Bar stopped: " + ex);
            }
        }

        private static double _battleScaleAtStart;
        private static double _battleHeightAtStart;
        private static double _battleScale;

        /// <summary>Pulling the right or the bottom edge makes the bar larger or smaller; saved when the edge is released.</summary>
        private static void BattleScaleDragging(double factor)
        {
            if (_battleScaleAtStart <= 0)
            {
                _battleScaleAtStart = BattleBarOptions.Load().Scale;
            }

            double scale = Math.Max(0.5, Math.Min(3, _battleScaleAtStart * factor));
            if (Math.Abs(scale - _battleScale) < 0.01)
            {
                return;
            }

            _battleScale = scale;
            _battleView.ApplyOptions(BattleBarOptions.Load().ForFeatures(_battleFeatures).WithScale(scale));
            _battleView.Update(BattleSnapshot());
            _battleWindow.SetFixedSize(_battleView.DesignSize);
        }

        private static void BattleScaleDragged()
        {
            double scale = _battleScale > 0 ? _battleScale : BattleBarOptions.Load().Scale;
            _battleScaleAtStart = 0;
            _battleHeightAtStart = 0;
            _battleScale = 0;
            BattleBarOptions.SetScale(scale);         // raises Changed: the window snaps to the saved (rounded) size
            _battleWindow.SetFixedSize(_battleView.DesignSize);
        }

        // ---------------------------------------------------------------- Relatives

        private static void SyncRelatives()
        {
            if (!OverlaySettings.IsEnabled(OverlayData.RelativesKey))
            {
                _relativesTimer.Stop();
                if (_relativesWindow != null)
                {
                    _relativesWindow.Hide();
                }

                return;
            }

            if (_relativesView != null)
            {
                _relativesView.ApplyOptions(StandingsOptions.Load(OverlayData.RelativesKey).ForFeatures(_relativesFeatures));
                if (_relativesWindow != null)
                {
                    _relativesWindow.SetFixedSize(_relativesView.DesignSize);
                }
            }

            _relativesTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / OverlaySettings.RateFor(OverlayData.RelativesKey));
            _relativesTimer.Start();
            TickRelatives();
        }

        /// <summary>The data, or invented cars spread around the lap in edit mode without a session.</summary>
        private static StandingsSnapshot RelativesSnapshot()
        {
            StandingsSnapshot snapshot = OverlayData.Current;
            return snapshot.HasData ? snapshot : StandingsSample.OnTrack(StandingsSample.Create(true)).As(OverlayData.PreviewFeatures(), OverlayData.SelectedGame);
        }

        private static void TickRelatives()
        {
            try
            {
                bool enabled = OverlaySettings.IsEnabled(OverlayData.RelativesKey);
                StandingsSnapshot snapshot = OverlayData.Current;
                bool inGame = snapshot.HasData
                    && (!OverlaySettings.OnlyWhenGameInFront || GameFocus.IsGameInFront(snapshot.Game))
                    && (OverlaySettings.ShowInReplays || !snapshot.IsReplay)
                    && (OverlaySettings.ShowInGarage || !snapshot.IsInGarage);
                if (!enabled || !(_edit || inGame))
                {
                    if (_relativesWindow != null && _relativesWindow.IsVisible)
                    {
                        _relativesWindow.Hide();
                    }

                    return;
                }

                if (_relativesWindow == null)
                {
                    _relativesView = new StandingsOverlay(false, OverlayData.RelativesKey);
                    _relativesWindow = new OverlayWindow(OverlayData.RelativesKey, "Relatives", _relativesView, _relativesView.DesignSize);
                    _relativesWindow.SetEditMode(_edit);
                    _relativesWindow.SetFixedSize(_relativesView.DesignSize);
                    // the number of rows follows from the cars in front and behind: both edges change the size
                    _relativesWindow.WidthDragging += factor => RelativesScaleDragging(factor);
                    _relativesWindow.WidthDragged += RelativesScaleDragged;
                    _relativesWindow.HeightDragging += height =>
                    {
                        if (_relativesHeightAtStart <= 0)
                        {
                            _relativesHeightAtStart = _relativesWindow.Height;
                        }

                        RelativesScaleDragging(height / Math.Max(1, _relativesHeightAtStart));
                    };
                    _relativesWindow.HeightDragged += height => RelativesScaleDragged();
                }

                StandingsSnapshot shown = RelativesSnapshot();
                if (shown.Features != _relativesFeatures)
                {
                    _relativesFeatures = shown.Features;
                    _relativesView.ApplyOptions(StandingsOptions.Load(OverlayData.RelativesKey).ForFeatures(_relativesFeatures));
                    _relativesWindow.SetFixedSize(_relativesView.DesignSize);
                }

                _relativesView.Update(shown);
                if (!_relativesWindow.IsVisible)
                {
                    _relativesWindow.Show();
                }
            }
            catch (Exception ex)
            {
                _relativesTimer.Stop();
                PluginLog.Info("Relatives stopped: " + ex);
            }
        }

        private static double _relativesScaleAtStart;
        private static double _relativesHeightAtStart;
        private static double _relativesScale;

        private static void RelativesScaleDragging(double factor)
        {
            if (_relativesScaleAtStart <= 0)
            {
                _relativesScaleAtStart = StandingsOptions.Load(OverlayData.RelativesKey).Scale;
            }

            double scale = Math.Max(0.5, Math.Min(3, _relativesScaleAtStart * factor));
            if (Math.Abs(scale - _relativesScale) < 0.01)
            {
                return;
            }

            _relativesScale = scale;
            _relativesView.ApplyOptions(StandingsOptions.Load(OverlayData.RelativesKey).ForFeatures(_relativesFeatures).WithScale(scale));
            _relativesView.Update(RelativesSnapshot());
            _relativesWindow.SetFixedSize(_relativesView.DesignSize);
        }

        private static void RelativesScaleDragged()
        {
            double scale = _relativesScale > 0 ? _relativesScale : StandingsOptions.Load(OverlayData.RelativesKey).Scale;
            _relativesScaleAtStart = 0;
            _relativesHeightAtStart = 0;
            _relativesScale = 0;
            StandingsOptions.SetScale(scale, OverlayData.RelativesKey);       // raises Changed: the window snaps to the saved size
            _relativesWindow.SetFixedSize(_relativesView.DesignSize);
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
