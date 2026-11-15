using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace vSuite.Ui
{
    /// <summary>
    /// A question or message in the theme (instead of a plain Windows message box): an optional glyph, a title, a line or two, and one to
    /// three buttons. Enter = the default button, Esc = the cancel one. danger: the first button is drawn in the warning colour (the
    /// Secondary Accent).
    ///
    /// Colours and glass come from the owner when it shows the theme (IThemedWindow), else from <see cref="FallbackTheme"/>. In an app
    /// whose resources don't hold the suite's styles (vShell - its bars and widgets keep their own look), the window carries the vx.*
    /// brushes and Controls.xaml in its own resources, so nothing else in that app changes.
    /// </summary>
    public sealed class Ask : Window
    {
        /// <summary>The theme for a question whose owner doesn't show one (vShell: its own theme). null = the app's resources only.</summary>
        public static Func<Theme> FallbackTheme;

        int _answer;

        /// <summary>
        /// Ask; true = the first button. no = null: a message with just the one button. The owner gives the colours and glass (a window that
        /// shows the theme - IThemedWindow); null = centred on the screen.
        /// </summary>
        public static bool YesNo(Window owner, string title, string message, string yes, string no, bool danger)
        {
            string[] buttons = no == null ? new[] { yes } : new[] { yes, no };
            return Show(owner, title, message, buttons, 0, buttons.Length - 1, danger, null, null) == 0;
        }

        /// <summary>
        /// Ask with any buttons; returns the index of the one picked (Esc / closing = cancelIndex). glyph: a Segoe Fluent icon before the
        /// title, or null. place: called before the window shows, to set its owner / position (vShell: on top of everything for a widget);
        /// null = owned by 'owner' when it's showing.
        /// </summary>
        public static int Show(Window owner, string title, string message, string[] buttons, int defaultIndex, int cancelIndex, bool danger,
                               string glyph, Action<Window> place)
        {
            // tests (PREFIX_TEST_HOOKS=1): PREFIX_TEST_ASK=yes / no answers without showing the question (yes = the first button)
            string answer = App.TestHooks ? App.Env("TEST_ASK") : null;
            if (answer == "yes" || answer == "no") { Log.Write("Test answered '" + answer + "': " + title); return answer == "yes" ? 0 : cancelIndex; }
            var a = new Ask(owner, title, message, buttons, defaultIndex, cancelIndex, danger, glyph, place);
            a.ShowDialog();
            return a._answer;
        }

        Ask(Window ownerWindow, string title, string message, string[] buttons, int defaultIndex, int cancelIndex, bool danger, string glyph, Action<Window> place)
        {
            _answer = cancelIndex;
            if (place != null) place(this);
            else if (ownerWindow != null && ownerWindow.IsLoaded && ownerWindow.IsVisible) Owner = ownerWindow;
            var themed = ownerWindow as IThemedWindow;
            Theme theme = themed != null && themed.CurrentTheme != null ? themed.CurrentTheme : FallbackTheme != null ? FallbackTheme() : null;
            // see-through like the owner (an app's "Use the theme's opacity"); without one (vShell) always, as vShell's bars
            bool blur = themed != null && themed.CurrentTheme != null ? themed.Blurred : theme != null;
            Dress(this, theme, blur);

            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
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
            SourceInitialized += (o, e) =>
            {
                if (theme != null) WindowEffects.Look(this, theme, blur);
            };

            var root = new Border { Padding = new Thickness(22, 18, 22, 18) };
            root.SetResourceReference(Border.BackgroundProperty, "vx.Bg");
            var stack = new StackPanel();
            root.Child = stack;
            var head = new DockPanel();
            if (!string.IsNullOrEmpty(glyph))
            {
                var g = new TextBlock { Text = glyph, FontSize = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                g.SetResourceReference(TextBlock.FontFamilyProperty, "vx.Icons");
                g.SetResourceReference(TextBlock.ForegroundProperty, danger ? "vx.Secondary" : "vx.Accent");
                head.Children.Add(g);
            }
            head.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(head);
            var msg = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 18) };
            msg.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            stack.Children.Add(msg);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var made = new Button[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                var b = new Button { Content = buttons[i], MinWidth = 90, Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0) };
                b.SetResourceReference(StyleProperty, "vx.TextButton");
                if (i == 0)
                {
                    b.SetResourceReference(Control.BackgroundProperty, danger ? "vx.Secondary" : "vx.Accent");
                    // text that reads on the button's colour
                    Color fill = theme != null ? (danger ? theme.Secondary : theme.Accent) : Colors.SteelBlue;
                    b.Foreground = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255 > 0.6 ? Brushes.Black : Brushes.White;
                    b.FontWeight = FontWeights.SemiBold;
                }
                b.Click += (o, e) => { _answer = index; Close(); };
                made[i] = b;
                row.Children.Add(b);
            }
            stack.Children.Add(row);
            Content = root;

            // the default answer has the focus to start with; Tab moves it, and Enter presses the button that has it (as Windows' boxes)
            Loaded += (o, e) => { if (defaultIndex >= 0 && defaultIndex < made.Length) made[defaultIndex].Focus(); };
            PreviewKeyDown += (o, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; _answer = cancelIndex; Close(); }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    int focused = Array.IndexOf(made, Keyboard.FocusedElement as Button);
                    _answer = focused >= 0 ? focused : defaultIndex;
                    Close();
                }
            };
        }

        /// <summary>Give a window the suite's look when the app's own resources don't have it (vShell) - see Look.Dress.</summary>
        public static void Dress(Window w, Theme theme, bool blur) { Look.Dress(w, theme, blur, false); }    }
}
