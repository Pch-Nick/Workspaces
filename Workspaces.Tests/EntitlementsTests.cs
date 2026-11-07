using System;
using System.Collections.Generic;
using Xunit;

namespace Workspaces.Tests
{
    public class EntitlementsTests : IDisposable
    {
        readonly Func<LicenseStatus> original = Entitlements.StatusProvider;
        readonly HashSet<Feature> originalFree = Entitlements.FreeFeatures;

        public EntitlementsTests()
        {
            // тесты ограничений проверяют разделение версий, поэтому временно убираем открытые функции
            Entitlements.FreeFeatures = new HashSet<Feature>();
        }

        public void Dispose()
        {
            Entitlements.StatusProvider = original;
            Entitlements.FreeFeatures = originalFree;
        }

        [Fact]
        public void CurrentFreeConfiguration_HasAllFeaturesAndNoLimit()
        {
            Entitlements.FreeFeatures = originalFree;
            SetState(LicenseState.Free);
            foreach (Feature f in Enum.GetValues(typeof(Feature))) Assert.True(Entitlements.Has(f));
            Assert.Equal(int.MaxValue, Entitlements.MaxWorkspaces);
            Assert.True(Entitlements.CanCreateWorkspace(100));
        }

        static void SetState(LicenseState st)
        {
            Entitlements.StatusProvider = () => new LicenseStatus { State = st };
        }

        [Fact]
        public void Free_AllowsThreeWorkspaces()
        {
            SetState(LicenseState.Free);
            Assert.Equal(3, Entitlements.MaxWorkspaces);
            Assert.True(Entitlements.CanCreateWorkspace(0));
            Assert.True(Entitlements.CanCreateWorkspace(2));
            Assert.False(Entitlements.CanCreateWorkspace(3));
            Assert.False(Entitlements.CanCreateWorkspace(7)); // уже созданные продолжают работать, новые нельзя
        }

        [Theory]
        [InlineData(LicenseState.Invalid)]
        [InlineData(LicenseState.Expired)]
        [InlineData(LicenseState.Unverified)]
        public void NonProStates_BehaveAsFree(LicenseState st)
        {
            SetState(st);
            Assert.False(Entitlements.IsPro);
            Assert.Equal(3, Entitlements.MaxWorkspaces);
        }

        [Fact]
        public void Free_UsesDefaultsForProSettings_WithoutErasingStoredValues()
        {
            SetState(LicenseState.Free);
            var d = new AppData { Theme = "light", Accent = "#FF0000", IslandHeight = 70, IslandOpacity = 50, Monitor = 1, AnimType = 2, IslandPos = 2 };
            Assert.Equal("dark", Eff.ThemeMode(d));
            Assert.Equal(Eff.DefaultAccent, Eff.Accent(d));
            Assert.Equal(58, Eff.IslandHeight(d));
            Assert.Equal(100, Eff.IslandOpacity(d));
            Assert.Equal(0, Eff.Monitor(d));
            Assert.Equal(0, Eff.AnimType(d));
            Assert.Equal(0, Eff.IslandPos(d));
            Assert.Equal("light", d.Theme);   // сохранённое значение не стёрто
            Assert.Equal(70, d.IslandHeight);
        }

        [Fact]
        public void Pro_UnlocksEverything()
        {
            SetState(LicenseState.Pro);
            Assert.True(Entitlements.Has(Feature.MultiMonitor));
            Assert.Equal(int.MaxValue, Entitlements.MaxWorkspaces);
            var d = new AppData { Theme = "light", Accent = "#FF0000", IslandHeight = 70 };
            Assert.Equal("light", Eff.ThemeMode(d));
            Assert.Equal("#FF0000", Eff.Accent(d));
            Assert.Equal(70, Eff.IslandHeight(d));
        }
    }
}
