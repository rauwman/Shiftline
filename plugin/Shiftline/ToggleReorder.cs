using System;
using System.Linq;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Lets the toggle buttons of a panel be put into another order by drag and drop: a picture of the button follows the
    /// mouse, a bar in the accent colour shows where it lands, and the new order of the button tags is handed to the owner.
    /// </summary>
    internal sealed class ToggleReorder
    {
        private readonly System.Windows.FrameworkElement _page;
        private readonly System.Windows.Controls.Panel _panel;
        private readonly Action<string[]> _reordered;

        /// <param name="page">The page the picture is drawn over.</param>
        /// <param name="panel">The panel that holds the buttons; their Tag is the key that is saved.</param>
        /// <param name="reordered">Called with the keys in the new order after a drop.</param>
        public ToggleReorder(System.Windows.FrameworkElement page, System.Windows.Controls.Panel panel, Action<string[]> reordered)
        {
            _page = page;
            _panel = panel;
            _reordered = reordered;
        }

        /// <summary>Makes a button draggable and a drop target.</summary>
        public void Attach(System.Windows.Controls.Primitives.ToggleButton toggle)
        {
            toggle.AllowDrop = true;
            toggle.PreviewMouseLeftButtonDown += MouseDown;
            toggle.PreviewMouseMove += MouseMove;
            toggle.DragOver += DragOver;
            toggle.DragLeave += (sender, args) => HideMarker();
            toggle.Drop += Drop;
        }

        /// <summary>
        /// Call first in the button's Click handler: true when the click was only the mouse going up at the end of a drag.
        /// The button's state is then put back and the click should be ignored.
        /// </summary>
        public bool SwallowClick(System.Windows.Controls.Primitives.ToggleButton clicked)
        {
            if (!_suppressClick)
            {
                return false;
            }

            _suppressClick = false;
            clicked.IsChecked = !clicked.IsChecked;
            return true;
        }


        private System.Windows.Point _dragStart;
        private System.Windows.Controls.Primitives.ToggleButton _dragSource;
        private bool _suppressClick;

        private System.Windows.Point _dragGrab;

        private void MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _dragSource = (System.Windows.Controls.Primitives.ToggleButton)sender;
            _dragStart = e.GetPosition(_panel);
            _dragGrab = e.GetPosition(_dragSource);
            _suppressClick = false;
        }

        private void MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_dragSource == null || !ReferenceEquals(sender, _dragSource) || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            {
                return;
            }

            System.Windows.Vector moved = e.GetPosition(_panel) - _dragStart;
            if (Math.Abs(moved.X) < System.Windows.SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(moved.Y) < System.Windows.SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            System.Windows.Controls.Primitives.ToggleButton source = _dragSource;
            _dragSource = null;
            _suppressClick = true;
            // A copy of the column follows the mouse while it is dragged.
            System.Windows.Documents.AdornerLayer layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(_page);
            DragGhost ghost = layer == null ? null : new DragGhost(_page, source, _dragGrab);
            System.Windows.GiveFeedbackEventHandler follow = (s, args) => ghost?.MoveTo(MouseOnPage());
            if (ghost != null)
            {
                layer.Add(ghost);
                ghost.MoveTo(MouseOnPage());
                source.GiveFeedback += follow;
            }

            source.Opacity = 0.35;
            try
            {
                System.Windows.DragDrop.DoDragDrop(source, new System.Windows.DataObject(typeof(System.Windows.Controls.Primitives.ToggleButton), source), System.Windows.DragDropEffects.Move);
            }
            finally
            {
                if (ghost != null)
                {
                    source.GiveFeedback -= follow;
                    layer.Remove(ghost);
                }

                RestoreOpacity(source);
                HideMarker();
            }
        }

        private void Drop(object sender, System.Windows.DragEventArgs e)
        {
            System.Windows.Controls.Primitives.ToggleButton target = (System.Windows.Controls.Primitives.ToggleButton)sender;
            HideMarker();
            System.Windows.Controls.Primitives.ToggleButton source = e.Data.GetData(typeof(System.Windows.Controls.Primitives.ToggleButton)) as System.Windows.Controls.Primitives.ToggleButton;
            if (source == null || ReferenceEquals(source, target))
            {
                return;
            }

            // Dropped on the right half of a column: behind it, otherwise in front of it.
            bool behind = e.GetPosition(target).X > target.ActualWidth / 2;
            _panel.Children.Remove(source);
            int index = _panel.Children.IndexOf(target) + (behind ? 1 : 0);
            _panel.Children.Insert(index, source);
            e.Handled = true;
            _reordered(_panel.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Select(t => (string)t.Tag).ToArray());
        }

        private InsertMarker _marker;

        /// <summary>Shows where the dragged column will land: a bar on the left or right edge of the column under the mouse.</summary>
        private void DragOver(object sender, System.Windows.DragEventArgs e)
        {
            System.Windows.Controls.Primitives.ToggleButton target = (System.Windows.Controls.Primitives.ToggleButton)sender;
            System.Windows.Controls.Primitives.ToggleButton source = e.Data.GetData(typeof(System.Windows.Controls.Primitives.ToggleButton)) as System.Windows.Controls.Primitives.ToggleButton;
            if (source == null || ReferenceEquals(source, target))
            {
                HideMarker();
                e.Effects = System.Windows.DragDropEffects.None;
                e.Handled = true;
                return;
            }

            bool behind = e.GetPosition(target).X > target.ActualWidth / 2;
            if (_marker == null || !ReferenceEquals(_marker.AdornedElement, target) || _marker.Behind != behind)
            {
                HideMarker();
                System.Windows.Documents.AdornerLayer layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(target);
                if (layer != null)
                {
                    System.Windows.Media.Brush accent = _page.TryFindResource("AccentBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DeepSkyBlue;
                    _marker = new InsertMarker(target, behind, accent);
                    layer.Add(_marker);
                }
            }

            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
        }

        private void HideMarker()
        {
            if (_marker != null)
            {
                System.Windows.Documents.AdornerLayer.GetAdornerLayer(_marker.AdornedElement)?.Remove(_marker);
                _marker = null;
            }
        }

        /// <summary>A short upright bar in the accent colour, in the gap left or right of a column.</summary>
        private sealed class InsertMarker : System.Windows.Documents.Adorner
        {
            private readonly System.Windows.Media.Brush _brush;

            public InsertMarker(System.Windows.UIElement target, bool behind, System.Windows.Media.Brush brush) : base(target)
            {
                Behind = behind;
                _brush = brush;
                IsHitTestVisible = false;
            }

            public bool Behind { get; }

            protected override void OnRender(System.Windows.Media.DrawingContext drawingContext)
            {
                System.Windows.Size size = AdornedElement.RenderSize;
                // The columns sit 8 px apart: the bar goes into the middle of that gap.
                double x = Behind ? size.Width + 4 - 1.5 : -4 - 1.5;
                drawingContext.DrawRoundedRectangle(_brush, null, new System.Windows.Rect(x, -3, 3, size.Height + 6), 1.5, 1.5);
            }
        }

        /// <summary>The mouse position on this page. During a drag WPF reports no mouse moves, so it is read from Windows.</summary>
        private System.Windows.Point MouseOnPage()
        {
            System.Drawing.Point screen = System.Windows.Forms.Control.MousePosition;
            return _page.PointFromScreen(new System.Windows.Point(screen.X, screen.Y));
        }

        /// <summary>A see-through picture of the dragged column, drawn above the page at the mouse.</summary>
        private sealed class DragGhost : System.Windows.Documents.Adorner
        {
            private readonly System.Windows.Shapes.Rectangle _picture;
            private readonly System.Windows.Point _grab;
            private System.Windows.Point _at;

            public DragGhost(System.Windows.UIElement page, System.Windows.FrameworkElement source, System.Windows.Point grab) : base(page)
            {
                _grab = grab;
                IsHitTestVisible = false;
                _picture = new System.Windows.Shapes.Rectangle
                {
                    Width = source.ActualWidth,
                    Height = source.ActualHeight,
                    Fill = new System.Windows.Media.ImageBrush(Snapshot(source)) { Opacity = 0.9 },
                    RadiusX = 6,
                    RadiusY = 6,
                    IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.5 }
                };
                AddVisualChild(_picture);
            }

            /// <summary>A still picture of the column, taken before it is dimmed for the drag.</summary>
            private static System.Windows.Media.ImageSource Snapshot(System.Windows.FrameworkElement source)
            {
                System.Windows.DpiScale dpi = System.Windows.Media.VisualTreeHelper.GetDpi(source);
                int width = Math.Max(1, (int)Math.Ceiling(source.ActualWidth * dpi.DpiScaleX));
                int height = Math.Max(1, (int)Math.Ceiling(source.ActualHeight * dpi.DpiScaleY));
                System.Windows.Media.Imaging.RenderTargetBitmap bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
                System.Windows.Media.DrawingVisual drawing = new System.Windows.Media.DrawingVisual();
                using (System.Windows.Media.DrawingContext context = drawing.RenderOpen())
                {
                    context.DrawRectangle(new System.Windows.Media.VisualBrush(source), null, new System.Windows.Rect(0, 0, source.ActualWidth, source.ActualHeight));
                }

                bitmap.Render(drawing);
                bitmap.Freeze();
                return bitmap;
            }

            public void MoveTo(System.Windows.Point at)
            {
                _at = at;
                System.Windows.Documents.AdornerLayer layer = Parent as System.Windows.Documents.AdornerLayer;
                layer?.Update(AdornedElement);
            }

            protected override int VisualChildrenCount
            {
                get { return 1; }
            }

            protected override System.Windows.Media.Visual GetVisualChild(int index)
            {
                return _picture;
            }

            protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)
            {
                _picture.Measure(constraint);
                return _picture.DesiredSize;
            }

            protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
            {
                _picture.Arrange(new System.Windows.Rect(_picture.DesiredSize));
                return finalSize;
            }

            public override System.Windows.Media.GeneralTransform GetDesiredTransform(System.Windows.Media.GeneralTransform transform)
            {
                System.Windows.Media.GeneralTransformGroup group = new System.Windows.Media.GeneralTransformGroup();
                group.Children.Add(base.GetDesiredTransform(transform));
                group.Children.Add(new System.Windows.Media.TranslateTransform(_at.X - _grab.X, _at.Y - _grab.Y));
                return group;
            }
        }

        private static void RestoreOpacity(System.Windows.Controls.Primitives.ToggleButton toggle)
        {
            toggle.Opacity = toggle.IsEnabled ? 1.0 : 0.45;
        }
    }
}
