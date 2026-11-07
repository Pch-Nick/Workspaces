using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Workspaces.Tests
{
    public class UpdateServiceTests
    {
        static readonly Version Current = new Version(1, 3, 0, 0);
        const string Feed = "https://updates.test.invalid/feed.json"; // тестовый адрес, обращений в сеть нет

        static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        static UpdateService Svc(Func<HttpRequestMessage, HttpResponseMessage> h, string pem = "")
        {
            var cfg = new AppConfig { UpdateFeedUrl = Feed, UpdatePublicKeyPem = pem };
            return new UpdateService(cfg, Current, new FakeHandler(h));
        }

        [Theory]
        [InlineData("")]
        [InlineData("http://insecure.test.invalid/feed.json")]
        public async Task NotConfigured_WhenFeedEmptyOrNotHttps(string url)
        {
            var svc = new UpdateService(new AppConfig { UpdateFeedUrl = url }, Current,
                new FakeHandler(r => { throw new Exception("не должно быть запроса"); }));
            Assert.False(svc.IsConfigured);
            Assert.Equal(UpdateState.NotConfigured, (await svc.CheckAsync()).State);
        }

        [Fact]
        public void ParseFeed_Valid_AndDropsNonHttpsLinks()
        {
            UpdateInfo i; string err;
            Assert.True(UpdateService.TryParseFeed(
                "{\"version\":\"v1.4.0\",\"notes\":[\"a\",\"b\"],\"downloadUrl\":\"http://x/y.exe\",\"pageUrl\":\"https://x/p\"}", out i, out err));
            Assert.Equal(new Version(1, 4, 0), i.Version);
            Assert.Equal(2, i.Notes.Count);
            Assert.Equal("", i.DownloadUrl);
            Assert.Equal("https://x/p", i.PageUrl);
        }

        [Fact]
        public void ParseFeed_GitHubRelease_ExtractsVersionNotesAndAssets()
        {
            string json = "{\"tag_name\":\"v1.4.0\",\"html_url\":\"https://github.com/o/r/releases/tag/v1.4.0\","
                + "\"body\":\"## Changes\\n- Fixed A\\n- Added B\","
                + "\"assets\":[{\"name\":\"Workspaces-Free-Setup-1.4.0.exe\",\"browser_download_url\":\"https://github.com/o/r/releases/download/v1.4.0/s.exe\",\"digest\":\"sha256:ABCDEF\"},"
                + "{\"name\":\"Workspaces-Free-Setup-1.4.0.exe.sig\",\"browser_download_url\":\"https://github.com/o/r/releases/download/v1.4.0/s.exe.sig\"}]}";
            UpdateInfo i; string err;
            Assert.True(UpdateService.TryParseFeed(json, out i, out err));
            Assert.Equal(new Version(1, 4, 0), i.Version);
            Assert.Contains("Fixed A", i.Notes);
            Assert.EndsWith("s.exe", i.DownloadUrl);
            Assert.Equal("ABCDEF", i.Sha256);
            Assert.EndsWith("s.exe.sig", i.SignatureUrl);
        }

        [Fact]
        public async Task Check_NoReleases404_IsErrorWithMessage()
        {
            var svc = Svc(r => Json("{}", HttpStatusCode.NotFound));
            var res = await svc.CheckAsync();
            Assert.Equal(UpdateState.Error, res.State);
            Assert.False(string.IsNullOrEmpty(res.Message));
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("{\"version\":\"abc\"}")]
        [InlineData("{}")]
        public void ParseFeed_Invalid_ReturnsFalse(string json)
        {
            UpdateInfo i; string err;
            Assert.False(UpdateService.TryParseFeed(json, out i, out err));
        }

        [Theory]
        [InlineData("1.4.0", UpdateState.Available)]
        [InlineData("1.3.1", UpdateState.Available)]
        [InlineData("1.3.0", UpdateState.UpToDate)]
        [InlineData("1.2.0", UpdateState.UpToDate)]
        public async Task Check_ComparesVersions(string remote, UpdateState expected)
        {
            var svc = Svc(r => Json("{\"version\":\"" + remote + "\"}"));
            Assert.Equal(expected, (await svc.CheckAsync()).State);
        }

        [Fact]
        public async Task Check_NoInternet_IsOffline()
        {
            var svc = Svc(r => { throw new HttpRequestException("no network"); });
            Assert.Equal(UpdateState.Offline, (await svc.CheckAsync()).State);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.NotFound)]
        public async Task Check_ServerErrors_AreReportedAsErrors(HttpStatusCode code)
        {
            var svc = Svc(r => Json("oops", code));
            var res = await svc.CheckAsync();
            Assert.Equal(UpdateState.Error, res.State);
            Assert.False(string.IsNullOrEmpty(res.Message));
        }

        [Fact]
        public async Task Check_BrokenFeed_IsError()
        {
            var svc = Svc(r => Json("<html>"));
            Assert.Equal(UpdateState.Error, (await svc.CheckAsync()).State);
        }

        // ---- проверка подлинности файла
        static void MakeKeyAndFile(string dir, out string pem, out string path, out string shaHex, out string sig)
        {
            using (var rsa = RSA.Create(2048))
            {
                pem = rsa.ExportSubjectPublicKeyInfoPem();
                path = System.IO.Path.Combine(dir, "setup.bin");
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes("installer-bytes-" + Guid.NewGuid()));
                byte[] hash;
                using (var fs = File.OpenRead(path)) hash = SHA256.HashData(fs);
                shaHex = Convert.ToHexString(hash).ToLowerInvariant();
                sig = Convert.ToBase64String(rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
        }

        [Fact]
        public void VerifyFile_AcceptsGood_RejectsTampered_AndWrongKey()
        {
            using (var t = new TempDir())
            {
                string pem, path, sha, sig, err;
                MakeKeyAndFile(t.Path, out pem, out path, out sha, out sig);
                Assert.True(UpdateService.VerifyFile(path, sha, sig, pem, out err));

                File.AppendAllText(path, "x");
                Assert.False(UpdateService.VerifyFile(path, sha, sig, pem, out err));

                string pem2, p2, s2, g2;
                MakeKeyAndFile(t.Path, out pem2, out p2, out s2, out g2);
                string pemOther;
                using (var other = RSA.Create(2048)) pemOther = other.ExportSubjectPublicKeyInfoPem();
                Assert.False(UpdateService.VerifyFile(p2, s2, g2, pemOther, out err));
            }
        }

        [Fact]
        public async Task Download_VerifiedFile_IsKept_BadHash_IsDeleted_NoKey_IsRefused()
        {
            using (var t = new TempDir())
            {
                string pem, path, sha, sig;
                MakeKeyAndFile(t.Path, out pem, out path, out sha, out sig);
                byte[] bytes = File.ReadAllBytes(path);
                Func<HttpRequestMessage, HttpResponseMessage> serve = r =>
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                string dest = System.IO.Path.Combine(t.Path, "dl");

                var good = new UpdateInfo { DownloadUrl = "https://dl.test.invalid/setup.bin", Sha256 = sha, Signature = sig };
                var ok = await Svc(serve, pem).DownloadAsync(good, dest, null);
                Assert.True(ok.Ok);
                Assert.True(File.Exists(ok.Path));

                string dest2 = System.IO.Path.Combine(t.Path, "dl2");
                var bad = new UpdateInfo { DownloadUrl = "https://dl.test.invalid/setup.bin", Sha256 = new string('0', 64), Signature = sig };
                var fail = await Svc(serve, pem).DownloadAsync(bad, dest2, null);
                Assert.False(fail.Ok);
                Assert.Empty(Directory.GetFiles(dest2));

                var noKey = await Svc(serve, "").DownloadAsync(good, System.IO.Path.Combine(t.Path, "dl3"), null);
                Assert.False(noKey.Ok);
            }
        }
    }
}
