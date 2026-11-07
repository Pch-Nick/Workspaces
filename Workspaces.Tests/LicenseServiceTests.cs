using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Workspaces.Tests
{
    class MemStorage : ILicenseStorage
    {
        public LicenseRecord Record;
        public LicenseRecord Load() { return Record; }
        public void Save(LicenseRecord r) { Record = r; }
        public void Clear() { Record = null; }
        public string GetInstallId() { return "test-install"; }
    }

    class FakeProvider : ILicenseProvider
    {
        public bool Configured = true;
        public Func<LicenseResult> Verify = () => new LicenseResult { Ok = true, State = LicenseState.Pro, Token = "t" };
        public bool IsConfigured { get { return Configured; } }
        public Task<LicenseResult> VerifyAsync(LicenseRecord r, string id, CancellationToken ct) { return Task.FromResult(Verify()); }
        public Task<LicenseResult> ActivateAsync(string key, string id, CancellationToken ct)
        {
            return Task.FromResult(new LicenseResult { Ok = true, State = LicenseState.Pro, Token = "tok", Plan = "pro" });
        }
        public Task<LicenseResult> DeactivateAsync(LicenseRecord r, string id, CancellationToken ct)
        {
            return Task.FromResult(new LicenseResult { Ok = true, State = LicenseState.Free });
        }
    }

    public class LicenseServiceTests
    {
        static LicenseRecord Rec(DateTime? verified = null)
        {
            return new LicenseRecord { Key = "ABCD-1234", Token = "t", Plan = "pro", LastVerifiedUtc = verified };
        }

        [Fact]
        public async Task NoRecord_IsFree()
        {
            var svc = new LicenseService(new FakeProvider(), new MemStorage());
            var s = await svc.CheckAsync();
            Assert.Equal(LicenseState.Free, s.State);
        }

        [Fact]
        public async Task NotConfiguredProvider_WithRecord_IsUnverified_NotPro()
        {
            var svc = new LicenseService(new NotConfiguredLicenseProvider(), new MemStorage { Record = Rec() });
            var s = await svc.CheckAsync();
            Assert.Equal(LicenseState.Unverified, s.State);
            Assert.False(s.IsPro);
        }

        [Fact]
        public async Task ProviderOk_GivesPro_AndStoresVerificationTime()
        {
            var st = new MemStorage { Record = Rec() };
            var now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var svc = new LicenseService(new FakeProvider(), st, 7, () => now);
            var s = await svc.CheckAsync();
            Assert.Equal(LicenseState.Pro, s.State);
            Assert.Equal(now, st.Record.LastVerifiedUtc);
            Assert.Equal("••••1234", s.KeyHint);
        }

        [Fact]
        public async Task Offline_WithinGrace_StaysPro_ButNotAfter()
        {
            var now = new DateTime(2030, 1, 10, 0, 0, 0, DateTimeKind.Utc);
            var prov = new FakeProvider { Verify = () => new LicenseResult { Offline = true } };

            var within = new LicenseService(prov, new MemStorage { Record = Rec(now.AddDays(-3)) }, 7, () => now);
            Assert.Equal(LicenseState.Pro, (await within.CheckAsync()).State);

            var after = new LicenseService(prov, new MemStorage { Record = Rec(now.AddDays(-30)) }, 7, () => now);
            Assert.Equal(LicenseState.Unverified, (await after.CheckAsync()).State);
        }

        [Theory]
        [InlineData(LicenseState.Expired)]
        [InlineData(LicenseState.Invalid)]
        public async Task ServerSaysExpiredOrInvalid_IsReflected(LicenseState st)
        {
            var prov = new FakeProvider { Verify = () => new LicenseResult { State = st } };
            var svc = new LicenseService(prov, new MemStorage { Record = Rec(DateTime.UtcNow) });
            Assert.Equal(st, (await svc.CheckAsync()).State);
        }

        [Fact]
        public async Task ProviderThrows_DoesNotThrow_AndKeepsRecord()
        {
            var st = new MemStorage { Record = Rec() };
            var prov = new FakeProvider { Verify = () => { throw new InvalidOperationException("boom"); } };
            var svc = new LicenseService(prov, st);
            var s = await svc.CheckAsync();
            Assert.Equal(LicenseState.Unverified, s.State);
            Assert.NotNull(st.Record);
        }

        [Fact]
        public async Task Activate_WhenNotConfigured_ReportsNotConfigured_AndStaysFree()
        {
            var st = new MemStorage();
            var svc = new LicenseService(new NotConfiguredLicenseProvider(), st);
            var r = await svc.ActivateAsync("KEY");
            Assert.True(r.NotConfigured);
            Assert.False(r.Ok);
            Assert.Null(st.Record);
            Assert.Equal(LicenseState.Free, svc.Current.State);
        }

        [Fact]
        public async Task Activate_Then_Deactivate_RoundTrip()
        {
            var st = new MemStorage();
            var svc = new LicenseService(new FakeProvider(), st);
            var a = await svc.ActivateAsync("KEY-9999");
            Assert.True(a.Ok);
            Assert.Equal(LicenseState.Pro, svc.Current.State);
            Assert.NotNull(st.Record);
            var d = await svc.DeactivateAsync();
            Assert.True(d.Ok);
            Assert.Null(st.Record);
            Assert.Equal(LicenseState.Free, svc.Current.State);
        }
    }
}
