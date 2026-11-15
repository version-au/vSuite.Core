using System;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;

namespace vSuite.Tests
{
    public class ComObjectTests
    {
        // the desktop wallpaper object, declared here as an app would (its own class and interface)
        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAppWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
        }

        [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
        class AppWallpaperClass { }

        static readonly Guid Clsid = new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

        static void OnSta(Action a)
        {
            Exception err = null;
            var t = new Thread(() => { try { a(); } catch (Exception ex) { err = ex; } });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (err != null) throw new Exception("on the STA thread: " + err.Message, err);
        }

        [Fact]
        public void Create_works_after_the_app_made_the_same_object_through_its_own_class()
        {
            OnSta(() =>
            {
                // the app first (vShell's wallpaper code), then the library (Glass / Wallpaper Theme) - the order vShell hit
                var first = (IAppWallpaper)new AppWallpaperClass();
                first.GetMonitorDevicePathCount();
                var second = ComObject.Create<IAppWallpaper>(Clsid);
                Assert.NotNull(second);
                Assert.Equal(first.GetMonitorDevicePathCount(), second.GetMonitorDevicePathCount());
                // and the library's own lookups still find a picture
                WallpaperColor.FindWallpaper();
            });
        }

        [Fact]
        public void Create_works_twice()
        {
            OnSta(() =>
            {
                var a = ComObject.Create<IAppWallpaper>(Clsid);
                var b = ComObject.Create<IAppWallpaper>(Clsid);
                Assert.Equal(a.GetMonitorDevicePathCount(), b.GetMonitorDevicePathCount());
            });
        }
    }
}
