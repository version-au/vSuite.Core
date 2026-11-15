using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace vSuite.Ui
{
    /// <summary>
    /// One tab in a <see cref="TabStrip"/>: vXplorer's tab - a card with rounded top corners, the theme's Panel colour and a Main Accent line
    /// under it when chosen, the hover tint, an icon, the name (trimmed), something extra after it (a chip), and a close button that shows
    /// on the chosen tab and under the pointer (an unsaved tab shows a dot there until the pointer comes).
    /// </summary>
    public sealed class StripTab
    {
        internal readonly Grid Root = new Grid { Background = Brushes.Transparent };
        readonly Border _bg = new Border();
        readonly Border _line = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(1), Visibility = Visibility.Hidden };
        readonly TextBlock _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly Image _icon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
        readonly ContentControl _extra = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed, Focusable = false };
        internal readonly Button CloseBtn = new Button { Content = "", ToolTip = "Close tab" };
        readonly Ellipse _dot = new Ellipse { Width = 7, Height = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        bool _active, _dirty;
        Brush _lineBrush;

        /// <summary>The app's own object for this tab.</summary>
        public object Tag { get; set; }
        /// <summary>The strip it's in now (null once taken out).</summary>
        public TabStrip Strip { get; internal set; }

        public StripTab(object tag, string title)
        {
            Tag = tag;
            _bg.SetResourceReference(Border.CornerRadiusProperty, "vx.TabRadius");
            _line.SetResourceReference(Border.BackgroundProperty, "vx.Accent");
            _dot.SetResourceReference(Shape.FillProperty, "vx.Accent");
            CloseBtn.SetResourceReference(FrameworkElement.StyleProperty, "vx.TabClose");
            RenderOptions.SetBitmapScalingMode(_icon, BitmapScalingMode.HighQuality);
            var end = new Grid { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
            end.Children.Add(_dot);
            end.Children.Add(CloseBtn);
            var row = new DockPanel { Margin = new Thickness(10, 0, 4, 0) };
            DockPanel.SetDock(end, Dock.Right);
            DockPanel.SetDock(_extra, Dock.Right);
            DockPanel.SetDock(_icon, Dock.Left);
            row.Children.Add(end);
            row.Children.Add(_extra);
            row.Children.Add(_icon);
            row.Children.Add(_label);
            Root.Children.Add(_bg);
            Root.Children.Add(_line);
            Root.Children.Add(row);
            Root.Margin = new Thickness(0, 0, 2, 0);
            WindowChrome.SetIsHitTestVisibleInChrome(Root, true);
            Root.MouseEnter += (o, e) => Paint();
            Root.MouseLeave += (o, e) => Paint();
            Title = title;
            Paint();
        }

        public string Title { get { return _label.Text; } set { _label.Text = value ?? ""; } }

        public ImageSource Icon
        {
            get { return _icon.Source; }
            set { _icon.Source = value; _icon.Visibility = value == null ? Visibility.Collapsed : Visibility.Visible; }
        }

        /// <summary>Shown when the pointer rests on the tab.</summary>
        public object ToolTip { get { return Root.ToolTip; } set { Root.ToolTip = value; } }

        /// <summary>Something after the name (a server's name as a chip), or null.</summary>
        public object Extra
        {
            get { return _extra.Content; }
            set { _extra.Content = value; _extra.Visibility = value == null ? Visibility.Collapsed : Visibility.Visible; }
        }

        /// <summary>Unsaved: a dot where the close button is, until the pointer comes.</summary>
        public bool Dirty { get { return _dirty; } set { _dirty = value; Paint(); } }

        /// <summary>The line's own colour (an SSH connection's colour): shown faintly when not chosen too. null = the Main Accent, chosen only.</summary>
        public Brush LineBrush
        {
            get { return _lineBrush; }
            set
            {
                _lineBrush = value;
                if (value != null) _line.Background = value; else _line.SetResourceReference(Border.BackgroundProperty, "vx.Accent");
                Paint();
            }
        }

        public bool IsActive { get { return _active; } internal set { _active = value; Paint(); } }

        /// <summary>Files dragged over it would go here (vXplorer: into its folder) - the Active tint.</summary>
        public bool DropHighlight { get { return _drop; } set { _drop = value; Paint(); } }
        bool _drop;

        /// <summary>The tab itself, for an app's own events on it (vXplorer: files dragged onto it - AllowDrop, DragOver, Drop).</summary>
        public FrameworkElement Element { get { return Root; } }

        internal double Width { set { Root.Width = value; } }
        internal double Height { set { Root.Height = value; } }

        void Paint()
        {
            bool over = Root.IsMouseOver;
            if (_drop) _bg.SetResourceReference(Border.BackgroundProperty, "vx.Active");
            else if (_active) _bg.SetResourceReference(Border.BackgroundProperty, "vx.Panel");
            else if (over) _bg.SetResourceReference(Border.BackgroundProperty, "vx.Hover");
            else _bg.Background = Brushes.Transparent;
            _label.SetResourceReference(TextBlock.ForegroundProperty, _active ? "vx.Fg" : "vx.FgDim");
            _line.Visibility = _active || _lineBrush != null ? Visibility.Visible : Visibility.Hidden;
            _line.Opacity = _active ? 1 : 0.45;
            bool dot = _dirty && !over;
            _dot.Visibility = dot ? Visibility.Visible : Visibility.Collapsed;
            CloseBtn.Visibility = !dot && (_active || over) ? Visibility.Visible : Visibility.Hidden;
        }
    }

    /// <summary>
    /// The suite's tab strip, as vXplorer's: tabs share the room (as wide as 220, narrower as more open, never under 90 - then arrows either
    /// side scroll through them, and the wheel does too), then the app's buttons (+ …). Dragging a tab along the strip: it follows the
    /// pointer, the others slide out of its way, and it glides into its place on release (250 ms ease-out). Pulled out of the strip, a copy
    /// follows the pointer: let go over another strip (or another window of the app) and the app moves it there (<see cref="MovedTo"/>),
    /// in the open and the app makes a new window for it (<see cref="TornOut"/>). A window's only tab moves the window instead (Windows' own
    /// move), joining another window's strip if it's let go on one. Esc or losing the mouse puts everything back. Colours are vx.*.
    /// </summary>
    public sealed class TabStrip : Grid
    {
        const int SlideMillis = 250;
        const double TearDistance = 36, EdgeZone = 40;

        /// <summary>Every strip that's open (in this app): where a dragged tab can go.</summary>
        static readonly List<TabStrip> Live = new List<TabStrip>();

        readonly List<StripTab> _list = new List<StripTab>();
        readonly StackPanel _row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        readonly RepeatButton _leftBtn = new RepeatButton { Content = "", ToolTip = "Scroll the tabs left", Margin = new Thickness(0, 0, 2, 2), Visibility = Visibility.Collapsed };
        readonly RepeatButton _rightBtn = new RepeatButton { Content = "", ToolTip = "Scroll the tabs right", Margin = new Thickness(2, 0, 0, 2), Visibility = Visibility.Collapsed };
        readonly ScrollViewer _scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        readonly StackPanel _panel = new StackPanel { Orientation = Orientation.Horizontal };
        readonly StackPanel _trail = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        StripTab _active;

        // ---- what the app hears ----
        /// <summary>A tab was clicked (the app shows it - the strip has marked it chosen already).</summary>
        public event Action<StripTab> Picked;
        /// <summary>Its close button, or a middle-click.</summary>
        public event Action<StripTab> CloseClicked;
        /// <summary>A right-click on a tab (its menu).</summary>
        public event Action<StripTab> MenuRequested;
        /// <summary>A tab took a new place along the strip (from index, to index).</summary>
        public event Action<StripTab, int, int> Reordered;
        /// <summary>A tab dropped on another strip (at that index, -1 = the end): the app moves it there (Remove here, Insert there).</summary>
        public event Action<StripTab, TabStrip, int> MovedTo;
        /// <summary>A tab let go in the open (screen pixels): the app makes a new window for it there.</summary>
        public event Action<StripTab, Point> TornOut;
        /// <summary>A press on a tab is over (a click, or a drag that ended) - vTerm gives the keyboard back to the terminal then.</summary>
        public event Action<StripTab> PressEnded;

        /// <summary>The window's only tab may still be pulled out (vTerm's drop-down) - otherwise dragging it moves the window.</summary>
        public bool CanTearLast;
        /// <summary>Another window that counts as this strip's for a drop (vTerm's text window, a window of its own over the terminal).</summary>
        public Func<IntPtr, bool> OwnsWindow;
        /// <summary>How much of the row to leave free beside the tabs (the window is dragged by it).</summary>
        public double Reserve = 56;
        public double MinTabWidth = 90, MaxTabWidth = 220;
        double _tabHeight = 34;

        public TabStrip()
        {
            _leftBtn.SetResourceReference(StyleProperty, "vx.ScrollArrow");
            _rightBtn.SetResourceReference(StyleProperty, "vx.ScrollArrow");
            _leftBtn.Click += (o, e) => ScrollTabs(-1);
            _rightBtn.Click += (o, e) => ScrollTabs(1);
            _scroll.Content = _panel;
            WindowChrome.SetIsHitTestVisibleInChrome(_scroll, true);
            _scroll.ScrollChanged += (o, e) => UpdateArrows();
            _scroll.PreviewMouseWheel += (o, e) => { _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset - e.Delta / 2.0); e.Handled = true; };
            _trail.Margin = new Thickness(4, 0, 0, 2);
            _row.Children.Add(_leftBtn);
            _row.Children.Add(_scroll);
            _row.Children.Add(_rightBtn);
            _row.Children.Add(_trail);
            Children.Add(_row);
            SizeChanged += (o, e) => UpdateWidths();
            _trail.SizeChanged += (o, e) => UpdateWidths();
            Loaded += (o, e) => { if (!Live.Contains(this)) Live.Add(this); UpdateWidths(); };
            Unloaded += (o, e) => Live.Remove(this);
        }

        // =================================================================
        // Tabs
        // =================================================================
        public IReadOnlyList<StripTab> Tabs { get { return _list; } }
        public StripTab Active { get { return _active; } }
        public int Count { get { return _list.Count; } }

        /// <summary>The tabs' height (34, as vXplorer's).</summary>
        public double TabHeight { get { return _tabHeight; } set { _tabHeight = value; foreach (StripTab t in _list) t.Height = value; } }

        /// <summary>A button after the tabs (+, …).</summary>
        public void AddButton(UIElement b) { _trail.Children.Add(b); }

        /// <summary>A new tab at index (-1 = the end).</summary>
        public StripTab Add(object tag, string title, int index = -1)
        {
            var t = new StripTab(tag, title);
            Insert(t, index);
            return t;
        }

        /// <summary>Every tab out (an app putting its tabs back in from its own list).</summary>
        public void Clear()
        {
            foreach (StripTab t in _list.ToList()) Remove(t);
        }

        /// <summary>Put a tab in at index (-1 = the end) - one from another strip leaves that one first.</summary>
        public void Insert(StripTab t, int index = -1)
        {
            if (t.Strip != null && t.Strip != this) t.Strip.Remove(t);
            if (t.Strip == this) return;
            t.Strip = this;
            Hook(t, true);
            ResetOne(t);
            if (index < 0 || index > _list.Count) index = _list.Count;
            _list.Insert(index, t);
            _panel.Children.Insert(index, t.Root);
            t.Height = _tabHeight;
            UpdateWidths();
        }

        /// <summary>Take a tab out (closing, or moving to another strip). The app chooses the next one to show (Select).</summary>
        public void Remove(StripTab t)
        {
            if (t == null || t.Strip != this) return;
            if (_dragTab == t) EndDrag(false);
            Hook(t, false);
            _list.Remove(t);
            _panel.Children.Remove(t.Root);
            t.Strip = null;
            if (_active == t) { _active = null; t.IsActive = false; }
            UpdateWidths();
        }

        /// <summary>Mark a tab chosen (the app shows its content) and scroll it into view.</summary>
        public void Select(StripTab t)
        {
            if (_active != null && _active != t) _active.IsActive = false;
            _active = t;
            if (t == null) return;
            t.IsActive = true;
            BringIntoView(t);
        }

        /// <summary>Move a tab to index, every tab gliding from where it is now to its new place.</summary>
        public void Move(StripTab t, int to)
        {
            int from = _list.IndexOf(t);
            if (from < 0) return;
            to = Math.Max(0, Math.Min(_list.Count - 1, to));
            var was = new Dictionary<StripTab, double>();
            foreach (StripTab s in _list) if (s.Root.IsLoaded) was[s] = s.Root.TranslatePoint(new Point(0, 0), _panel).X;   // includes the slide
            ResetSlides();
            if (to != from)
            {
                _list.RemoveAt(from);
                _list.Insert(to, t);
                _panel.Children.Remove(t.Root);
                _panel.Children.Insert(to, t.Root);
            }
            _panel.UpdateLayout();
            foreach (StripTab s in _list)
            {
                double old;
                if (!s.Root.IsLoaded || !was.TryGetValue(s, out old)) continue;
                double d = old - s.Root.TranslatePoint(new Point(0, 0), _panel).X;
                if (Math.Abs(d) < 0.5) continue;
                TranslateTransform tt = SlideOf(s.Root);
                tt.X = d;
                Glide(tt, 0, d);
            }
            if (to != from)
            {
                BringIntoView(t);
                if (Reordered != null) Reordered(t, from, to);
            }
        }

        void Hook(StripTab t, bool on)
        {
            if (on)
            {
                t.Root.MouseLeftButtonDown += Tab_Down;
                t.Root.MouseMove += Tab_Move;
                t.Root.MouseLeftButtonUp += Tab_Up;
                t.Root.LostMouseCapture += Tab_LostCapture;
                t.Root.MouseUp += Tab_Middle;
                t.Root.MouseRightButtonUp += Tab_Right;
                t.CloseBtn.Click += Close_Click;
            }
            else
            {
                t.Root.MouseLeftButtonDown -= Tab_Down;
                t.Root.MouseMove -= Tab_Move;
                t.Root.MouseLeftButtonUp -= Tab_Up;
                t.Root.LostMouseCapture -= Tab_LostCapture;
                t.Root.MouseUp -= Tab_Middle;
                t.Root.MouseRightButtonUp -= Tab_Right;
                t.CloseBtn.Click -= Close_Click;
            }
        }

        StripTab TabOf(object sender)
        {
            var fe = sender as FrameworkElement;
            if (fe == null) return null;
            foreach (StripTab t in _list) if (t.Root == fe || t.CloseBtn == fe) return t;
            return null;
        }

        void Close_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            StripTab t = TabOf(sender);
            if (t != null && CloseClicked != null) CloseClicked(t);
        }

        void Tab_Middle(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            e.Handled = true;
            StripTab t = TabOf(sender);
            if (t != null && CloseClicked != null) CloseClicked(t);
        }

        void Tab_Right(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            StripTab t = TabOf(sender);
            if (t != null && MenuRequested != null) MenuRequested(t);
        }

        // =================================================================
        // Widths and the arrows
        // =================================================================
        /// <summary>
        /// The tabs' width for this much room: as wide as max, narrower as more open, never under min; overflow = they don't all fit at
        /// min (the arrows show, and take two arrow widths of the room).
        /// </summary>
        public static double WidthFor(double room, int count, double min, double max, double arrows, out bool overflow)
        {
            overflow = count > 0 && count * (min + 2) > room;
            if (overflow) room -= arrows;
            if (count <= 0) return max;
            return Math.Max(min, Math.Min(max, room / count - 2));
        }

        void UpdateWidths()
        {
            double room = ActualWidth - _trail.ActualWidth - _trail.Margin.Left - Reserve;
            if (room <= 0) return;
            bool overflow;
            double w = WidthFor(room, _list.Count, MinTabWidth, MaxTabWidth, 2 * (24 + 2), out overflow);
            Visibility arrows = overflow ? Visibility.Visible : Visibility.Collapsed;
            bool newly = overflow && _leftBtn.Visibility != Visibility.Visible;
            if (newly && _active != null) BringIntoView(_active);     // just started scrolling: keep the chosen tab in sight
            _leftBtn.Visibility = arrows;
            _rightBtn.Visibility = arrows;
            if (overflow) room -= 2 * (24 + 2);
            foreach (StripTab t in _list) t.Width = w;
            _scroll.MaxWidth = Math.Max(MinTabWidth, room);
            UpdateArrows();
        }

        double Slot { get { return _list.Count > 0 && _list[0].Root.ActualWidth > 0 ? _list[0].Root.ActualWidth + 2 : MaxTabWidth + 2; } }

        void UpdateArrows()
        {
            _leftBtn.IsEnabled = _scroll.HorizontalOffset > 0.5;
            _rightBtn.IsEnabled = _scroll.HorizontalOffset < _scroll.ScrollableWidth - 0.5;
        }

        /// <summary>One tab along, lined up so a whole tab is at the edge.</summary>
        void ScrollTabs(int dir)
        {
            double slot = Slot;
            double at = _scroll.HorizontalOffset / slot;
            double to = dir < 0 ? Math.Ceiling(at - 0.01) - 1 : Math.Floor(at + 0.01) + 1;
            _scroll.ScrollToHorizontalOffset(Math.Max(0, Math.Min(_scroll.ScrollableWidth, to * slot)));
        }

        void BringIntoView(StripTab t)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { if (t.Strip == this) t.Root.BringIntoView(); }));
        }

        // =================================================================
        // Dragging
        // =================================================================
        StripTab _dragTab;
        Point _pressAt;                      // in _panel
        bool _dragging, _torn;
        int _dragFrom, _dragTo;
        double _slot;
        readonly Dictionary<StripTab, double> _slideGoal = new Dictionary<StripTab, double>();
        Popup _ghost;
        System.Windows.Threading.DispatcherTimer _edgeTimer;

        /// <summary>A tab is pressed (maybe the start of a drag) - vTerm's text window waits before taking the keyboard.</summary>
        public bool Pressing { get { return _dragTab != null; } }

        /// <summary>Esc while dragging: everything goes back.</summary>
        public void CancelDrag() { EndDrag(false); }

        void Tab_Down(object sender, MouseButtonEventArgs e)
        {
            StripTab t = TabOf(sender);
            if (t == null) return;
            e.Handled = true;
            Select(t);
            _dragTab = t;
            _pressAt = e.GetPosition(_panel);
            _dragging = _torn = false;
            t.Root.CaptureMouse();
            if (Picked != null) Picked(t);
        }

        void Tab_Move(object sender, MouseEventArgs e)
        {
            if (_dragTab == null || TabOf(sender) != _dragTab) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(false); return; }
            Point p = e.GetPosition(_panel);
            if (!_dragging)
            {
                if (Math.Abs(p.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance * 2 &&
                    Math.Abs(p.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
                StartDrag();
                if (!_dragging) return;
            }

            // out of the strip (and there's another tab to leave behind, or the last one may go): it comes loose
            Point inArea = e.GetPosition(this);
            bool outside = inArea.Y < -TearDistance || inArea.Y > ActualHeight + TearDistance || inArea.X < -TearDistance || inArea.X > ActualWidth + TearDistance;
            bool canTear = _list.Count > 1 || CanTearLast;
            if (outside && canTear && !_torn) Tear(true);
            else if (!outside && _torn) Tear(false);
            if (_torn) { MoveGhost(); return; }

            // near the ends of a strip that scrolls: scroll it gently, so a tab can reach tabs out of view
            if (_scroll.ScrollableWidth > 0)
            {
                if (_edgeTimer == null)
                {
                    _edgeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                    _edgeTimer.Tick += (s, a) => EdgeScroll();
                }
                if (!_edgeTimer.IsEnabled) _edgeTimer.Start();
            }
            FollowMouse();
        }

        void EdgeScroll()
        {
            if (_dragTab == null || !_dragging || _torn) { _edgeTimer.Stop(); return; }
            double x = Mouse.GetPosition(_scroll).X, w = _scroll.ActualWidth, step = 0;
            if (x < EdgeZone) step = -(EdgeZone - Math.Max(0, x)) / EdgeZone;
            else if (x > w - EdgeZone) step = (x - (w - EdgeZone)) / EdgeZone;
            if (step == 0) return;
            step = Math.Max(-1, Math.Min(1, step)) * 7;
            double to = Math.Max(0, Math.Min(_scroll.ScrollableWidth, _scroll.HorizontalOffset + step));
            if (Math.Abs(to - _scroll.HorizontalOffset) < 0.1) return;
            _scroll.ScrollToHorizontalOffset(to);
            _scroll.UpdateLayout();
            FollowMouse();
        }

        /// <summary>The dragged tab under the pointer along the strip; the others make room.</summary>
        void FollowMouse()
        {
            Point p = Mouse.GetPosition(_panel);
            double dx = p.X - _pressAt.X;
            dx = Math.Max(-_dragFrom * _slot, Math.Min((_list.Count - 1 - _dragFrom) * _slot, dx));
            SlideOf(_dragTab.Root).X = dx;
            int to = Math.Max(0, Math.Min(_list.Count - 1, (int)Math.Round(_dragFrom + dx / _slot)));
            if (to != _dragTo) { _dragTo = to; MakeRoom(); }
        }

        void StartDrag()
        {
            if (_list.Count == 1 && !CanTearLast) { DragWholeWindow(); return; }
            FrameworkElement c = _dragTab.Root;
            if (c.ActualWidth <= 0) { EndDrag(false); return; }
            _dragging = true;
            _dragFrom = _dragTo = _list.IndexOf(_dragTab);
            _slot = c.ActualWidth + 2;
            _slideGoal.Clear();
            Panel.SetZIndex(c, 10);
            SlideOf(c).BeginAnimation(TranslateTransform.XProperty, null);
        }

        /// <summary>The other tabs slide aside to leave a gap where the dragged one would go.</summary>
        void MakeRoom()
        {
            for (int i = 0; i < _list.Count; i++)
            {
                StripTab t = _list[i];
                if (t == _dragTab) continue;
                double goal = 0;
                if (!_torn)
                {
                    if (_dragFrom < _dragTo && i > _dragFrom && i <= _dragTo) goal = -_slot;
                    else if (_dragTo < _dragFrom && i >= _dragTo && i < _dragFrom) goal = _slot;
                }
                double cur;
                if (_slideGoal.TryGetValue(t, out cur) && cur == goal) continue;
                _slideGoal[t] = goal;
                Glide(SlideOf(t.Root), goal);
            }
        }

        /// <summary>Out of the strip (a copy of the tab follows the pointer) or back into it.</summary>
        void Tear(bool torn)
        {
            _torn = torn;
            FrameworkElement c = _dragTab.Root;
            if (torn)
            {
                c.Opacity = 0.35;
                Glide(SlideOf(c), 0);
                _dragTo = _dragFrom;
                ShowGhost();
            }
            else
            {
                c.Opacity = 1;
                SlideOf(c).BeginAnimation(TranslateTransform.XProperty, null);
                HideGhost();
            }
            MakeRoom();
        }

        void Tab_Up(object sender, MouseButtonEventArgs e)
        {
            if (_dragTab == null || TabOf(sender) != _dragTab) return;
            e.Handled = true;
            EndDrag(true);
        }

        void Tab_LostCapture(object sender, MouseEventArgs e)
        {
            if (_dragTab != null && TabOf(sender) == _dragTab && !_releasing) EndDrag(false);
        }

        bool _releasing;

        /// <summary>The drag is over: drop = do it, otherwise everything goes back.</summary>
        void EndDrag(bool drop)
        {
            StripTab t = _dragTab;
            if (t == null) return;
            try { Finish(t, drop); }
            finally { if (PressEnded != null) PressEnded(t); }
        }

        void Finish(StripTab t, bool drop)
        {
            bool dragging = _dragging, torn = _torn;
            _dragTab = null;
            _dragging = _torn = false;
            if (_edgeTimer != null) _edgeTimer.Stop();
            if (t.Root.IsMouseCaptured) { _releasing = true; t.Root.ReleaseMouseCapture(); _releasing = false; }
            HideGhost();
            if (!dragging) return;

            Point screen = CursorScreen();
            t.Root.Opacity = 1;
            Panel.SetZIndex(t.Root, 0);
            if (drop && torn)
            {
                ResetSlides();
                TabStrip other = StripAt(screen);
                if (other != null && other != this) { if (MovedTo != null) MovedTo(t, other, other.IndexAt(screen)); return; }
                if (other == this) return;          // let go on its own window, off the strip: nothing changes
                if (TornOut != null) TornOut(t, screen);
                return;
            }
            Move(t, drop ? _dragTo : _dragFrom);
        }

        void ResetSlides()
        {
            _slideGoal.Clear();
            foreach (StripTab s in _list) ResetOne(s);
        }

        static void ResetOne(StripTab s)
        {
            TranslateTransform tt = SlideOf(s.Root);
            tt.BeginAnimation(TranslateTransform.XProperty, null);
            tt.X = 0;
            Panel.SetZIndex(s.Root, 0);
            s.Root.Opacity = 1;
        }

        static TranslateTransform SlideOf(FrameworkElement c)
        {
            var tt = c.RenderTransform as TranslateTransform;
            if (tt == null || tt.IsFrozen) { tt = new TranslateTransform(); c.RenderTransform = tt; }
            return tt;
        }

        static void Glide(TranslateTransform tt, double to, double? from = null)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(SlideMillis)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            if (from.HasValue) a.From = from.Value;
            tt.BeginAnimation(TranslateTransform.XProperty, a);
        }

        /// <summary>
        /// The only tab: dragging it moves the window (Windows' own move, so it's smooth and snaps). Let go over another strip and the tab
        /// joins that window (MovedTo - the app closes the empty one).
        /// </summary>
        void DragWholeWindow()
        {
            StripTab t = _dragTab;
            _dragTab = null;
            if (t.Root.IsMouseCaptured) { _releasing = true; t.Root.ReleaseMouseCapture(); _releasing = false; }
            try
            {
                Window w = Window.GetWindow(this);
                if (w == null || w.WindowState == WindowState.Maximized) return;
                try { w.DragMove(); } catch (InvalidOperationException) { return; }   // returns when the button is let go
                Point screen = CursorScreen();
                TabStrip other = StripAt(screen, true);
                if (other != null && other != this && MovedTo != null) MovedTo(t, other, other.IndexAt(screen));
            }
            finally { if (PressEnded != null) PressEnded(t); }
        }

        // ---- the loose tab following the pointer ----
        void ShowGhost()
        {
            if (_ghost == null) _ghost = new Popup { AllowsTransparency = true, Placement = PlacementMode.AbsolutePoint, IsHitTestVisible = false, Focusable = false };
            var row = new DockPanel { Margin = new Thickness(10, 0, 10, 0) };
            if (_dragTab.Icon != null)
            {
                var icon = new Image { Source = _dragTab.Icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) };
                DockPanel.SetDock(icon, Dock.Left);
                row.Children.Add(icon);
            }
            var title = new TextBlock { Text = _dragTab.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.ForegroundProperty, "vx.Fg");
            row.Children.Add(title);
            var card = new Border
            {
                Width = Math.Max(90, _slot - 2), Height = _tabHeight, Child = row, Margin = new Thickness(8), BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 }
            };
            TextElement.SetFontFamily(card, TextElement.GetFontFamily(this));
            TextElement.SetFontSize(card, TextElement.GetFontSize(this));
            card.SetResourceReference(Border.BackgroundProperty, "vx.Popup");
            card.SetResourceReference(Border.BorderBrushProperty, "vx.Accent");
            card.SetResourceReference(Border.CornerRadiusProperty, "vx.MenuRadius");
            _ghost.Child = card;
            _ghost.PlacementTarget = this;
            MoveGhost();
            _ghost.IsOpen = true;
        }

        void MoveGhost()
        {
            if (_ghost == null) return;
            Point screen = CursorScreen();
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            // the copy sits under the pointer where the tab was held
            _ghost.HorizontalOffset = screen.X / dpi.DpiScaleX - (_pressAt.X - _dragFrom * _slot) - 8;
            _ghost.VerticalOffset = screen.Y / dpi.DpiScaleY - _tabHeight / 2 - 8;
        }

        void HideGhost() { if (_ghost != null) _ghost.IsOpen = false; }

        /// <summary>Where the pointer is (screen pixels).</summary>
        static Point CursorScreen()
        {
            POINT p;
            GetCursorPos(out p);
            return new Point(p.X, p.Y);
        }

        /// <summary>Where the dragged tab's left edge was, from the pointer (DIPs) - a new window puts its strip there.</summary>
        public double GrabOffset { get { return _pressAt.X - _dragFrom * _slot; } }

        // =================================================================
        // Strips under a point
        // =================================================================
        /// <summary>
        /// The strip a tab let go at this screen point belongs to: one whose tab row is there, else (not stripOnly) the strip of the window
        /// there (the top one, or one it owns - vTerm's text window). null = open desktop / another app.
        /// </summary>
        static TabStrip StripAt(Point screen, bool stripOnly = false)
        {
            foreach (TabStrip s in Live.ToList())
            {
                if (!s.IsVisible || PresentationSource.FromVisual(s) == null) continue;
                Window w = Window.GetWindow(s);
                if (w != null && w.WindowState == WindowState.Minimized) continue;
                Point p = s.PointFromScreen(screen);
                if (p.X >= 0 && p.X <= s.ActualWidth && p.Y >= -10 && p.Y <= s.ActualHeight + 10) return s;
            }
            if (stripOnly) return null;
            IntPtr under = WindowFromPoint(new POINT { X = (int)screen.X, Y = (int)screen.Y });
            IntPtr root = under != IntPtr.Zero ? GetAncestor(under, 2 /* GA_ROOT */) : IntPtr.Zero;
            if (root == IntPtr.Zero) return null;
            foreach (TabStrip s in Live.ToList())
            {
                if (!s.IsVisible) continue;
                Window w = Window.GetWindow(s);
                IntPtr h = w != null ? new WindowInteropHelper(w).Handle : IntPtr.Zero;
                if (h == root || (s.OwnsWindow != null && s.OwnsWindow(root))) return s;
            }
            return null;
        }

        /// <summary>The place a tab dropped at this screen point takes (-1 = after the last, when it's off the tab row).</summary>
        public int IndexAt(Point screen)
        {
            Point p = _panel.PointFromScreen(screen);
            if (p.Y < -12 || p.Y > _panel.ActualHeight + 12) return -1;
            double slot = Slot;
            return Math.Max(0, Math.Min(_list.Count, (int)Math.Round(p.X / slot)));
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    }
}
