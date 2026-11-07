using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Workspaces
{
    public enum LicenseState { Free, Pro, Invalid, Expired, Unverified }

    public class LicenseStatus
    {
        public LicenseState State = LicenseState.Free;
        public string Plan = "";
        public DateTime? ExpiresUtc;
        public DateTime? LastVerifiedUtc;
        public string Message = "";
        public string KeyHint = "";

        public bool IsPro { get { return State == LicenseState.Pro; } }

        public static LicenseStatus Free(string message = "")
        {
            return new LicenseStatus { State = LicenseState.Free, Message = message };
        }
    }

    // Локальное состояние лицензии. Хранится зашифрованным (DPAPI, учётная запись пользователя).
    public class LicenseRecord
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("token")] public string Token { get; set; } = "";
        [JsonPropertyName("plan")] public string Plan { get; set; } = "";
        [JsonPropertyName("expires")] public DateTime? ExpiresUtc { get; set; }
        [JsonPropertyName("verified")] public DateTime? LastVerifiedUtc { get; set; }
    }

    public class LicenseResult
    {
        public bool Ok;
        public LicenseState State = LicenseState.Unverified;
        public string Message = "";
        public string Token = "";
        public string Plan = "";
        public DateTime? ExpiresUtc;
        public bool Offline;          // сервер недоступен (сеть, таймаут)
        public bool NotConfigured;    // сервис лицензирования не подключён
    }

    // Точка подключения реального сервера лицензий. Реализации пока нет, сервер не создаётся.
    public interface ILicenseProvider
    {
        bool IsConfigured { get; }
        Task<LicenseResult> VerifyAsync(LicenseRecord record, string installId, CancellationToken ct);
        Task<LicenseResult> ActivateAsync(string key, string installId, CancellationToken ct);
        Task<LicenseResult> DeactivateAsync(LicenseRecord record, string installId, CancellationToken ct);
    }

    public class NotConfiguredLicenseProvider : ILicenseProvider
    {
        static LicenseResult NC()
        {
            return new LicenseResult
            {
                NotConfigured = true,
                Message = L.T("Сервис лицензирования не подключён.", "The licensing service is not connected.")
            };
        }
        public bool IsConfigured { get { return false; } }
        public Task<LicenseResult> VerifyAsync(LicenseRecord r, string id, CancellationToken ct) { return Task.FromResult(NC()); }
        public Task<LicenseResult> ActivateAsync(string key, string id, CancellationToken ct) { return Task.FromResult(NC()); }
        public Task<LicenseResult> DeactivateAsync(LicenseRecord r, string id, CancellationToken ct) { return Task.FromResult(NC()); }
    }

    public interface ILicenseStorage
    {
        LicenseRecord Load();
        void Save(LicenseRecord record);
        void Clear();
        string GetInstallId();
    }

    public class DpapiLicenseStorage : ILicenseStorage
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Workspaces.License.v1");
        readonly string file, idFile;

        public DpapiLicenseStorage(string dir)
        {
            file = Path.Combine(dir, "license.dat");
            idFile = Path.Combine(dir, "install.id");
        }

        public LicenseRecord Load()
        {
            try
            {
                if (!File.Exists(file)) return null;
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(file), Entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<LicenseRecord>(Encoding.UTF8.GetString(plain));
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось прочитать локальное состояние лицензии", ex);
                return null;
            }
        }

        public void Save(LicenseRecord r)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(r)), Entropy, DataProtectionScope.CurrentUser);
            string tmp = file + ".tmp";
            File.WriteAllBytes(tmp, enc);
            if (File.Exists(file)) File.Replace(tmp, file, null, true); else File.Move(tmp, file);
        }

        public void Clear()
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (Exception ex) { Logger.Warn("Не удалось удалить состояние лицензии", ex); }
        }

        // Случайный идентификатор установки, не связан с оборудованием.
        public string GetInstallId()
        {
            try
            {
                if (File.Exists(idFile))
                {
                    string s = File.ReadAllText(idFile).Trim();
                    if (s.Length > 0) return s;
                }
                string id = Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(Path.GetDirectoryName(idFile));
                File.WriteAllText(idFile, id);
                return id;
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось сохранить идентификатор установки", ex);
                return "unknown";
            }
        }
    }

    // Проверка лицензии никогда не блокирует приложение: любой сбой даёт Free или Unverified,
    // а бесплатные функции и пользовательские данные не затрагиваются.
    public class LicenseService
    {
        readonly ILicenseProvider provider;
        readonly ILicenseStorage storage;
        readonly int graceDays;
        readonly Func<DateTime> now;

        public LicenseStatus Current { get; private set; }
        public event Action Changed;

        public LicenseService(ILicenseProvider provider, ILicenseStorage storage, int graceDays = 7, Func<DateTime> utcNow = null)
        {
            this.provider = provider;
            this.storage = storage;
            this.graceDays = graceDays;
            now = utcNow ?? (() => DateTime.UtcNow);
            Current = LicenseStatus.Free();
        }

        public bool ProviderConfigured { get { return provider.IsConfigured; } }
        public bool HasLocalLicense { get { return storage.Load() != null; } }

        void Set(LicenseStatus s)
        {
            Current = s;
            var h = Changed;
            if (h != null) h();
        }

        public async Task<LicenseStatus> CheckAsync(CancellationToken ct = default(CancellationToken))
        {
            LicenseRecord rec = null;
            try
            {
                rec = storage.Load();
                if (rec == null || string.IsNullOrEmpty(rec.Key)) { Set(LicenseStatus.Free()); return Current; }

                if (!provider.IsConfigured)
                {
                    Set(Make(LicenseState.Unverified, rec, L.T("Проверка лицензии пока недоступна.", "License verification is not available yet.")));
                    return Current;
                }
                var res = await provider.VerifyAsync(rec, storage.GetInstallId(), ct);
                Set(Apply(rec, res));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Error("Ошибка проверки лицензии", ex);
                Set(rec != null ? Make(LicenseState.Unverified, rec, ex.Message) : LicenseStatus.Free());
            }
            return Current;
        }

        LicenseStatus Apply(LicenseRecord rec, LicenseResult res)
        {
            if (res.Offline || res.NotConfigured)
            {
                bool inGrace = rec.LastVerifiedUtc.HasValue && !string.IsNullOrEmpty(rec.Token)
                               && (now() - rec.LastVerifiedUtc.Value).TotalDays <= graceDays
                               && (!rec.ExpiresUtc.HasValue || rec.ExpiresUtc.Value > now());
                if (inGrace) return Make(LicenseState.Pro, rec, L.T("Работает офлайн по последней успешной проверке.", "Working offline based on the last successful check."));
                return Make(LicenseState.Unverified, rec, res.Message);
            }
            if (res.State == LicenseState.Pro && res.Ok)
            {
                rec.Token = res.Token ?? rec.Token;
                rec.Plan = res.Plan ?? rec.Plan;
                rec.ExpiresUtc = res.ExpiresUtc;
                rec.LastVerifiedUtc = now();
                TrySave(rec);
                return Make(LicenseState.Pro, rec, res.Message);
            }
            if (res.State == LicenseState.Expired) return Make(LicenseState.Expired, rec, res.Message);
            if (res.State == LicenseState.Invalid) return Make(LicenseState.Invalid, rec, res.Message);
            return Make(LicenseState.Unverified, rec, res.Message);
        }

        void TrySave(LicenseRecord rec)
        {
            try { storage.Save(rec); }
            catch (Exception ex) { Logger.Error("Не удалось сохранить состояние лицензии", ex); }
        }

        static LicenseStatus Make(LicenseState state, LicenseRecord rec, string msg)
        {
            return new LicenseStatus
            {
                State = state, Plan = rec.Plan ?? "", ExpiresUtc = rec.ExpiresUtc, LastVerifiedUtc = rec.LastVerifiedUtc,
                Message = msg ?? "", KeyHint = Mask(rec.Key)
            };
        }

        public static string Mask(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return key.Length <= 4 ? "••••" : "••••" + key.Substring(key.Length - 4);
        }

        // Точка подключения для будущего экрана ввода ключа.
        public async Task<LicenseResult> ActivateAsync(string key, CancellationToken ct = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(key))
                return new LicenseResult { State = LicenseState.Invalid, Message = L.T("Введите ключ.", "Enter a key.") };
            if (!provider.IsConfigured) return await provider.ActivateAsync(key, storage.GetInstallId(), ct);
            try
            {
                var res = await provider.ActivateAsync(key.Trim(), storage.GetInstallId(), ct);
                if (res.Ok && res.State == LicenseState.Pro)
                {
                    var rec = new LicenseRecord
                    {
                        Key = key.Trim(), Token = res.Token, Plan = res.Plan, ExpiresUtc = res.ExpiresUtc, LastVerifiedUtc = now()
                    };
                    storage.Save(rec);
                    Set(Make(LicenseState.Pro, rec, res.Message));
                }
                return res;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Error("Ошибка активации лицензии", ex);
                return new LicenseResult { Offline = true, Message = ex.Message };
            }
        }

        public async Task<LicenseResult> DeactivateAsync(CancellationToken ct = default(CancellationToken))
        {
            var rec = storage.Load();
            if (rec == null) return new LicenseResult { Ok = true, State = LicenseState.Free };
            if (!provider.IsConfigured) return await provider.DeactivateAsync(rec, storage.GetInstallId(), ct);
            try
            {
                var res = await provider.DeactivateAsync(rec, storage.GetInstallId(), ct);
                if (res.Ok)
                {
                    storage.Clear();
                    Set(LicenseStatus.Free());
                }
                return res;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Error("Ошибка деактивации лицензии", ex);
                return new LicenseResult { Offline = true, Message = ex.Message };
            }
        }
    }
}
