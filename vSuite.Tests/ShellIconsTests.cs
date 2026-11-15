using System;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using vSuite.Shell;
using Xunit;

namespace vSuite.Tests
{
    public class ShellIconsTests
    {
        /// <summary>Runs body on an STA thread with a Dispatcher, pumping until it has seen `count` images (or the time runs out).</summary>
        static ImageSource[] Collect(int count, Action<ShellIcons, Action<ImageSource>> body)
        {
            var got = new ImageSource[count];
            int n = 0;
            Exception err = null;
            var t = new Thread(() =>
            {
                try
                {
                    var icons = new ShellIcons(Dispatcher.CurrentDispatcher);
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                    timer.Tick += (s, e) => frame.Continue = false;
                    timer.Start();
                    body(icons, img => { if (n < count) got[n++] = img; if (n == count) frame.Continue = false; });
                    Dispatcher.PushFrame(frame);
                    icons.Dispose();
                }
                catch (Exception ex) { err = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (err != null) throw err;
            return got;
        }

        static string TempPicture()
        {
            string dir = Path.Combine(Path.GetTempPath(), "vSuite.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "pic.png");
            var px = new byte[64 * 64 * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = 200; px[i + 1] = 80; px[i + 2] = 40; px[i + 3] = 255; }
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, px, 64 * 4)));
            using (var fs = File.Create(file)) enc.Save(fs);
            return file;
        }

        [Fact]
        public void Thumbnail_arrives_made_and_then_from_the_cache()
        {
            string file = TempPicture();
            try
            {
                ImageSource[] first = Collect(1, (icons, done) => icons.RequestThumb(file, 48, null, done));
                Assert.NotNull(first[0]);
                ImageSource[] again = Collect(1, (icons, done) => icons.RequestThumb(file, 48, null, done));
                Assert.NotNull(again[0]);
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(file), true); } catch { } }
        }

        [Fact]
        public void A_file_with_no_thumbnail_still_gets_its_icon()
        {
            string dll = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
            ImageSource[] got = Collect(1, (icons, done) => icons.RequestThumb(dll, 48, null, done));
            Assert.NotNull(got[0]);
        }

        [Fact]
        public void A_flood_of_icons_all_arrive_in_batches()
        {
            string[] exts = { ".txt", ".zip", ".pdf", ".png", ".json", ".mp3" };
            ImageSource[] got = Collect(600, (icons, done) =>
            {
                for (int i = 0; i < 600; i++)
                    icons.Request(@"C:\nowhere\f" + i + exts[i % exts.Length], "ext:" + exts[i % exts.Length], null, done);
            });
            Assert.All(got, Assert.NotNull);
        }

        [Fact]
        public void Own_icons_arrive_on_the_icon_threads()
        {
            string[] exes = { "notepad.exe", "cmd.exe", "regedit.exe", "taskmgr.exe", "explorer.exe", "control.exe" };
            ImageSource[] got = Collect(exes.Length, (icons, done) =>
            {
                foreach (string e in exes)
                {
                    string p = Path.Combine(Environment.SystemDirectory, e);
                    if (!File.Exists(p)) p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), e);
                    icons.Request(p, null, null, done);
                }
            });
            Assert.All(got, Assert.NotNull);
        }
    }
}
