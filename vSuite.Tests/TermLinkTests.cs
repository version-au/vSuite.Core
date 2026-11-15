using System;
using System.Threading;
using vSuite;
using Xunit;

namespace vSuite.Tests
{
    public class TermLinkTests
    {
        [Fact]
        public void RequestRoundTrips()
        {
            var r = new TermRequest { Cmd = "open-host", X = -1920, Y = 40, W = 1200, H = 500, FromTop = false, Attached = true, Radius = 12,
                                      BorderColor = "#A689B4FA", Blur = "Acrylic", Host = "10.0.0.5", Name = "pve", User = "root",
                                      Remote = "docker exec -it 'a b' sh", Args = new[] { "--dir", "C:\\x y" } };
            TermRequest back = TermLink.Decode(TermLink.Encode(r));
            Assert.Equal("open-host", back.Cmd);
            Assert.Equal(-1920, back.X);
            Assert.False(back.FromTop);
            Assert.Equal("Acrylic", back.Blur);
            Assert.Equal("docker exec -it 'a b' sh", back.Remote);
            Assert.Equal(new[] { "--dir", "C:\\x y" }, back.Args);
        }

        [Fact]
        public void BadLineIsNull()
        {
            Assert.Null(TermLink.Decode("not json"));
        }

        [Fact]
        public void PipeNameFollowsTheSettingsFolder()
        {
            Assert.Equal(TermLink.PipeName(@"C:\Users\a\AppData\Roaming\vTerm"), TermLink.PipeName(@"c:\users\a\appdata\roaming\VTERM"));
            Assert.NotEqual(TermLink.PipeName(@"C:\one"), TermLink.PipeName(@"C:\two"));
            Assert.StartsWith("vTerm-", TermLink.PipeName(@"C:\one"));
        }

        [Fact]
        public void SendGetsTheAnswer()
        {
            string pipe = "vTerm-test-" + Guid.NewGuid().ToString("N");
            using (var stop = new CancellationTokenSource())
            {
                TermRequest got = null;
                TermLink.Serve(pipe, r => { got = r; return "shown"; }, stop.Token);
                string answer = TermLink.Send(pipe, new TermRequest { Cmd = "toggle", Profile = "PowerShell 7" }, 3000);
                Assert.Equal("shown", answer);
                Assert.Equal("PowerShell 7", got.Profile);
                // a second request on the same pipe works too
                Assert.Equal("shown", TermLink.Send(pipe, new TermRequest { Cmd = "hide" }, 3000));
                stop.Cancel();
            }
        }

        [Fact]
        public void NobodyListeningIsNull()
        {
            Assert.Null(TermLink.Send("vTerm-nobody-" + Guid.NewGuid().ToString("N"), new TermRequest(), 300));
        }
    }
}
