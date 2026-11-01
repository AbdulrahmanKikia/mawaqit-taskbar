using System.Globalization;
using Microsoft.UI.Xaml;

namespace Mawaqit.WinUI.Helpers;

internal sealed class LocaleManager
{
    public const string System = "system";

    public static LocaleManager Default { get; } = new();

    public event EventHandler? CurrentChanged;

    public string Preference { get; private set; } = System;

    public string Resolved
    {
        get
        {
            if (Preference is "en" or "fr" or "ar") return Preference;
            var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return two is "fr" or "ar" ? two : "en";
        }
    }

    public CultureInfo CurrentCulture => Resolved switch
    {
        "fr" => CultureInfo.GetCultureInfo("fr-CA"),
        "ar" => CultureInfo.GetCultureInfo("ar"),
        _ => CultureInfo.GetCultureInfo("en")
    };

    public string Current => Resolved;
    public bool IsRtl => Resolved == "ar";
    public FlowDirection Flow => IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string Get(string key)
    {
        var pack = Catalog.TryGetValue(Resolved, out var map) ? map : Catalog["en"];
        if (pack.TryGetValue(key, out var s)) return s;
        if (Catalog["en"].TryGetValue(key, out var en)) return en;
        return key;
    }

    public string Prayer(string kind) => Get("Prayer." + kind);

    internal static readonly string[] MapKeys =
    [
        "Map.Title", "Map.Hint", "Map.Filter", "Map.Placeholder", "Map.MyLocation", "Map.Refresh",
        "Map.Use", "Map.Favorite", "Map.None", "Map.Starting", "Map.SelectedPrefix", "Map.Count",
        "Map.NoneInView", "Map.NoneFound", "Map.Loading", "Map.Matching", "Map.MapReady",
        "Map.Locating", "Map.GeoUnavailable", "Map.LocationDenied", "Map.LoadingLib",
        "Map.LeafletMissing", "Map.CouldNotLoad", "Map.Error"
    ];

    public void Apply(string? preference)
    {
        var next = Normalize(preference);
        if (string.Equals(Preference, next, StringComparison.OrdinalIgnoreCase)
            && _applied == Resolved)
            return;
        Preference = next;
        var culture = CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        _applied = Resolved;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyFlow(FrameworkElement? element)
    {
        if (element != null) element.FlowDirection = Flow;
    }

    public static string Normalize(string? preference)
    {
        if (string.IsNullOrWhiteSpace(preference) || preference.Equals("system", StringComparison.OrdinalIgnoreCase)
            || preference.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return System;
        var p = preference.Trim().ToLowerInvariant();
        if (p.StartsWith("fr")) return "fr";
        if (p.StartsWith("ar")) return "ar";
        if (p.StartsWith("en")) return "en";
        return System;
    }

    public static int ComboIndex(string preference) => Normalize(preference) switch
    {
        "en" => 1,
        "fr" => 2,
        "ar" => 3,
        _ => 0
    };

    public static string FromCombo(int index) => index switch
    {
        1 => "en",
        2 => "fr",
        3 => "ar",
        _ => System
    };

    private string _applied = "";

    private static readonly Dictionary<string, Dictionary<string, string>> Catalog = new()
    {
        ["en"] = En(),
        ["fr"] = Fr(),
        ["ar"] = Ar()
    };

    private static Dictionary<string, string> En() => new()
    {
        ["Data.AppName"] = "Mawaqit Widget",
        ["UI.Preferences"] = "Preferences",
        ["UI.Mosque"] = "Mosque",
        ["UI.NoMosque"] = "No mosque selected",
        ["UI.ChangeMosque"] = "Change mosque…",
        ["UI.FindMosque"] = "Find mosque",
        ["UI.PreferencesTip"] = "Preferences",
        ["UI.Refresh"] = "Refresh",
        ["UI.Prayer"] = "Prayer",
        ["UI.Adhan"] = "Adhan",
        ["UI.Iqama"] = "Iqama",
        ["UI.TapToStart"] = "Tap to start",
        ["UI.Countdown"] = "Countdown",
        ["UI.CountdownDesc"] = "What the chip counts down to",
        ["UI.CountDownTo"] = "Count down to",
        ["UI.AdhanThenIqama"] = "Adhan, then iqama",
        ["UI.AdhanThenIqamaHint"] = "Counts to the call, then to iqama for that prayer, then the next adhan.",
        ["UI.Placement"] = "Placement",
        ["UI.PlacementDesc"] = "Where the chip lives",
        ["UI.Location"] = "Location",
        ["UI.InTaskbar"] = "In the taskbar",
        ["UI.ScreenStrip"] = "Screen strip — reserves desktop space",
        ["UI.StripHint"] = "A full-width bar above the taskbar. Windows will not cover it.",
        ["UI.Chip"] = "Chip",
        ["UI.ChipDesc"] = "Always-on info",
        ["UI.Size"] = "Size",
        ["UI.Compact"] = "Compact — countdown only",
        ["UI.Expanded"] = "Expanded — prayer, time, countdown",
        ["UI.Autostart"] = "Start with Windows",
        ["UI.Notifications"] = "Notifications",
        ["UI.NotificationsDesc"] = "Prayer alerts",
        ["UI.AtAdhan"] = "At adhan",
        ["UI.AtIqama"] = "At iqama",
        ["UI.MinutesBefore"] = "Minutes before adhan",
        ["UI.Favorites"] = "Favorites",
        ["UI.FavoritesDesc"] = "Up to 5 mosques",
        ["UI.RemoveFavorite"] = "Remove selected",
        ["UI.FavoritesFull"] = "You can keep 5 favorites. Remove one in Preferences first.",
        ["UI.About"] = "About",
        ["UI.Version"] = "Version",
        ["UI.CheckUpdates"] = "Check for updates",
        ["UI.Language"] = "Language",
        ["UI.LanguageDesc"] = "App language",
        ["UI.LangSystem"] = "Windows default",
        ["UI.LangEn"] = "English",
        ["UI.LangFr"] = "Français",
        ["UI.LangAr"] = "العربية",
        ["UI.Checking"] = "Checking…",
        ["UI.UpToDate"] = "Up to date ({0})",
        ["UI.UpdateAvailable"] = "Update {0} available — downloading…",
        ["UI.StartingInstaller"] = "Starting installer…",
        ["UI.Update"] = "Update",
        ["UI.ContextMenu.Show"] = "Show",
        ["UI.ContextMenu.Hide"] = "Hide",
        ["UI.ContextMenu.Reposition"] = "Re-position",
        ["UI.ContextMenu.ManualPosition"] = "Manual position",
        ["UI.ContextMenu.Display"] = "Display",
        ["UI.ContextMenu.AlwaysPrimary"] = "Always primary",
        ["UI.ContextMenu.Quit"] = "Quit",
        ["UI.ContextMenu.ChipSize"] = "Chip size",
        ["UI.ContextMenu.Compact"] = "Compact",
        ["UI.ContextMenu.Expanded"] = "Expanded",
        ["UI.NoUpcoming"] = "No upcoming prayer",
        ["Prayer.Fajr"] = "Fajr",
        ["Prayer.Shuruq"] = "Shuruq",
        ["Prayer.Dhuhr"] = "Dhuhr",
        ["Prayer.Asr"] = "Asr",
        ["Prayer.Maghrib"] = "Maghrib",
        ["Prayer.Isha"] = "Isha",
        ["Map.Title"] = "Find your mosque",
        ["Map.Hint"] = "Click a name on the map or list. Double-click to use it.",
        ["Map.Filter"] = "Filter",
        ["Map.Placeholder"] = "Filter by name or city…",
        ["Map.MyLocation"] = "My location",
        ["Map.Refresh"] = "Refresh map",
        ["Map.Use"] = "Use selected",
        ["Map.Favorite"] = "Set as favorite",
        ["Map.None"] = "No mosque selected",
        ["Map.Starting"] = "Starting…",
        ["Map.SelectedPrefix"] = "Selected:",
        ["Map.Count"] = "{0} mosque name(s)",
        ["Map.NoneInView"] = "No mosques in view",
        ["Map.NoneFound"] = "No mosques found",
        ["Map.Loading"] = "Loading mosque names…",
        ["Map.Matching"] = "Matching “{0}” on Mawaqit…",
        ["Map.MapReady"] = "Map ready — loading mosque names…",
        ["Map.Locating"] = "Locating…",
        ["Map.GeoUnavailable"] = "Geolocation unavailable — pan the map instead.",
        ["Map.LocationDenied"] = "Location denied — pan the map or filter by name.",
        ["Map.LoadingLib"] = "Loading map library…",
        ["Map.LeafletMissing"] = "Map library loaded but Leaflet is missing.",
        ["Map.CouldNotLoad"] = "Could not load local map library.",
        ["Map.Error"] = "Something went wrong",
        ["Alert.Before"] = "{0} in {1} min",
        ["Alert.BeforeBody"] = "Adhan at {0}.",
        ["Alert.Adhan"] = "It's time for {0}",
        ["Alert.AdhanBody"] = "Adhan {0}.",
        ["Alert.Iqama"] = "{0} iqama",
        ["Alert.IqamaBody"] = "Iqama {0}.",
        ["UI.RequiresWin11"] = "Mawaqit Widget {0} requires Windows 11 (build 22000 or later).",
        ["UI.AlreadyRunning"] = "Mawaqit Widget is already running.",
        ["UI.InjectRetry"] = "{0}\n\nRetry, or Cancel to keep the tray icon only.",
        ["UI.InjectFailed"] = "Could not place the chip on the taskbar. Use the tray icon near the clock — Show will try again.",
        ["UI.ChipHidden"] = "The prayer chip is hidden. Use the tray icon near the clock to Show it later.",
    };

    private static Dictionary<string, string> Fr() => new()
    {
        ["Data.AppName"] = "Mawaqit Widget",
        ["UI.Preferences"] = "Préférences",
        ["UI.Mosque"] = "Mosquée",
        ["UI.NoMosque"] = "Aucune mosquée sélectionnée",
        ["UI.ChangeMosque"] = "Changer de mosquée…",
        ["UI.FindMosque"] = "Trouver une mosquée",
        ["UI.PreferencesTip"] = "Préférences",
        ["UI.Refresh"] = "Actualiser",
        ["UI.Prayer"] = "Prière",
        ["UI.Adhan"] = "Adhan",
        ["UI.Iqama"] = "Iqama",
        ["UI.TapToStart"] = "Appuyer pour commencer",
        ["UI.Countdown"] = "Compte à rebours",
        ["UI.CountdownDesc"] = "Cible du compte à rebours",
        ["UI.CountDownTo"] = "Compter jusqu’à",
        ["UI.AdhanThenIqama"] = "Adhan, puis iqama",
        ["UI.AdhanThenIqamaHint"] = "Compte jusqu’à l’appel, puis l’iqama de cette prière, puis le prochain adhan.",
        ["UI.Placement"] = "Emplacement",
        ["UI.PlacementDesc"] = "Où vit la pastille",
        ["UI.Location"] = "Position",
        ["UI.InTaskbar"] = "Dans la barre des tâches",
        ["UI.ScreenStrip"] = "Bandeau — réserve l’espace du bureau",
        ["UI.StripHint"] = "Une barre pleine largeur au-dessus de la barre des tâches. Les fenêtres ne la recouvrent pas.",
        ["UI.Chip"] = "Pastille",
        ["UI.ChipDesc"] = "Infos toujours visibles",
        ["UI.Size"] = "Taille",
        ["UI.Compact"] = "Compact — compte à rebours seulement",
        ["UI.Expanded"] = "Étendu — prière, heure, compte à rebours",
        ["UI.Autostart"] = "Démarrer avec Windows",
        ["UI.Notifications"] = "Notifications",
        ["UI.NotificationsDesc"] = "Alertes de prière",
        ["UI.AtAdhan"] = "À l’adhan",
        ["UI.AtIqama"] = "À l’iqama",
        ["UI.MinutesBefore"] = "Minutes avant l’adhan",
        ["UI.Favorites"] = "Favoris",
        ["UI.FavoritesDesc"] = "Jusqu’à 5 mosquées",
        ["UI.RemoveFavorite"] = "Retirer la sélection",
        ["UI.FavoritesFull"] = "5 favoris maximum. Retirez-en un dans Préférences.",
        ["UI.About"] = "À propos",
        ["UI.Version"] = "Version",
        ["UI.CheckUpdates"] = "Rechercher des mises à jour",
        ["UI.Language"] = "Langue",
        ["UI.LanguageDesc"] = "Langue de l’application",
        ["UI.LangSystem"] = "Langue de Windows",
        ["UI.LangEn"] = "English",
        ["UI.LangFr"] = "Français",
        ["UI.LangAr"] = "العربية",
        ["UI.Checking"] = "Vérification…",
        ["UI.UpToDate"] = "À jour ({0})",
        ["UI.UpdateAvailable"] = "Mise à jour {0} — téléchargement…",
        ["UI.StartingInstaller"] = "Démarrage de l’installateur…",
        ["UI.Update"] = "Mise à jour",
        ["UI.ContextMenu.Show"] = "Afficher",
        ["UI.ContextMenu.Hide"] = "Masquer",
        ["UI.ContextMenu.Reposition"] = "Repositionner",
        ["UI.ContextMenu.ManualPosition"] = "Position manuelle",
        ["UI.ContextMenu.Display"] = "Écran",
        ["UI.ContextMenu.AlwaysPrimary"] = "Toujours l’écran principal",
        ["UI.ContextMenu.Quit"] = "Quitter",
        ["UI.ContextMenu.ChipSize"] = "Taille",
        ["UI.ContextMenu.Compact"] = "Compact",
        ["UI.ContextMenu.Expanded"] = "Étendu",
        ["UI.NoUpcoming"] = "Aucune prière à venir",
        ["Prayer.Fajr"] = "Fajr",
        ["Prayer.Shuruq"] = "Chourouq",
        ["Prayer.Dhuhr"] = "Dhuhr",
        ["Prayer.Asr"] = "Asr",
        ["Prayer.Maghrib"] = "Maghrib",
        ["Prayer.Isha"] = "Isha",
        ["Map.Title"] = "Trouver votre mosquée",
        ["Map.Hint"] = "Cliquez un nom sur la carte ou la liste. Double-clic pour l’utiliser.",
        ["Map.Filter"] = "Filtrer",
        ["Map.Placeholder"] = "Filtrer par nom ou ville…",
        ["Map.MyLocation"] = "Ma position",
        ["Map.Refresh"] = "Actualiser la carte",
        ["Map.Use"] = "Utiliser",
        ["Map.Favorite"] = "Mettre en favori",
        ["Map.None"] = "Aucune mosquée sélectionnée",
        ["Map.Starting"] = "Démarrage…",
        ["Map.SelectedPrefix"] = "Sélection :",
        ["Map.Count"] = "{0} mosquée(s)",
        ["Map.NoneInView"] = "Aucune mosquée dans la vue",
        ["Map.NoneFound"] = "Aucune mosquée trouvée",
        ["Map.Loading"] = "Chargement des mosquées…",
        ["Map.Matching"] = "Recherche de « {0} » sur Mawaqit…",
        ["Map.MapReady"] = "Carte prête — chargement des mosquées…",
        ["Map.Locating"] = "Localisation…",
        ["Map.GeoUnavailable"] = "Géolocalisation indisponible — déplacez la carte.",
        ["Map.LocationDenied"] = "Position refusée — déplacez la carte ou filtrez par nom.",
        ["Map.LoadingLib"] = "Chargement de la carte…",
        ["Map.LeafletMissing"] = "La bibliothèque de carte a chargé mais Leaflet est absent.",
        ["Map.CouldNotLoad"] = "Impossible de charger la carte locale.",
        ["Map.Error"] = "Une erreur s’est produite",
        ["Alert.Before"] = "{0} dans {1} min",
        ["Alert.BeforeBody"] = "Adhan à {0}.",
        ["Alert.Adhan"] = "C’est l’heure de {0}",
        ["Alert.AdhanBody"] = "Adhan {0}.",
        ["Alert.Iqama"] = "Iqama de {0}",
        ["Alert.IqamaBody"] = "Iqama {0}.",
        ["UI.RequiresWin11"] = "Mawaqit Widget {0} nécessite Windows 11 (build 22000 ou plus).",
        ["UI.AlreadyRunning"] = "Mawaqit Widget est déjà en cours d’exécution.",
        ["UI.InjectRetry"] = "{0}\n\nRéessayer, ou Annuler pour garder seulement l’icône de notification.",
        ["UI.InjectFailed"] = "Impossible de placer la pastille. Utilisez l’icône près de l’horloge — Afficher réessaiera.",
        ["UI.ChipHidden"] = "La pastille est masquée. Utilisez l’icône près de l’horloge pour l’afficher.",
    };

    private static Dictionary<string, string> Ar() => new()
    {
        ["Data.AppName"] = "Mawaqit Widget",
        ["UI.Preferences"] = "الإعدادات",
        ["UI.Mosque"] = "المسجد",
        ["UI.NoMosque"] = "لم يُحدد مسجد",
        ["UI.ChangeMosque"] = "تغيير المسجد…",
        ["UI.FindMosque"] = "البحث عن مسجد",
        ["UI.PreferencesTip"] = "الإعدادات",
        ["UI.Refresh"] = "تحديث",
        ["UI.Prayer"] = "الصلاة",
        ["UI.Adhan"] = "الأذان",
        ["UI.Iqama"] = "الإقامة",
        ["UI.TapToStart"] = "اضغط للبدء",
        ["UI.Countdown"] = "العدّ التنازلي",
        ["UI.CountdownDesc"] = "هدف العدّ التنازلي",
        ["UI.CountDownTo"] = "العدّ حتى",
        ["UI.AdhanThenIqama"] = "الأذان ثم الإقامة",
        ["UI.AdhanThenIqamaHint"] = "يعدّ حتى الأذان، ثم إقامة تلك الصلاة، ثم الأذان التالي.",
        ["UI.Placement"] = "الموضع",
        ["UI.PlacementDesc"] = "مكان الشريحة",
        ["UI.Location"] = "الموقع",
        ["UI.InTaskbar"] = "في شريط المهام",
        ["UI.ScreenStrip"] = "شريط الشاشة — يحجز مساحة سطح المكتب",
        ["UI.StripHint"] = "شريط بعرض الشاشة فوق شريط المهام. لا تغطيه النوافذ.",
        ["UI.Chip"] = "الشريحة",
        ["UI.ChipDesc"] = "المعلومات الظاهرة دائماً",
        ["UI.Size"] = "الحجم",
        ["UI.Compact"] = "مضغوط — العدّ فقط",
        ["UI.Expanded"] = "موسّع — الصلاة والوقت والعدّ",
        ["UI.Autostart"] = "التشغيل مع Windows",
        ["UI.Notifications"] = "الإشعارات",
        ["UI.NotificationsDesc"] = "تنبيهات الصلاة",
        ["UI.AtAdhan"] = "عند الأذان",
        ["UI.AtIqama"] = "عند الإقامة",
        ["UI.MinutesBefore"] = "دقائق قبل الأذان",
        ["UI.Favorites"] = "المفضلة",
        ["UI.FavoritesDesc"] = "حتى 5 مساجد",
        ["UI.RemoveFavorite"] = "إزالة المحدد",
        ["UI.FavoritesFull"] = "يمكنك حفظ 5 مفضلات. أزل واحدة من الإعدادات أولاً.",
        ["UI.About"] = "حول",
        ["UI.Version"] = "الإصدار",
        ["UI.CheckUpdates"] = "التحقق من التحديثات",
        ["UI.Language"] = "اللغة",
        ["UI.LanguageDesc"] = "لغة التطبيق",
        ["UI.LangSystem"] = "لغة Windows",
        ["UI.LangEn"] = "English",
        ["UI.LangFr"] = "Français",
        ["UI.LangAr"] = "العربية",
        ["UI.Checking"] = "جارٍ التحقق…",
        ["UI.UpToDate"] = "محدّث ({0})",
        ["UI.UpdateAvailable"] = "يتوفر {0} — جارٍ التنزيل…",
        ["UI.StartingInstaller"] = "تشغيل المُثبّت…",
        ["UI.Update"] = "تحديث",
        ["UI.ContextMenu.Show"] = "إظهار",
        ["UI.ContextMenu.Hide"] = "إخفاء",
        ["UI.ContextMenu.Reposition"] = "إعادة التموضع",
        ["UI.ContextMenu.ManualPosition"] = "موضع يدوي",
        ["UI.ContextMenu.Display"] = "الشاشة",
        ["UI.ContextMenu.AlwaysPrimary"] = "دائماً الشاشة الرئيسية",
        ["UI.ContextMenu.Quit"] = "خروج",
        ["UI.ContextMenu.ChipSize"] = "الحجم",
        ["UI.ContextMenu.Compact"] = "مضغوط",
        ["UI.ContextMenu.Expanded"] = "موسّع",
        ["UI.NoUpcoming"] = "لا صلاة قادمة",
        ["Prayer.Fajr"] = "الفجر",
        ["Prayer.Shuruq"] = "الشروق",
        ["Prayer.Dhuhr"] = "الظهر",
        ["Prayer.Asr"] = "العصر",
        ["Prayer.Maghrib"] = "المغرب",
        ["Prayer.Isha"] = "العشاء",
        ["Map.Title"] = "ابحث عن مسجدك",
        ["Map.Hint"] = "انقر اسماً على الخريطة أو القائمة. انقر مرتين للاستخدام.",
        ["Map.Filter"] = "تصفية",
        ["Map.Placeholder"] = "تصفية بالاسم أو المدينة…",
        ["Map.MyLocation"] = "موقعي",
        ["Map.Refresh"] = "تحديث الخريطة",
        ["Map.Use"] = "استخدام المحدد",
        ["Map.Favorite"] = "إضافة للمفضلة",
        ["Map.None"] = "لم يُحدد مسجد",
        ["Map.Starting"] = "جارٍ البدء…",
        ["Map.SelectedPrefix"] = "المحدد:",
        ["Map.Count"] = "{0} مسجد",
        ["Map.NoneInView"] = "لا مساجد في العرض",
        ["Map.NoneFound"] = "لم يُعثر على مساجد",
        ["Map.Loading"] = "جارٍ تحميل المساجد…",
        ["Map.Matching"] = "البحث عن « {0} » في Mawaqit…",
        ["Map.MapReady"] = "الخريطة جاهزة — جارٍ تحميل المساجد…",
        ["Map.Locating"] = "جارٍ تحديد الموقع…",
        ["Map.GeoUnavailable"] = "تحديد الموقع غير متاح — حرّك الخريطة.",
        ["Map.LocationDenied"] = "رُفض الموقع — حرّك الخريطة أو صفِّ بالاسم.",
        ["Map.LoadingLib"] = "جارٍ تحميل مكتبة الخريطة…",
        ["Map.LeafletMissing"] = "حُمّلت المكتبة لكن Leaflet غير موجود.",
        ["Map.CouldNotLoad"] = "تعذّر تحميل الخريطة المحلية.",
        ["Map.Error"] = "حدث خطأ",
        ["Alert.Before"] = "{0} بعد {1} د",
        ["Alert.BeforeBody"] = "الأذان عند {0}.",
        ["Alert.Adhan"] = "حان وقت {0}",
        ["Alert.AdhanBody"] = "الأذان {0}.",
        ["Alert.Iqama"] = "إقامة {0}",
        ["Alert.IqamaBody"] = "الإقامة {0}.",
        ["UI.RequiresWin11"] = "يتطلب Mawaqit Widget {0} نظام Windows 11 (الإصدار 22000 أو أحدث).",
        ["UI.AlreadyRunning"] = "Mawaqit Widget قيد التشغيل بالفعل.",
        ["UI.InjectRetry"] = "{0}\n\nأعد المحاولة، أو إلغاء للإبقاء على أيقونة المنطقة فقط.",
        ["UI.InjectFailed"] = "تعذّر وضع الشريحة على شريط المهام. استخدم الأيقونة بجانب الساعة — إظهار سيعيد المحاولة.",
        ["UI.ChipHidden"] = "الشريحة مخفية. استخدم الأيقونة بجانب الساعة لإظهارها لاحقاً.",
    };
}
