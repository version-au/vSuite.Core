using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace vSuite.Ui
{
    /// <summary>One row of a FormWindow: a text box, a password box, a drop-down or a tick box, with a line of help under it.</summary>
    public sealed class FormField
    {
        public enum Kinds { Text, Password, Combo, Check }
        public Kinds Kind = Kinds.Text;
        public string Label = "", Value = "", Hint = "";
        public string[] Items;
        public bool Checked;

        public FormField(string label, string value) { Label = label ?? ""; Value = value ?? ""; }

        public static FormField Password(string label, string hint) { return new FormField(label, "") { Kind = Kinds.Password, Hint = hint ?? "" }; }
        public static FormField Combo(string label, string value, string[] items) { return new FormField(label, value) { Kind = Kinds.Combo, Items = items }; }
        public static FormField Check(string label, bool on) { return new FormField(label, "") { Kind = Kinds.Check, Checked = on }; }
    }

    /// <summary>
    /// A small form in the theme (a password, a line number, a saved connection): the fields one under another, OK and Cancel.
    /// Enter = OK, Esc = Cancel. Owned by the window it's asked from. (vShell has a FormWindow of its own, in its plugin API.)
    /// </summary>
    public sealed class FormWindow : Window
    {
        readonly List<FormField> _fields;
        readonly List<Func<bool>> _read = new List<Func<bool>>();
        bool _ok;

        /// <summary>Show the form; true = OK (the fields hold what was typed).</summary>
        public static bool Ask(Window owner, string title, List<FormField> fields)
        {
            var w = new FormWindow(owner, title, fields);
            w.ShowDialog();
            return w._ok;
        }

        /// <summary>Ask for one line of text; null = cancelled.</summary>
        public static string Prompt(Window owner, string title, string label, string value)
        {
            var f = new List<FormField> { new FormField(label, value) };
            return Ask(owner, title, f) ? f[0].Value : null;
        }

        FormWindow(Window owner, string title, List<FormField> fields)
        {
            _fields = fields;
            if (owner != null && owner.IsLoaded && owner.IsVisible) Owner = owner;
            Theme theme = Looks.Theme;
            bool blur = Looks.Blur;
            Look.Dress(this, theme, blur, true);
            Title = title;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = Owner == null;
            WindowStartupLocation = Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12.5;
            UseLayoutRounding = true;
            Background = Brushes.Transparent;
            SetResourceReference(ForegroundProperty, "vx.Fg");
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 30, ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false
            });
            SourceInitialized += (o, e) => { if (theme != null) WindowEffects.Look(this, theme, blur); };

            var root = new Border { Padding = new Thickness(22, 18, 22, 18) };
            root.SetResourceReference(Border.BackgroundProperty, "vx.Bg");
            var stack = new StackPanel();
            root.Child = stack;
            stack.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = SystemParameters.WorkArea.Height * 0.7 };
            var rows = new StackPanel();
            scroll.Content = rows;
            stack.Children.Add(scroll);
            Control first = null;
            foreach (FormField f in fields)
            {
                FormField me = f;
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                Control input;
                switch (f.Kind)
                {
                    case FormField.Kinds.Password:
                        {
                            row.Children.Add(Label(f.Label));
                            var pb = new PasswordBox();
                            _read.Add(() => { me.Value = pb.Password; return true; });
                            input = pb;
                            break;
                        }
                    case FormField.Kinds.Combo:
                        {
                            row.Children.Add(Label(f.Label));
                            var cb = new ComboBox { ItemsSource = f.Items ?? new string[0], SelectedItem = f.Value, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260 };
                            _read.Add(() => { me.Value = cb.SelectedItem as string ?? me.Value; return true; });
                            input = cb;
                            break;
                        }
                    case FormField.Kinds.Check:
                        {
                            var ch = new CheckBox { Content = f.Label, IsChecked = f.Checked };
                            _read.Add(() => { me.Checked = ch.IsChecked == true; return true; });
                            input = ch;
                            break;
                        }
                    default:
                        {
                            row.Children.Add(Label(f.Label));
                            var tb = new TextBox { Text = f.Value };
                            _read.Add(() => { me.Value = tb.Text; return true; });
                            input = tb;
                            break;
                        }
                }
                row.Children.Add(input);
                if (first == null && f.Kind != FormField.Kinds.Check) first = input;
                if (!string.IsNullOrEmpty(f.Hint))
                {
                    var hint = new TextBlock { Text = f.Hint, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0) };
                    hint.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
                    row.Children.Add(hint);
                }
                rows.Children.Add(row);
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true };
            ok.SetResourceReference(StyleProperty, "vx.Primary");
            ok.Click += (o, e) => Finish();
            var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            cancel.Click += (o, e) => Close();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            stack.Children.Add(buttons);
            Content = root;

            Loaded += (o, e) =>
            {
                if (first == null) return;
                first.Focus();
                var tb = first as TextBox;
                if (tb != null) tb.SelectAll();
            };
            PreviewKeyDown += (o, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }
                else if (e.Key == Key.Enter && !(Keyboard.FocusedElement is ComboBox)) { e.Handled = true; Finish(); }
            };
        }

        static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 4), FontWeight = FontWeights.SemiBold };
        }

        void Finish()
        {
            foreach (Func<bool> r in _read) r();
            _ok = true;
            Close();
        }
    }

    /// <summary>The theme in use now (every window's dialogs take it), and questions in it.</summary>
    public static class Looks
    {
        public static Theme Theme;
        public static bool Blur = true;

        public static bool YesNo(Window owner, string question, string title)
        {
            return vSuite.Ui.Ask.YesNo(owner, title, question, "Yes", "No", false);
        }

        public static void Tell(Window owner, string text, string title)
        {
            vSuite.Ui.Ask.YesNo(owner, title, text, "OK", null, false);
        }
    }
}
