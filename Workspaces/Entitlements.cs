using System;
using System.Collections.Generic;

namespace Workspaces
{
    // Все функции, которые зависят от версии. Сейчас в Free доступно только то, что не входит в список PRO.
    public enum Feature
    {
        MultiMonitor,          // выбор монитора для острова
        ThemeChoice,           // светлая и системная темы
        Personalization,       // цвета, акценты, шрифт, положение острова, оформление карточек
        IslandOpacity,         // прозрачность острова
        IslandSize,            // высота острова
        VisualEffects,         // зарезервировано
        AdvancedAnimations,    // выбор типа и скорости анимации
        UnlimitedWorkspaces,   // больше 3 рабочих сред
        Automation,            // зарезервировано
        CloudSync,             // зарезервировано
        AdvancedLaunch         // зарезервировано
    }

    public static class Entitlements
    {
        public const int FreeMaxWorkspaces = 3;

        // Источник статуса лицензии. Приложение подставляет LicenseService.Current.
        public static Func<LicenseStatus> StatusProvider = () => LicenseStatus.Free();

        // Функции, доступные без лицензии. Сейчас владелец открыл ВСЕ функции для бесплатной версии.
        // Чтобы снова разделить версии, уберите нужные значения из этого набора.
        public static HashSet<Feature> FreeFeatures = new HashSet<Feature>((Feature[])Enum.GetValues(typeof(Feature)));

        public static bool IsPro
        {
            get
            {
                var s = StatusProvider();
                return s != null && s.State == LicenseState.Pro;
            }
        }

        public static bool Has(Feature f)
        {
#if DEBUG
            // Только для отладочных сборок: проверка PRO-кода без лицензии. В Release не компилируется.
            if (Environment.GetEnvironmentVariable("WORKSPACES_DEV_PRO") == "1") return true;
#endif
            return FreeFeatures.Contains(f) || IsPro;
        }

        public static int MaxWorkspaces { get { return Has(Feature.UnlimitedWorkspaces) ? int.MaxValue : FreeMaxWorkspaces; } }

        public static bool CanCreateWorkspace(int currentCount) { return currentCount < MaxWorkspaces; }

        public static string Edition { get { return IsPro ? "Workspaces PRO" : "Workspaces Free"; } }
    }

    // Эффективные значения настроек: сохранённое значение действует, только если функция доступна.
    // Сами сохранённые значения не стираются, поэтому при переходе на PRO ничего не теряется.
    public static class Eff
    {
        public const string DefaultAccent = "#6C8CFF";
        public const string DefaultIslandBg = "#0B0B0D";

        public static string ThemeMode(AppData d)
        {
            if (d.Theme == "light" || d.Theme == "system") return Entitlements.Has(Feature.ThemeChoice) ? d.Theme : "dark";
            return "dark";
        }
        public static string Accent(AppData d)
        {
            return Entitlements.Has(Feature.Personalization) && Theme.IsHex(d.Accent) ? d.Accent : DefaultAccent;
        }
        public static string IslandBg(AppData d)
        {
            return Entitlements.Has(Feature.Personalization) && Theme.IsHex(d.IslandBg) ? d.IslandBg : DefaultIslandBg;
        }
        public static int IslandOpacity(AppData d) { return Entitlements.Has(Feature.IslandOpacity) ? d.IslandOpacity : 100; }
        public static int IslandHeight(AppData d) { return Entitlements.Has(Feature.IslandSize) ? d.IslandHeight : 58; }
        public static int IslandFont(AppData d) { return Entitlements.Has(Feature.Personalization) ? d.IslandFont : 13; }
        public static bool IslandNames(AppData d) { return !Entitlements.Has(Feature.Personalization) || d.IslandNames; }
        public static int IslandPos(AppData d) { return Entitlements.Has(Feature.Personalization) ? d.IslandPos : 0; }
        public static int Monitor(AppData d) { return Entitlements.Has(Feature.MultiMonitor) ? d.Monitor : 0; }
        public static int AnimType(AppData d) { return Entitlements.Has(Feature.AdvancedAnimations) ? d.AnimType : 0; }
        public static int AnimSpeed(AppData d) { return Entitlements.Has(Feature.AdvancedAnimations) ? d.AnimSpeed : 100; }
        public static int CardRadius(AppData d) { return Entitlements.Has(Feature.Personalization) ? d.CardRadius : 14; }
        public static int CardSize(AppData d) { return Entitlements.Has(Feature.Personalization) ? d.CardSize : 100; }
        public static int Density(AppData d) { return Entitlements.Has(Feature.Personalization) ? d.Density : 1; }
    }
}
