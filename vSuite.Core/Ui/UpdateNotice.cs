using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Effects;

namespace vSuite.Ui
{
    /// <summary>
    /// The update notice, as vShell's: "vXplorer 0.1.34 is available" with the release notes, Install and restart / What's new / Skip and a
    /// close (later) button, then the download's progress or why it failed. Every window shows the same notice (UpdateCenter's state);
    /// each app puts it where nothing native covers it (vXplorer over the file list, vView over the picture). Only inside the app - never
    /// in vShell's notifications.
    /// </summary>
    public sealed class UpdateNotice : Border
    {
        StackPanel _offer, _progress, _errorButtons;
        TextBlock _title, _have, _status;
        ScrollViewer _notes;
        ColumnDefinition _fill, _rest;
        Grid _track;
        string _notesFor;
        bool _built;

        /// <summary>A collapsed notice; it shows itself when UpdateCenter has news. Put it in a Grid cell over the window's content.</summary>
        public UpdateNotice()
        {
            Visibility = Visibility.Collapsed;
            Width = 340;
            Padding = new Thickness(16, 14, 16, 14);
            Margin = new Thickness(0, 0, 14, 14);
            BorderThickness = new Thickness(1.5);
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Bottom;
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.4 };
            SetResourceReference(BackgroundProperty, "vx.Popup");
            SetResourceReference(BorderBrushProperty, "vx.Accent");
            SetResourceReference(CornerRadiusProperty, "vx.MenuRadius");
            SetResourceReference(TextElement.ForegroundProperty, "vx.Fg");
            Panel.SetZIndex(this, 50);
            Action changed = () => Dispatcher.BeginInvoke(new Action(Draw));
            Loaded += (o, e) => { UpdateCenter.Changed -= changed; UpdateCenter.Changed += changed; Draw(); };
            Unloaded += (o, e) => UpdateCenter.Changed -= changed;
        }

        void Build()
        {
            _built = true;
            var col = new StackPanel();
            var head = new DockPanel();
            var close = new Button { Content = "", ToolTip = "Later", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, -4, -6, 0) };
            close.SetResourceReference(StyleProperty, "vx.TabClose");
            close.Click += (o, e) => UpdateCenter.Later();
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            _title = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            head.Children.Add(_title);
            col.Children.Add(head);
            _have = new TextBlock { Margin = new Thickness(0, 2, 0, 0) };
            _have.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            col.Children.Add(_have);
            _notes = new ScrollViewer { MaxHeight = 200, Margin = new Thickness(0, 8, -6, 0), Padding = new Thickness(0, 0, 6, 0),
                                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            col.Children.Add(_notes);

            _offer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            _offer.Children.Add(NoticeButton("Install and restart", true, () => UpdateCenter.Install(UpdateCenter.Available)));
            _offer.Children.Add(NoticeButton("What's new", false, () => UpdateCenter.OpenPage(UpdateCenter.Available)));
            _offer.Children.Add(NoticeButton("Skip", false, () => UpdateCenter.Skip(UpdateCenter.Available)));
            col.Children.Add(_offer);

            // progress (once Install is clicked)
            _progress = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
            _progress.Children.Add(_status);
            _track = new Grid { Height = 6, Margin = new Thickness(0, 7, 0, 0) };
            _fill = new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) };
            _rest = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            _track.ColumnDefinitions.Add(_fill);
            _track.ColumnDefinitions.Add(_rest);
            var trackBg = new Border { CornerRadius = new CornerRadius(3) };
            trackBg.SetResourceReference(BackgroundProperty, "vx.Track");
            Grid.SetColumnSpan(trackBg, 2);
            _track.Children.Add(trackBg);
            var fill = new Border { CornerRadius = new CornerRadius(3) };
            fill.SetResourceReference(BackgroundProperty, "vx.Accent");
            _track.Children.Add(fill);
            _progress.Children.Add(_track);
            _errorButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            _errorButtons.Children.Add(NoticeButton("Try again", true, () => UpdateCenter.Install(UpdateCenter.Available)));
            _errorButtons.Children.Add(NoticeButton("Close", false, UpdateCenter.Later));
            _progress.Children.Add(_errorButtons);
            col.Children.Add(_progress);
            Child = col;
        }

        static Button NoticeButton(string text, bool main, Action click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
            b.SetResourceReference(StyleProperty, "vx.TextButton");
            if (main)
            {
                b.SetResourceReference(Control.BackgroundProperty, "vx.Accent");
                b.SetResourceReference(Control.ForegroundProperty, "vx.OnAccent");
                b.FontWeight = FontWeights.SemiBold;
            }
            b.Click += (o, e) => click();
            return b;
        }

        void Draw()
        {
            UpdateInfo u = UpdateCenter.Available;
            bool show = UpdateCenter.NoticeVisible && u != null;
            if (!show) { Visibility = Visibility.Collapsed; return; }
            if (!_built) Build();
            Visibility = Visibility.Visible;
            _title.Text = App.Name + " " + UpdateCenter.VersionText(u) + (u.PreRelease ? " (pre-release)" : "") + " is available";
            _have.Text = "You have " + Updater.CurrentText + ".";
            if (_notesFor != u.Tag)
            {
                _notesFor = u.Tag;
                _notes.Content = NotesText(u.Notes ?? "");
                _notes.Visibility = string.IsNullOrWhiteSpace(u.Notes) ? Visibility.Collapsed : Visibility.Visible;
            }

            bool failed = !UpdateCenter.Installing && UpdateCenter.InstallError.Length > 0;
            bool busy = UpdateCenter.Installing || failed;
            _offer.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (!busy) return;
            double p = Math.Max(0, Math.Min(1, UpdateCenter.Progress));
            _fill.Width = new GridLength(Math.Max(0.0001, p), GridUnitType.Star);
            _rest.Width = new GridLength(Math.Max(0.0001, 1 - p), GridUnitType.Star);
            _track.Visibility = failed ? Visibility.Collapsed : Visibility.Visible;
            _errorButtons.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
            double mb = u.FileSize > 0 ? u.FileSize / 1048576.0 : 0;
            if (failed) _status.Text = "The update didn't install: " + UpdateCenter.InstallError;
            else if (UpdateCenter.Stage == "restart") _status.Text = "Installed - restarting " + App.Name + "…";
            else if (UpdateCenter.Stage == "install") _status.Text = "Checking and installing…";
            else _status.Text = "Downloading…  " + Math.Round(p * 100) + "%" + (mb > 0 ? "  (" + (p * mb).ToString("0.0") + " of " + mb.ToString("0.0") + " MB)" : "");
        }

        /// <summary>The release notes (GitHub markdown) as plain text: headings and **bold** lines in bold, "- " as bullets.</summary>
        public static UIElement NotesText(string md)
        {
            var panel = new StackPanel();
            foreach (string raw in md.Replace("\r", "").Split('\n'))
            {
                string text = raw.Trim();
                if (text.Length == 0) continue;
                bool head = false, bullet = false;
                if (text.StartsWith("#")) { text = text.TrimStart('#').Trim(); head = true; }
                if (text.StartsWith("**") && text.EndsWith("**") && text.Length > 4) { text = text.Substring(2, text.Length - 4); head = true; }
                if (text.StartsWith("- ") || text.StartsWith("* ")) { text = text.Substring(2); bullet = true; }
                text = text.Replace("**", "").Replace("`", "");
                panel.Children.Add(new TextBlock
                {
                    Text = bullet ? "•  " + text : text, TextWrapping = TextWrapping.Wrap, FontSize = 12,
                    FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal,
                    Margin = new Thickness(bullet ? 4 : 0, head && panel.Children.Count > 0 ? 8 : 2, 0, 0)
                });
            }
            return panel;
        }
    }
}
