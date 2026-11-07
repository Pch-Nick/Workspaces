using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Workspaces.Tests
{
    public class TempDir : IDisposable
    {
        public readonly string Path;

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ws-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (Exception) { /* временные файлы тестов */ }
        }
    }

    public class FakeHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> respond;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) { this.respond = respond; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(respond(request));
        }
    }

    public static class Samples
    {
        public static AppData Data(params string[] names)
        {
            var d = new AppData();
            foreach (var n in names)
                d.Workspaces.Add(new Workspace
                {
                    Name = n, Color = "#6C8CFF",
                    Items = { new WinItem { Exe = @"C:\Windows\notepad.exe", Title = "t", ShowCmd = 1, Rect = new[] { 0, 0, 100, 100 } } }
                });
            return d;
        }
    }
}
