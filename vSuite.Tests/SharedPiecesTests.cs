using System;
using System.IO;
using vSuite;
using Xunit;

namespace vSuite.Tests
{
    public class ChangelogMarkTests
    {
        /// <summary>site-release.ps1 marks headings "## 0.1.5 · release" / "· pre-release": the update notice still finds the versions.</summary>
        [Fact]
        public void MarkedHeadingsAreStillVersions()
        {
            string log = "# App\n\n## 0.1.5 · release\n- five\n\n## 0.1.4 (Build 4 - experimental) · pre-release\n- four\n\n## 0.1.3\n- three\n";
            string between = Updater.ChangesBetween(log, new Version(0, 1, 3), new Version(0, 1, 5));
            Assert.Contains("## 0.1.5 · release", between);
            Assert.Contains("- four", between);
            Assert.DoesNotContain("three", between);
        }
    }

    public class SecretTests
    {
        [Fact]
        public void RoundTripsAndIsMarked()
        {
            string stored = Secret.Protect("hunter2");
            Assert.True(Secret.IsProtected(stored));
            Assert.DoesNotContain("hunter2", stored);
            Assert.Equal("hunter2", Secret.Unprotect(stored));
        }

        [Fact]
        public void PlainAndEmptyPassThrough()
        {
            Assert.Equal("", Secret.Protect(""));
            Assert.Equal("plain", Secret.Unprotect("plain"));
            // (a damaged "dpapi:" value gives "" too - not tested here: it writes to the log, which the log's own test is counting)
        }
    }

    /// <summary>Uses VTERM_DATA_DIR - kept in one class so its tests don't run alongside each other.</summary>
    public class VTermConnectionsTests
    {
        [Fact]
        public void ReadsSortsAndFollowsTheFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "vterm-conn-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string old = Environment.GetEnvironmentVariable("VTERM_DATA_DIR");
            Environment.SetEnvironmentVariable("VTERM_DATA_DIR", dir);
            try
            {
                Assert.False(VTermConnections.Exists);
                Assert.Empty(VTermConnections.All);
                Assert.Null(VTermConnections.Read<VTermConnection>());

                string file = Path.Combine(dir, "settings.json");
                File.WriteAllText(file, @"{ ""Font"": """", ""Connections"": [
                    { ""Id"": ""b"", ""Name"": ""pve"", ""Folder"": ""nodes"", ""Host"": ""10.0.0.10"", ""User"": ""root"", ""Auth"": ""Key"" },
                    { ""Id"": ""a"", ""Name"": ""docker-vm"", ""Folder"": ""vm's"", ""Host"": ""10.0.0.20"", ""Port"": 0 },
                    { ""Id"": ""c"", ""Name"": ""no address"", ""Host"": """" } ] }");
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-1));
                var all = VTermConnections.All;
                Assert.Equal(2, all.Count);                     // the one without an address is left out
                Assert.Equal("pve", all[0].Name);               // by folder: nodes before vm's
                Assert.Equal("root@10.0.0.10", all[0].Target);
                Assert.Equal(22, all[1].Port);                  // a bad port made 22
                Assert.Equal("Password", all[1].Auth);
                Assert.Equal("docker-vm", VTermConnections.Find("a").Name);

                File.WriteAllText(file, @"{ ""Connections"": [ { ""Id"": ""z"", ""Name"": ""nas"", ""Host"": ""10.0.0.30"" } ] }");
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
                Assert.Equal("nas", VTermConnections.All[0].Name);   // read again: the file changed
            }
            finally
            {
                Environment.SetEnvironmentVariable("VTERM_DATA_DIR", old);
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
