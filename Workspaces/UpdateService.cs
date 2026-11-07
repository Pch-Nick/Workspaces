using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Workspaces
{
    public enum UpdateState { NotChecked, NotConfigured, Checking, UpToDate, Available, Offline, Error }

    public class UpdateInfo
    {
        public Version Version;
        public string VersionText = "";
        public string DownloadUrl = "";
        public string PageUrl = "";
        public string Sha256 = "";
        public string Signature = "";
        public string SignatureUrl = "";
        public List<string> Notes = new List<string>();
    }

    public class UpdateCheckResult
    {
        public UpdateState State = UpdateState.NotChecked;
        public UpdateInfo Info;
        public string Message = "";
    }

    public class DownloadResult
    {
        public bool Ok;
        public string Path = "";
        public string Message = "";
    }

    // Источник обновлений задаётся в appconfig.json (updateFeedUrl). Если он пуст, сервис ничего не имитирует.
    //
    // Формат ленты (JSON по HTTPS):
    //   { "version": "1.4.0", "notes": ["..."], "downloadUrl": "https://...", "pageUrl": "https://...",
    //     "sha256": "<hex SHA-256 файла>", "signature": "<base64 RSA-SHA256 PKCS#1 v1.5 подпись файла>" }
    // Скачивание разрешено только при наличии публичного ключа (updatePublicKeyPem) и успешной проверке
    // и SHA-256, и подписи. Без ключа доступен лишь переход на страницу загрузки.
    public class UpdateService
    {
        readonly AppConfig cfg;
        readonly Version current;
        readonly HttpClient http;

        public UpdateService(AppConfig cfg, Version current, HttpMessageHandler handler = null)
        {
            this.cfg = cfg;
            this.current = current;
            http = handler == null ? new HttpClient() : new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Workspaces/" + current.ToString(3));
        }

        public bool IsConfigured { get { return AppConfig.IsHttps(cfg.UpdateFeedUrl); } }
        public bool CanVerifySignature { get { return !string.IsNullOrWhiteSpace(cfg.UpdatePublicKeyPem); } }

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default(CancellationToken))
        {
            if (!IsConfigured)
                return new UpdateCheckResult
                {
                    State = UpdateState.NotConfigured,
                    Message = L.T("Источник обновлений не настроен.", "The update source is not configured.")
                };
            try
            {
                using (var resp = await http.GetAsync(cfg.UpdateFeedUrl, ct))
                {
                    int code = (int)resp.StatusCode;
                    if (code == 404)
                        return Fail(L.T("В репозитории пока нет опубликованных релизов.", "No releases have been published in the repository yet."));
                    if (!resp.IsSuccessStatusCode)
                    {
                        Logger.Warn("Сервер обновлений ответил " + code);
                        return Fail(code >= 500
                            ? L.T("Сервер обновлений временно недоступен (" + code + ").", "The update server is temporarily unavailable (" + code + ").")
                            : L.T("Сервер обновлений ответил ошибкой " + code + ".", "The update server returned error " + code + "."));
                    }
                    if (resp.Content.Headers.ContentLength.HasValue && resp.Content.Headers.ContentLength.Value > 1024 * 1024)
                        return Fail(L.T("Лента обновлений слишком большая.", "The update feed is too large."));
                    string json = await resp.Content.ReadAsStringAsync();
                    UpdateInfo info; string err;
                    if (!TryParseFeed(json, out info, out err))
                    {
                        Logger.Warn("Неверная лента обновлений: " + err);
                        return Fail(L.T("Лента обновлений имеет неверный формат.", "The update feed has an invalid format."));
                    }
                    if (info.Version > current)
                        return new UpdateCheckResult { State = UpdateState.Available, Info = info };
                    return new UpdateCheckResult
                    {
                        State = UpdateState.UpToDate, Info = info,
                        Message = L.T("Установлена последняя версия.", "You have the latest version.")
                    };
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new UpdateCheckResult { State = UpdateState.Error, Message = L.T("Проверка отменена.", "Check cancelled.") };
            }
            catch (TaskCanceledException ex)
            {
                Logger.Warn("Тайм-аут проверки обновлений", ex);
                return Fail(L.T("Сервер не ответил вовремя.", "The server did not respond in time."));
            }
            catch (HttpRequestException ex)
            {
                Logger.Warn("Нет связи с сервером обновлений", ex);
                return new UpdateCheckResult
                {
                    State = UpdateState.Offline,
                    Message = L.T("Нет подключения к интернету или сервер недоступен.", "No internet connection or the server is unreachable.")
                };
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка проверки обновлений", ex);
                return Fail(L.T("Не удалось проверить обновления.", "Could not check for updates."));
            }
        }

        static UpdateCheckResult Fail(string msg)
        {
            return new UpdateCheckResult { State = UpdateState.Error, Message = msg };
        }

        public static bool TryParseFeed(string json, out UpdateInfo info, out string error)
        {
            info = null; error = null;
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) { error = "root is not an object"; return false; }
                    JsonElement tagEl;
                    if (r.TryGetProperty("tag_name", out tagEl)) return ParseGitHubRelease(r, out info, out error);
                    string ver = Str(r, "version").Trim();
                    if (ver.StartsWith("v") || ver.StartsWith("V")) ver = ver.Substring(1);
                    Version v;
                    if (!Version.TryParse(ver, out v)) { error = "bad version '" + ver + "'"; return false; }
                    info = new UpdateInfo
                    {
                        Version = v, VersionText = ver,
                        DownloadUrl = HttpsOrEmpty(Str(r, "downloadUrl")),
                        PageUrl = HttpsOrEmpty(Str(r, "pageUrl")),
                        Sha256 = Str(r, "sha256").Trim(),
                        Signature = Str(r, "signature").Trim()
                    };
                    JsonElement n;
                    if (r.TryGetProperty("notes", out n))
                    {
                        if (n.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var e in n.EnumerateArray())
                                if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                                    info.Notes.Add(e.GetString().Trim());
                        }
                        else if (n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()))
                            info.Notes.Add(n.GetString().Trim());
                    }
                    return true;
                }
            }
            catch (JsonException ex) { error = ex.Message; return false; }
        }

        // Формат GitHub Releases API (releases/latest): tag_name, body, html_url, assets[].
        // Контрольная сумма берётся из assets[].digest ("sha256:..."), подпись из соседнего ассета "<файл>.sig".
        static bool ParseGitHubRelease(JsonElement r, out UpdateInfo info, out string error)
        {
            info = null; error = null;
            string ver = Str(r, "tag_name").Trim();
            if (ver.StartsWith("v") || ver.StartsWith("V")) ver = ver.Substring(1);
            Version v;
            if (!Version.TryParse(ver, out v)) { error = "bad tag '" + ver + "'"; return false; }
            info = new UpdateInfo { Version = v, VersionText = ver, PageUrl = HttpsOrEmpty(Str(r, "html_url")) };

            foreach (var line in Str(r, "body").Split('\n'))
            {
                string t = line.Trim().TrimStart('-', '*', '#', ' ', '\t');
                if (t.Length > 0 && info.Notes.Count < 15) info.Notes.Add(t);
            }

            JsonElement assets;
            if (r.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array)
            {
                string exeName = null;
                foreach (var a in assets.EnumerateArray())
                {
                    string name = Str(a, "name");
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    string url = HttpsOrEmpty(Str(a, "browser_download_url"));
                    if (url.Length == 0) continue;
                    bool better = exeName == null || name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!better) continue;
                    exeName = name;
                    info.DownloadUrl = url;
                    string digest = Str(a, "digest");
                    info.Sha256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest.Substring(7) : "";
                }
                if (exeName != null)
                    foreach (var a in assets.EnumerateArray())
                        if (string.Equals(Str(a, "name"), exeName + ".sig", StringComparison.OrdinalIgnoreCase))
                            info.SignatureUrl = HttpsOrEmpty(Str(a, "browser_download_url"));
            }
            return true;
        }

        static string Str(JsonElement r, string name)
        {
            JsonElement e;
            return r.TryGetProperty(name, out e) && e.ValueKind == JsonValueKind.String ? (e.GetString() ?? "") : "";
        }

        static string HttpsOrEmpty(string url) { return AppConfig.IsHttps(url) ? url : ""; }

        // Скачивает установщик во временный файл, проверяет SHA-256 и подпись, и только затем переименовывает.
        public async Task<DownloadResult> DownloadAsync(UpdateInfo info, string destDir, IProgress<double> progress,
            CancellationToken ct = default(CancellationToken))
        {
            if (info == null || string.IsNullOrEmpty(info.DownloadUrl))
                return Bad(L.T("В ленте нет безопасной ссылки на загрузку (нужен HTTPS).", "The feed has no safe download link (HTTPS required)."));
            if (!CanVerifySignature)
                return Bad(L.T("Проверка подписи не настроена, автоматическая загрузка отключена. Откройте страницу загрузки.",
                               "Signature verification is not configured, automatic download is disabled. Open the download page."));
            if (string.IsNullOrEmpty(info.Signature) && !string.IsNullOrEmpty(info.SignatureUrl))
            {
                try { info.Signature = (await http.GetStringAsync(info.SignatureUrl)).Trim(); }
                catch (Exception ex) { Logger.Warn("Не удалось получить подпись обновления", ex); }
            }
            if (string.IsNullOrEmpty(info.Sha256) || string.IsNullOrEmpty(info.Signature))
                return Bad(L.T("В ленте нет контрольной суммы или подписи.", "The feed has no checksum or signature."));

            string part = null;
            try
            {
                Directory.CreateDirectory(destDir);
                string name = System.IO.Path.GetFileName(new Uri(info.DownloadUrl).LocalPath);
                foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                if (string.IsNullOrWhiteSpace(name)) name = "Workspaces-" + info.VersionText + "-setup.exe";
                string dest = System.IO.Path.Combine(destDir, name);
                part = dest + ".part";

                using (var resp = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    if (!resp.IsSuccessStatusCode)
                        return Bad(L.T("Сервер вернул ошибку " + (int)resp.StatusCode + " при загрузке.", "The server returned error " + (int)resp.StatusCode + " while downloading."));
                    long total = resp.Content.Headers.ContentLength ?? -1;
                    using (var src = await resp.Content.ReadAsStreamAsync())
                    using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buf = new byte[81920];
                        long done = 0; int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                        {
                            await fs.WriteAsync(buf, 0, n, ct);
                            done += n;
                            if (progress != null && total > 0) progress.Report((double)done / total);
                        }
                    }
                }
                string err;
                if (!VerifyFile(part, info.Sha256, info.Signature, cfg.UpdatePublicKeyPem, out err))
                {
                    Logger.Error("Проверка загруженного обновления не пройдена: " + err);
                    File.Delete(part);
                    return Bad(L.T("Файл не прошёл проверку подлинности и удалён.", "The file failed authenticity verification and was deleted."));
                }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(part, dest);
                return new DownloadResult { Ok = true, Path = dest, Message = L.T("Обновление загружено и проверено.", "The update was downloaded and verified.") };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Cleanup(part);
                return Bad(L.T("Загрузка отменена.", "Download cancelled."));
            }
            catch (HttpRequestException ex)
            {
                Logger.Warn("Ошибка сети при загрузке обновления", ex);
                Cleanup(part);
                return Bad(L.T("Нет подключения к интернету.", "No internet connection."));
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка загрузки обновления", ex);
                Cleanup(part);
                return Bad(L.T("Не удалось загрузить обновление.", "Could not download the update."));
            }
        }

        static void Cleanup(string part)
        {
            try { if (part != null && File.Exists(part)) File.Delete(part); }
            catch (Exception ex) { Logger.Warn("Не удалось удалить временный файл", ex); }
        }

        static DownloadResult Bad(string msg) { return new DownloadResult { Ok = false, Message = msg }; }

        public static bool VerifyFile(string path, string sha256Hex, string signatureBase64, string publicKeyPem, out string error)
        {
            error = null;
            try
            {
                byte[] hash;
                using (var fs = File.OpenRead(path))
                using (var sha = SHA256.Create()) hash = sha.ComputeHash(fs);
                string hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(hex, (sha256Hex ?? "").Trim().ToLowerInvariant(), StringComparison.Ordinal))
                { error = "SHA-256 mismatch"; return false; }
                using (var rsa = RSA.Create())
                {
                    rsa.ImportFromPem(publicKeyPem);
                    byte[] sig = Convert.FromBase64String(signatureBase64);
                    if (!rsa.VerifyHash(hash, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    { error = "signature mismatch"; return false; }
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
    }
}
