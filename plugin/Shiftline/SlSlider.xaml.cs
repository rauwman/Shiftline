using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Shiftline.Plugin
{
    /// <summary>
    /// A slider row for whole numbers: title, bar with a round dot, a number field and + / - buttons. It replaces SimHub's
    /// TitledSlider, whose bar was stretched sideways (oval dot).
    /// </summary>
    public partial class SlSlider : UserControl
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            "Title", typeof(string), typeof(SlSlider), new PropertyMetadata(string.Empty, (d, e) => ((SlSlider)d).TitleText.Text = (string)e.NewValue));

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            "Minimum", typeof(double), typeof(SlSlider), new PropertyMetadata(0.0, (d, e) => ((SlSlider)d).Bar.Minimum = (double)e.NewValue));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            "Maximum", typeof(double), typeof(SlSlider), new PropertyMetadata(100.0, (d, e) => ((SlSlider)d).Bar.Maximum = (double)e.NewValue));

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value", typeof(double), typeof(SlSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((SlSlider)d).ShowValue((double)e.NewValue)));

        private bool _updating;

        public SlSlider()
        {
            InitializeComponent();
            Bar.Maximum = 100;
        }

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public double Minimum
        {
            get { return (double)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        private void ShowValue(double value)
        {
            _updating = true;
            Bar.Value = value;
            Box.Text = Math.Round(value).ToString(CultureInfo.CurrentCulture);
            _updating = false;
        }

        private void Bar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }

            Value = Math.Round(e.NewValue);
        }

        private void SetClamped(double value)
        {
            Value = Math.Max(Bar.Minimum, Math.Min(Bar.Maximum, Math.Round(value)));
            Box.Text = Math.Round(Value).ToString(CultureInfo.CurrentCulture);
        }

        private void Commit()
        {
            double number;
            if (double.TryParse(Box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out number))
            {
                SetClamped(number);
            }
            else
            {
                Box.Text = Math.Round(Value).ToString(CultureInfo.CurrentCulture);
            }
        }

        private void Box_LostFocus(object sender, RoutedEventArgs e)
        {
            Commit();
        }

        private void Box_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                Keyboard.ClearFocus();
            }
        }

        private void Plus_Click(object sender, RoutedEventArgs e)
        {
            SetClamped(Value + 1);
        }

        private void Minus_Click(object sender, RoutedEventArgs e)
        {
            SetClamped(Value - 1);
        }
    }
}
