namespace vSuite.Ui
{
    /// <summary>
    /// A window that shows the vShell theme: the library's dialogs (Ask, Options, the update notice) take its colours and glass from the
    /// window that opened them.
    /// </summary>
    public interface IThemedWindow
    {
        /// <summary>The theme it shows now (null before the first one is applied).</summary>
        Theme CurrentTheme { get; }
        /// <summary>The window is see-through (the theme's opacity over the wallpaper glass); false = solid.</summary>
        bool Blurred { get; }
    }
}
