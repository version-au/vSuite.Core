using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace vSuite.Ui
{
    /// <summary>
    /// The suite's Options window, laid out like vShell's settings: a list of pages on the left, each page a big title and cards of rows
    /// (the name and a short hint on the left, the control on the right, (i) for the longer story). Every change is saved and applied at
    /// once - there's no OK button. An app's Options derives from this: its constructor sets its own fields, then calls
    /// <see cref="Finish"/>, which runs <see cref="BuildPages"/> (made of Page / Card / Row / Switch / Segmented …, plus
    /// <see cref="UpdatesPage"/>) and opens the page shown last.
    /// </summary>
    public abstract class OptionsWindowBase : Window
    {
        /// <summary>The page shown last, per app (Options opens on it again while the app runs).</summary>
        static string _lastPage;

        Theme _theme;
        bool _blur;
        readonly Dictionary<string, StackPanel> _pages = new Dictionary<string, StackPanel>();
        readonly Dictionary<string, RadioButton> _navItems = new Dictionary<string, RadioButton>();
        ScrollViewer _content;
        StackPanel _nav;
        Image _mark;

        protected OptionsWindowBase(Theme theme, bool blur)
        {
            _theme = theme;
            _blur = blur;
            Title = "Options - " + App.Name;
            Width = Math.Min(980, Math.Max(760, SystemParameters.WorkArea.Width - 80));
            Height = Math.Min(700, Math.Max(460, SystemParameters.WorkArea.Height - 60));
            MinWidth = 760;
            MinHeight = 460;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12.5;
            UseLayoutRounding = true;
            SetResourceReference(ForegroundProperty, "vx.Fg");
            Background = Brushes.Transparent;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false
            });
            SourceInitialized += (o, e) => ApplyTheme(_theme, _blur);
            PreviewKeyDown += (o, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
            Content = BuildFrame();
            // the form controls of vShell's settings (buttons, drop-downs, sliders, text boxes) on this window
            Look.Dress(this, theme, blur, true);
        }

        /// <summary>Build the pages and show the one shown last (or the first). Call at the end of the app's constructor.</summary>
        protected void Finish()
        {
            BuildPages();
            if (_lastPage == null || !_pages.ContainsKey(_lastPage)) _lastPage = _pages.Keys.FirstOrDefault();
            if (_lastPage != null) ShowPage(_lastPage);
        }

        /// <summary>The app's pages, in order.</summary>
        protected abstract void BuildPages();

        /// <summary>A setting changed: the app saves and applies it (to every window).</summary>
        protected abstract void Saved();

        /// <summary>The theme changed in vShell (the app's window passes it on).</summary>
        public void ApplyTheme(Theme t, bool blur)
        {
            _theme = t;
            _blur = blur;
            WindowEffects.Look(this, t, blur);
            Icon = AppIcon.WindowIcon(t);
            if (_mark != null) _mark.Source = AppIcon.Image(t, false);
        }

        // =================================================================
        // The frame: the page list on the left, the page on the right
        // =================================================================

        UIElement BuildFrame()
        {
            var root = new Grid();
            root.SetResourceReference(Panel.BackgroundProperty, "vx.Bg");
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) });
            root.ColumnDefinitions.Add(new ColumnDefinition());

            // ---- the page list ----
            var side = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
            side.SetResourceReference(Border.BackgroundProperty, "vx.Side");
            side.SetResourceReference(Border.BorderBrushProperty, "vx.Line");
            var sideDock = new DockPanel();
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 14, 12, 14) };
            _mark = new Image { Width = 22, Height = 22, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center, Source = AppIcon.Image(_theme, false) };
            brand.Children.Add(_mark);
            brand.Children.Add(new TextBlock { Text = App.Name, FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            brand.Children.Add(Dim("  options", 13, new Thickness(0, 1, 0, 0)));
            DockPanel.SetDock(brand, Dock.Top);
            sideDock.Children.Add(brand);
            _nav = new StackPanel { Margin = new Thickness(8, 0, 8, 12) };
            sideDock.Children.Add(new ScrollViewer { Content = _nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            side.Child = sideDock;
            root.Children.Add(side);

            // ---- the page, with a close button above it ----
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            right.RowDefinitions.Add(new RowDefinition());
            var close = new Button { Content = "", ToolTip = "Close (Esc)", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            close.SetResourceReference(StyleProperty, "vx.CloseButton");
            close.Click += (o, e) => Close();
            right.Children.Add(close);
            // a little in from the right edge: the window is resized from there, and that took the mouse off the page's scrollbar
            _content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Margin = new Thickness(0, 0, 6, 0) };
            Grid.SetRow(_content, 1);
            right.Children.Add(_content);
            Grid.SetColumn(right, 1);
            root.Children.Add(right);
            return root;
        }

        void NavItem(string key, string glyph)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var g = new TextBlock { Text = glyph, Width = 34, FontSize = 15, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.SetResourceReference(TextBlock.FontFamilyProperty, "vx.Icons");
            row.Children.Add(g);
            row.Children.Add(new TextBlock { Text = key, VerticalAlignment = VerticalAlignment.Center });
            var item = new RadioButton { Content = row, GroupName = "nav" + GetHashCode() };
            item.SetResourceReference(StyleProperty, "vx.NavItem");
            item.Checked += (o, e) => { if (!_navigating) ShowPage(key); };
            _navItems[key] = item;
            _nav.Children.Add(item);
        }

        /// <summary>Tests: show a page by its name (any case).</summary>
        public void ShowPageNamed(string name)
        {
            string key = _pages.Keys.FirstOrDefault(k => string.Equals(k, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (key != null) ShowPage(key);
        }

        public void ShowPage(string key)
        {
            StackPanel page;
            if (!_pages.TryGetValue(key, out page)) return;
            _lastPage = key;
            if (_content.Content != page)
            {
                _content.Content = page;
                _content.ScrollToTop();
            }
            RadioButton item;
            string nav;
            if (!_parentOf.TryGetValue(key, out nav)) nav = key;
            if (_navItems.TryGetValue(nav, out item) && item.IsChecked != true) { _navigating = true; item.IsChecked = true; _navigating = false; }
        }

        /// <summary>Sub-pages (opened from a LinkRow, not in the list) and the page they belong to.</summary>
        readonly Dictionary<string, string> _parentOf = new Dictionary<string, string>();
        bool _navigating;

        /// <summary>
        /// A page opened from a row on another page (vShell's Appearance > Colours …): not in the page list (its parent stays lit), a back
        /// arrow before its title. Fill it like a Page; open it with ShowPage(key) - LinkRow does.
        /// </summary>
        protected StackPanel SubPage(string key, string parentKey, string title, string sub)
        {
            _parentOf[key] = parentKey;
            var p = new StackPanel { Margin = new Thickness(30, 0, 30, 30), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var back = new Button { Content = "\uE72B", ToolTip = "Back", Width = 32, Height = 32, FontSize = 13, Margin = new Thickness(-6, 2, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            back.SetResourceReference(StyleProperty, "vx.IconButton");
            back.Click += (o, e) => ShowPage(parentKey);
            head.Children.Add(back);
            head.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(head);
            if (!string.IsNullOrEmpty(sub)) p.Children.Add(Dim(sub, 13, new Thickness(0, 3, 0, 0)));
            _pages[key] = p;
            return p;
        }

        // =================================================================
        // Building blocks (vShell's settings look)
        // =================================================================

        /// <summary>A page: its name in the list (with an icon glyph), its big title and the line under it; cards follow.</summary>
        protected StackPanel Page(string key, string glyph, string sub)
        {
            NavItem(key, glyph);
            var p = new StackPanel { Margin = new Thickness(30, 0, 30, 30), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch };
            p.Children.Add(new TextBlock { Text = key, FontSize = 26, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(sub)) p.Children.Add(Dim(sub, 13, new Thickness(0, 3, 0, 0)));
            _pages[key] = p;
            return p;
        }

        /// <summary>A titled card on the page; rows go into the returned panel.</summary>
        protected static StackPanel Card(StackPanel page, string header)
        {
            page.Children.Add(new TextBlock { Text = header, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, page.Children.Count <= 2 ? 22 : 20, 0, 8) });
            var body = new StackPanel();
            var card = new Border { BorderThickness = new Thickness(1), Child = body };
            card.SetResourceReference(Border.BackgroundProperty, "vx.Panel");
            card.SetResourceReference(Border.BorderBrushProperty, "vx.Line");
            card.SetResourceReference(Border.CornerRadiusProperty, "vx.MenuRadius");
            page.Children.Add(card);
            return body;
        }

        /// <summary>A row in a card, with a line above it unless it's the first.</summary>
        protected static void Place(StackPanel card, UIElement e)
        {
            var wrap = new Border { Child = e, Padding = new Thickness(16, 10, 16, 10) };
            if (card.Children.Count > 0)
            {
                wrap.BorderThickness = new Thickness(0, 1, 0, 0);
                wrap.SetResourceReference(Border.BorderBrushProperty, "vx.Line");
            }
            card.Children.Add(wrap);
        }

        /// <summary>The name (with an (i) when there's more to say) and a short grey hint under it.</summary>
        protected static UIElement Label(string text, string hint, string info)
        {
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { FontSize = 13.5, TextWrapping = TextWrapping.Wrap };
            title.Inlines.Add(new Run(text));
            if (!string.IsNullOrEmpty(info)) title.Inlines.Add(new InlineUIContainer(InfoIcon(info)) { BaselineAlignment = BaselineAlignment.Center });
            left.Children.Add(title);
            if (!string.IsNullOrEmpty(hint))
            {
                TextBlock h = Dim(hint, 12, new Thickness(0, 2, 0, 0));
                h.TextTrimming = TextTrimming.CharacterEllipsis;
                left.Children.Add(h);
            }
            return left;
        }

        /// <summary>
        /// An (i) after a label, as vShell's settings: hover it (or click it) and a card with the help text opens under it, staying while the
        /// mouse is on the icon or the card. A click on it never flips the switch or opens the row it's on.
        /// </summary>
        protected static UIElement InfoIcon(string text)
        {
            var icon = new TextBlock { Text = "\uE946", FontSize = 12, Margin = new Thickness(6, 0, 0, -1), Cursor = Cursors.Help, Background = Brushes.Transparent };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "vx.Icons");
            icon.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, FontSize = 12.5 };
            body.SetResourceReference(TextBlock.ForegroundProperty, "vx.Fg");
            var card = new Border
            {
                Child = body, Padding = new Thickness(12, 9, 12, 10), BorderThickness = new Thickness(1), Margin = new Thickness(6),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 }
            };
            card.SetResourceReference(Border.CornerRadiusProperty, "vx.MenuRadius");
            card.SetResourceReference(Border.BackgroundProperty, "vx.Popup");
            card.SetResourceReference(Border.BorderBrushProperty, "vx.CtrlBorder");
            var pop = new System.Windows.Controls.Primitives.Popup
            {
                Child = card, PlacementTarget = icon, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, AllowsTransparency = true,
                StaysOpen = true, HorizontalOffset = -12, VerticalOffset = 2, PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade
            };
            // close a moment after the mouse leaves the icon, unless it went onto the card
            var hide = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            hide.Tick += (o, e) => { hide.Stop(); if (!icon.IsMouseOver && !card.IsMouseOver) pop.IsOpen = false; };
            icon.MouseEnter += (o, e) => { hide.Stop(); pop.IsOpen = true; icon.SetResourceReference(TextBlock.ForegroundProperty, "vx.Accent"); };
            icon.MouseLeave += (o, e) => { hide.Start(); icon.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim"); };
            card.MouseLeave += (o, e) => hide.Start();
            icon.Unloaded += (o, e) => { hide.Stop(); pop.IsOpen = false; };
            icon.MouseLeftButtonDown += (o, e) => e.Handled = true;
            icon.MouseLeftButtonUp += (o, e) => { e.Handled = true; pop.IsOpen = !pop.IsOpen; };
            // the popup sits next to the icon in the window's tree, so the card finds the window's colours
            var holder = new Grid { VerticalAlignment = VerticalAlignment.Center };
            holder.Children.Add(icon);
            holder.Children.Add(pop);
            return holder;
        }

        /// <summary>The label on the left, the control (if any) on the right.</summary>
        protected static void Row(StackPanel card, string text, string hint, string info, UIElement control)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(Label(text, hint, info));
            if (control != null)
            {
                var fe = control as FrameworkElement;
                if (fe != null) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(16, 0, 0, 0); }
                Grid.SetColumn(control, 1);
                g.Children.Add(control);
            }
            Place(card, g);
        }

        /// <summary>An on / off row: the switch on the right, and a click anywhere on the row flips it.</summary>
        protected CheckBox Switch(StackPanel card, string text, string hint, string info, bool on, Action<bool> set)
        {
            var c = new CheckBox { Content = Label(text, hint, info), IsChecked = on, Margin = new Thickness(0) };
            c.SetResourceReference(StyleProperty, "vx.Switch");
            c.Checked += (o, e) => { set(true); Saved(); };
            c.Unchecked += (o, e) => { set(false); Saved(); };
            Place(card, c);
            return c;
        }

        protected sealed class Option
        {
            public string Text;
            public bool On;
            public Action Pick;
        }

        protected static Option Opt(string text, bool on, Action pick) { return new Option { Text = text, On = on, Pick = pick }; }

        /// <summary>Two to four short choices as joined buttons, the picked one filled with the accent.</summary>
        protected UIElement Segmented(params Option[] options)
        {
            string group = "seg" + Guid.NewGuid().ToString("N");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (Option o in options)
            {
                Option opt = o;
                var r = new RadioButton { Content = opt.Text, GroupName = group, IsChecked = opt.On };
                r.SetResourceReference(StyleProperty, "vx.Segment");
                r.Checked += (s, e) => { opt.Pick(); Saved(); };
                row.Children.Add(r);
            }
            var outer = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(1), Child = row };
            outer.SetResourceReference(Border.BorderBrushProperty, "vx.Track");
            return outer;
        }

        /// <summary>A small grey line under the cards, with an (i) in front.</summary>
        protected static void Note(StackPanel page, string text)
        {
            var row = new DockPanel { Margin = new Thickness(2, 14, 0, 0) };
            var i = new TextBlock { Text = "", FontSize = 12, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            i.SetResourceReference(TextBlock.FontFamilyProperty, "vx.Icons");
            i.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            row.Children.Add(i);
            row.Children.Add(Dim(text, 12, new Thickness(0)));
            page.Children.Add(row);
        }

        protected static Button TextButton(string text, Action click)
        {
            var b = new Button { Content = text };
            b.SetResourceReference(StyleProperty, "vx.TextButton");
            b.Click += (o, e) => click();
            return b;
        }

        protected static WrapPanel Buttons(params Button[] buttons)
        {
            var w = new WrapPanel();
            foreach (Button b in buttons) w.Children.Add(b);
            return w;
        }

        protected static TextBlock Dim(string text, double size, Thickness margin)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            return t;
        }

        // =================================================================
        // More of vShell's settings blocks
        // =================================================================

        /// <summary>
        /// A row that opens another page (a SubPage): a glyph in a tile, the title, a short summary under it, an optional control (a switch,
        /// buttons) and ›. Clicking the row - not the control - opens it; open = null: a row that only shows (no ›).
        /// </summary>
        protected static void LinkRow(StackPanel card, string glyph, string title, string summary, UIElement control, Action open)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var gl = new TextBlock { Text = glyph ?? "", FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            gl.SetResourceReference(TextBlock.FontFamilyProperty, "vx.Icons");
            var box = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Child = gl, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            box.SetResourceReference(Border.BackgroundProperty, "vx.Ctrl");
            g.Children.Add(box);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, FontSize = 13.5 });
            if (!string.IsNullOrEmpty(summary))
            {
                TextBlock s = Dim(summary, 12, new Thickness(0, 2, 0, 0));
                s.TextWrapping = TextWrapping.NoWrap;
                s.TextTrimming = TextTrimming.CharacterEllipsis;
                s.ToolTip = summary;
                text.Children.Add(s);
            }
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            if (control != null)
            {
                var fe = control as FrameworkElement;
                if (fe != null) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(12, 0, 0, 0); }
                Grid.SetColumn(control, 2);
                g.Children.Add(control);
            }
            if (open != null)
            {
                var chev = new TextBlock { Text = "", FontSize = 11, Margin = new Thickness(14, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
                chev.SetResourceReference(TextBlock.FontFamilyProperty, "vx.Icons");
                chev.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
                Grid.SetColumn(chev, 3);
                g.Children.Add(chev);
            }
            // the whole row (to the card's edges) lights up under the mouse
            var row = new Border { Child = g, Background = Brushes.Transparent, Margin = new Thickness(-16, -10, -16, -10), Padding = new Thickness(16, 10, 16, 10) };
            if (open != null)
            {
                row.Cursor = Cursors.Hand;
                row.MouseEnter += (o, e) => row.SetResourceReference(Border.BackgroundProperty, "vx.Ctrl");
                row.MouseLeave += (o, e) => row.Background = Brushes.Transparent;
                row.MouseLeftButtonUp += (o, e) =>
                {
                    if (control != null && e.OriginalSource is DependencyObject && IsInside((DependencyObject)e.OriginalSource, control)) return;
                    e.Handled = true;
                    open();
                };
            }
            Place(card, row);
        }

        static bool IsInside(DependencyObject d, UIElement container)
        {
            while (d != null)
            {
                if (d == container) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>The foldable sections' state while the app runs (closed titles).</summary>
        static readonly HashSet<string> _folded = new HashSet<string>();

        /// <summary>A card whose title folds it away (vShell's module inspector); rows go into the returned panel. headerControl sits in the title.</summary>
        protected static StackPanel Section(StackPanel page, string title, UIElement headerControl = null)
        {
            var body = new StackPanel();
            var outer = new StackPanel();
            var head = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(16, 12, 12, 12) });
            if (headerControl != null)
            {
                var fe = headerControl as FrameworkElement;
                if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(headerControl, 1);
                head.Children.Add(headerControl);
            }
            bool closed = _folded.Contains(title);
            var chev = new TextBlock { Text = closed ? "" : "", FontSize = 10, Margin = new Thickness(8, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            chev.SetResourceReference(TextBlock.FontFamilyProperty, "vx.Icons");
            chev.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            Grid.SetColumn(chev, 2);
            head.Children.Add(chev);
            var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = body, Visibility = closed ? Visibility.Collapsed : Visibility.Visible };
            line.SetResourceReference(Border.BorderBrushProperty, "vx.Line");
            head.MouseLeftButtonUp += (o, e) =>
            {
                if (headerControl != null && e.OriginalSource is DependencyObject && IsInside((DependencyObject)e.OriginalSource, headerControl)) return;
                bool nowClosed = line.Visibility == Visibility.Visible;
                line.Visibility = nowClosed ? Visibility.Collapsed : Visibility.Visible;
                chev.Text = nowClosed ? "" : "";
                if (nowClosed) _folded.Add(title); else _folded.Remove(title);
            };
            outer.Children.Add(head);
            outer.Children.Add(line);
            var card = new Border { BorderThickness = new Thickness(1), Margin = new Thickness(0, page.Children.Count <= 2 ? 22 : 12, 0, 0), Child = outer };
            card.SetResourceReference(Border.BackgroundProperty, "vx.Panel");
            card.SetResourceReference(Border.BorderBrushProperty, "vx.Line");
            card.SetResourceReference(Border.CornerRadiusProperty, "vx.MenuRadius");
            page.Children.Add(card);
            return body;
        }

        /// <summary>Tabs across the page, the picked one underlined (vShell: a plugin's Connection / Notifications / Modules).</summary>
        protected static void Tabs(StackPanel page, string[] names, string current, Action<string> pick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (string name in names)
            {
                string k = name;
                bool on = k == current;
                var col = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
                var t = new TextBlock { Text = k, FontSize = 13.5, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
                t.SetResourceReference(TextBlock.ForegroundProperty, on ? "vx.Fg" : "vx.FgDim");
                col.Children.Add(t);
                var under = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 7, 0, 0), Background = Brushes.Transparent };
                if (on) under.SetResourceReference(Border.BackgroundProperty, "vx.Accent");
                col.Children.Add(under);
                var tab = new Border { Child = col, Background = Brushes.Transparent, Cursor = on ? Cursors.Arrow : Cursors.Hand, Padding = new Thickness(2, 4, 2, 0) };
                if (!on)
                {
                    tab.MouseEnter += (o, e) => t.SetResourceReference(TextBlock.ForegroundProperty, "vx.Fg");
                    tab.MouseLeave += (o, e) => t.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
                    tab.MouseLeftButtonUp += (o, e) => { e.Handled = true; pick(k); };
                }
                row.Children.Add(tab);
            }
            var line = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 18, 0, 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "vx.Line");
            page.Children.Add(line);
        }

        /// <summary>A drop-down of choices (more than four, or long ones) - put it in a Row.</summary>
        protected ComboBox Combo(string[] items, string current, Action<string> set, double minWidth = 200)
        {
            var cb = new ComboBox { MinWidth = minWidth };
            foreach (string i in items) cb.Items.Add(i);
            cb.SelectedItem = current;
            cb.SelectionChanged += (o, e) => { if (cb.SelectedItem is string v) { set(v); Saved(); } };
            return cb;
        }

        /// <summary>A slider with its value beside it (unit after the number) - put it in a Row. Saved when let go.</summary>
        protected UIElement SliderBox(double min, double max, double value, double step, string unit, Action<double> set)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var s = new Slider { Minimum = min, Maximum = max, Value = value, Width = 200, IsSnapToTickEnabled = step > 0, TickFrequency = step > 0 ? step : 1, VerticalAlignment = VerticalAlignment.Center };
            var shown = new TextBlock { MinWidth = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            shown.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            Action show = () => shown.Text = Math.Round(s.Value, step > 0 && step < 1 ? 1 : 0) + (unit ?? "");
            show();
            s.ValueChanged += (o, e) => { show(); set(s.Value); };
            s.PreviewMouseLeftButtonUp += (o, e) => Saved();
            s.LostKeyboardFocus += (o, e) => Saved();
            row.Children.Add(s);
            row.Children.Add(shown);
            return row;
        }

        /// <summary>A text box - put it in a Row. Saved on Enter or when it loses focus.</summary>
        protected TextBox TextField(string value, Action<string> set, double width = 260)
        {
            var tb = new TextBox { Text = value ?? "", Width = width };
            Action save = () => { set(tb.Text); Saved(); };
            tb.LostKeyboardFocus += (o, e) => save();
            tb.KeyDown += (o, e) => { if (e.Key == Key.Enter) { save(); e.Handled = true; } };
            return tb;
        }

        /// <summary>A small rounded chip button with an optional colour dot; on = picked (accent outline).</summary>
        protected static Border Chip(string text, Color? dot, bool on, Action click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (dot.HasValue) sp.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(dot.Value), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 12 });
            var b = new Border { Child = sp, Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0) };
            if (on) { b.SetResourceReference(Border.BorderBrushProperty, "vx.Accent"); b.SetResourceReference(Border.BackgroundProperty, "vx.SelBg"); }
            else { b.SetResourceReference(Border.BorderBrushProperty, "vx.CtrlBorder"); b.Background = Brushes.Transparent; }
            b.MouseLeftButtonUp += (o, e) => { e.Handled = true; click(); };
            return b;
        }

        // =================================================================
        // The Updates page (the same in every app)
        // =================================================================

        TextBlock _updateStatus;
        Button _checkNow, _installNow;
        IUpdateSettings _updates;
        CheckBox _preSwitch;

        /// <summary>The standard Updates page: the version line with Check now / Install / vSuite website, and the two switches.</summary>
        protected void UpdatesPage(IUpdateSettings s)
        {
            _updates = s;
            StackPanel upd = Page("Updates", "", "New versions of " + App.Name);
            StackPanel c = Card(upd, "Version");
            _updateStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
            _checkNow = TextButton("Check now", () => UpdateCenter.CheckNow(true));
            _installNow = TextButton("Install", () => UpdateCenter.Install(UpdateCenter.Available));
            _installNow.SetResourceReference(BackgroundProperty, "vx.Accent");
            _installNow.SetResourceReference(ForegroundProperty, "vx.OnAccent");
            var page = TextButton("vSuite website", () => UpdateCenter.OpenPage(UpdateCenter.Available));
            var status = new StackPanel();
            status.Children.Add(_updateStatus);
            var b = Buttons(_checkNow, _installNow, page);
            b.Margin = new Thickness(0, 10, 0, 0);
            status.Children.Add(b);
            Place(c, status);
            c = Card(upd, "Checking");
            Switch(c, "Check for updates automatically", "A minute after " + App.Name + " starts, then about once a day",
                   "A notice in the window says when there's a new version.", s.UpdateAuto, v => s.UpdateAuto = v);
            _preSwitch = Switch(c, "Include pre-releases", "Test versions published before a release", "vShell's Settings > Updates > vSuite apps shows and changes this too.", s.UpdatePre, v => s.UpdatePre = v);
            Action redraw = () => Dispatcher.BeginInvoke(new Action(DrawUpdates));
            UpdateCenter.Changed += redraw;
            Closed += (o, e) => UpdateCenter.Changed -= redraw;
            DrawUpdates();
        }

        /// <summary>The Version card's line: checking, up to date, a new version (and Install), or why it couldn't check / install.</summary>
        void DrawUpdates()
        {
            UpdateInfo u = UpdateCenter.Available;
            string when = UpdateCenter.LastChecked > DateTime.MinValue ? TimeText.Stamp(UpdateCenter.LastChecked.ToLocalTime())
                        : _updates.UpdateLastCheck > DateTime.MinValue ? TimeText.Stamp(_updates.UpdateLastCheck.ToLocalTime()) : null;
            string text;
            if (UpdateCenter.Installing) text = "Installing " + App.Name + " " + UpdateCenter.VersionText(u) + "…  " + Math.Round(UpdateCenter.Progress * 100) + "%";
            else if (UpdateCenter.Busy) text = "Looking for a new version…";
            else if (UpdateCenter.InstallError.Length > 0) text = "The update didn't install: " + UpdateCenter.InstallError;
            else if (UpdateCenter.LastError.Length > 0) text = App.Name + " couldn't check for updates: " + UpdateCenter.LastError;
            else if (u != null) text = App.Name + " " + UpdateCenter.VersionText(u) + (u.PreRelease ? " (pre-release)" : "") + " is available - you have " + Updater.CurrentText + "." +
                                       (UpdateCenter.IsSkipped(u) ? " (You skipped it.)" : "");
            else text = "You have " + App.Name + " " + Updater.CurrentText + (when != null ? " - the newest, last checked " + when + "." : ".");
            if (Updater.CantUpdateReason != null) text += "\n" + Updater.CantUpdateReason;
            _updateStatus.Text = text;
            _checkNow.IsEnabled = !UpdateCenter.Busy && !UpdateCenter.Installing;
            _installNow.Content = u != null ? "Install " + UpdateCenter.VersionText(u) + " and restart" : "Install";
            _installNow.Visibility = u != null && !UpdateCenter.Installing && Updater.CantUpdateReason == null ? Visibility.Visible : Visibility.Collapsed;
            if (_preSwitch != null && _preSwitch.IsChecked != _updates.UpdatePre) _preSwitch.IsChecked = _updates.UpdatePre;   // changed from vShell
        }
    }
}
