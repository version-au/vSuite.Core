using System;
using System.Windows;

namespace vSuite.Ui
{
    /// <summary>
    /// The suite's look on one window, for an app whose own resources don't hold it (vShell - its bars and widgets keep their own drawing,
    /// so the styles must never go into its app-wide resources). vXplorer and vView merge Controls.xaml app-wide (Program) and put the theme
    /// there (Theme.Apply), so for them only Forms.xaml is added.
    /// </summary>
    public static class Look
    {
        public static readonly Uri ControlsUri = new Uri("pack://application:,,,/vSuite.Core;component/Ui/Controls.xaml", UriKind.Absolute);
        public static readonly Uri FormsUri = new Uri("pack://application:,,,/vSuite.Core;component/Ui/Forms.xaml", UriKind.Absolute);

        const string DressedKey = "vsuite.dressed";

        /// <summary>The app's resources already hold the suite's styles and brushes (vXplorer, vView).</summary>
        public static bool AppWide { get { return Application.Current != null && Application.Current.Resources.Contains("vx.Fg"); } }

        /// <summary>
        /// Give this window (or panel) the suite's styles - once - and the theme's vx.* brushes (again on every theme change). forms: also
        /// the form controls (Forms.xaml: buttons, switches, sliders, drop-downs, text boxes) - for settings / options windows.
        /// </summary>
        public static void Dress(FrameworkElement root, Theme theme, bool blur, bool forms)
        {
            if (!root.Resources.Contains(DressedKey))
            {
                root.Resources[DressedKey] = true;
                try
                {
                    var unused = System.IO.Packaging.PackUriHelper.UriSchemePack;   // pack:// URIs work even before (or without) an Application
                    if (!AppWide) root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = ControlsUri });
                    if (forms) root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = FormsUri });
                }
                catch (Exception ex) { Log.Write("Suite styles: " + ex.Message); }
            }
            if (!AppWide && theme != null) theme.Apply(root.Resources, blur);
        }
    }
}
