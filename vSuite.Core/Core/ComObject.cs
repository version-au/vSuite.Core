using System;

namespace vSuite
{
    /// <summary>
    /// Making a Windows COM object safely when more than one part of the app uses it.
    /// Some objects (the desktop wallpaper one) are one per process: every CoCreateInstance hands back the same object, and .NET then
    /// reuses the wrapper the first caller made - typed as that caller's own [ComImport] class. A second part doing
    /// <c>(IMine)new MyClass()</c> fails with an InvalidCastException ("Unable to cast DesktopWallpaperClass to DesktopWallpaperClass").
    /// Made by its CLSID and cast straight to the interface (which asks the object itself), it works whoever made it first.
    /// </summary>
    public static class ComObject
    {
        public static T Create<T>(Guid clsid) where T : class
        {
            Type t = Type.GetTypeFromCLSID(clsid, true);
            return (T)Activator.CreateInstance(t);
        }
    }
}
