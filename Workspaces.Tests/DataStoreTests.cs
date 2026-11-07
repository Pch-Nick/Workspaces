using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace Workspaces.Tests
{
    public class DataStoreTests
    {
        [Fact]
        public void FirstRun_NoFile_ReturnsDefaults()
        {
            using (var t = new TempDir())
            {
                var s = new DataStore(t.Path);
                string notice;
                var d = s.Load(out notice);
                Assert.Empty(d.Workspaces);
                Assert.Null(notice);
            }
        }

        [Fact]
        public void SaveThenLoad_RoundTrips()
        {
            using (var t = new TempDir())
            {
                var s = new DataStore(t.Path);
                Assert.True(s.Save(Samples.Data("Work", "Study")));
                string notice;
                var d = s.Load(out notice);
                Assert.Equal(new[] { "Work", "Study" }, d.Workspaces.Select(w => w.Name).ToArray());
                Assert.Equal(DataStore.CurrentSchema, d.SchemaVersion);
                Assert.Null(notice);
            }
        }

        [Fact]
        public void Save_LeavesNoTempFile_AndKeepsPreviousVersionAsBak()
        {
            using (var t = new TempDir())
            {
                var s = new DataStore(t.Path);
                s.Save(Samples.Data("A"));
                s.Save(Samples.Data("A", "B"));
                Assert.False(File.Exists(s.MainPath + ".tmp"));
                Assert.True(File.Exists(s.MainPath + ".bak"));
            }
        }

        [Fact]
        public void Load_CorruptMain_RestoresFromBak_AndQuarantinesBadFile()
        {
            using (var t = new TempDir())
            {
                var s = new DataStore(t.Path);
                s.Save(Samples.Data("A"));
                s.Save(Samples.Data("A", "B"));
                File.WriteAllText(s.MainPath, "{ this is not json");
                string notice;
                var d = s.Load(out notice);
                Assert.Equal(new[] { "A" }, d.Workspaces.Select(w => w.Name).ToArray());
                Assert.NotNull(notice);
                Assert.Single(Directory.GetFiles(t.Path, "workspaces.corrupt-*.json"));
                // после восстановления основной файл снова читается
                string n2;
                Assert.Single(s.Load(out n2).Workspaces);
            }
        }

        [Fact]
        public void Load_CorruptMain_RestoresFromBackupFolder()
        {
            using (var t = new TempDir())
            {
                var s = new DataStore(t.Path);
                s.Save(Samples.Data("Saved"));
                Assert.NotNull(s.CreateBackup("manual"));
                File.Delete(s.MainPath + ".bak");
                File.WriteAllText(s.MainPath, "");
                string notice;
                var d = s.Load(out notice);
                Assert.Equal("Saved", d.Workspaces.Single().Name);
                Assert.NotNull(notice);
            }
        }

        [Fact]
        public void Load_CorruptEverything_StartsEmpty_WithoutThrowing()
        {
            using (var t = new TempDir())
            {
                var s = new DataStore(t.Path);
                File.WriteAllText(s.MainPath, "garbage");
                string notice;
                var d = s.Load(out notice);
                Assert.Empty(d.Workspaces);
                Assert.NotNull(notice);
                Assert.Single(Directory.GetFiles(t.Path, "workspaces.corrupt-*.json"));
            }
        }

        [Fact]
        public void Migration_OldFormat_IsNormalized_AndBackedUp()
        {
            using (var t = new TempDir())
            {
                var s = new DataStore(t.Path);
                File.WriteAllText(s.MainPath, @"{
  ""workspaces"": [
    { ""name"": ""Dup"", ""color"": ""bad"", ""items"": [ { ""exe"": ""C:\\a.exe"", ""rect"": [1,2] } ] },
    { ""name"": ""dup"", ""items"": null },
    { ""name"": """" }
  ],
  ""hide_delay"": 99999, ""island_height"": 5, ""lang"": ""xx"", ""theme"": ""neon""
}");
                string notice;
                var d = s.Load(out notice);
                Assert.Equal(3, d.Workspaces.Count);
                Assert.Equal(3, d.Workspaces.Select(w => w.Name.ToLowerInvariant()).Distinct().Count());
                Assert.True(Theme.IsHex(d.Workspaces[0].Color));
                Assert.Equal(4, d.Workspaces[0].Items[0].Rect.Length);
                Assert.Equal(1500, d.HideDelay);
                Assert.Equal(40, d.IslandHeight);
                Assert.Equal("ru", d.Lang);
                Assert.Equal("dark", d.Theme);
                Assert.Equal(DataStore.CurrentSchema, d.SchemaVersion);
                Assert.True(d.FirstRunDone); // у существующего пользователя приветствие не показываем
                Assert.NotEmpty(s.ListBackups());
            }
        }

        [Fact]
        public void Backups_AreRotated()
        {
            using (var t = new TempDir())
            {
                var s = new DataStore(t.Path);
                s.Save(Samples.Data("A"));
                for (int i = 0; i < DataStore.MaxBackups + 5; i++) { s.CreateBackup("manual"); Thread.Sleep(3); }
                Assert.True(s.ListBackups().Count <= DataStore.MaxBackups);
            }
        }

        [Fact]
        public void ImportFrom_BadFile_ReturnsNull()
        {
            using (var t = new TempDir())
            {
                var s = new DataStore(t.Path);
                string f = System.IO.Path.Combine(t.Path, "x.json");
                File.WriteAllText(f, "nope");
                Assert.Null(s.TryRead(f));
            }
        }
    }
}
