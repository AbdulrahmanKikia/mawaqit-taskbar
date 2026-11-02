namespace Mawaqit.Core;

/// <summary>Calculation conventions, with the same parameters as api.aladhan.com /v1/methods.</summary>
public enum CalculationMethod
{
    MuslimWorldLeague,
    Isna,
    Egypt,
    UmmAlQura,
    Karachi,
    Tehran,
    Jafari,
    Gulf,
    Kuwait,
    Qatar,
    Singapore,
    France,
    Turkey,
    Russia,
    Dubai,
    Jakim,
    Tunisia,
    Algeria,
    Kemenag,
    Morocco,
    Portugal,
    Jordan,
}

/// <summary>Asr shadow length: Standard (Shafi'i, Maliki, Hanbali) = 1, Hanafi = 2.</summary>
public enum AsrSchool { Standard, Hanafi }

/// <summary>How Fajr/Isha are bounded where twilight never fully ends (high latitudes in summer).</summary>
public enum HighLatitudeRule { AngleBased, MiddleOfNight, OneSeventh }

/// <param name="IshaMinutes">Isha this many minutes after Maghrib instead of by angle.</param>
/// <param name="MaghribAngle">Maghrib by sun angle (Shia methods) instead of sunset.</param>
/// <param name="MaghribMinutes">Maghrib this many minutes after sunset.</param>
/// <param name="RamadanIshaMinutes">Umm al-Qura: Isha 120 min after Maghrib during Ramadan.</param>
/// <param name="Offsets">Fixed minutes the authority adds to each time (Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha), e.g. Diyanet's temkin.</param>
public sealed record MethodParams(
    double FajrAngle,
    double? IshaAngle,
    double? IshaMinutes = null,
    double? MaghribAngle = null,
    double MaghribMinutes = 0,
    double? RamadanIshaMinutes = null,
    int[]? Offsets = null);

public sealed class CalculationSettings
{
    /// <summary>null = pick from the Windows region (<see cref="Methods.DefaultFor"/>).</summary>
    public CalculationMethod? Method { get; set; }
    /// <summary>null = pick from the Windows region.</summary>
    public AsrSchool? Asr { get; set; }
    public HighLatitudeRule HighLatitude { get; set; } = HighLatitudeRule.AngleBased;
}

public static class Methods
{
    public static MethodParams Params(CalculationMethod method) => method switch
    {
        CalculationMethod.MuslimWorldLeague => new(18, 17),
        CalculationMethod.Isna => new(15, 15),
        CalculationMethod.Egypt => new(19.5, 17.5),
        CalculationMethod.UmmAlQura => new(18.5, null, IshaMinutes: 90, RamadanIshaMinutes: 120),
        CalculationMethod.Karachi => new(18, 18),
        CalculationMethod.Tehran => new(17.7, 14, MaghribAngle: 4.5),
        CalculationMethod.Jafari => new(16, 14, MaghribAngle: 4),
        CalculationMethod.Gulf => new(19.5, null, IshaMinutes: 90),
        CalculationMethod.Kuwait => new(18, 17.5),
        CalculationMethod.Qatar => new(18, null, IshaMinutes: 90),
        CalculationMethod.Singapore => new(20, 18),
        CalculationMethod.France => new(12, 12),
        CalculationMethod.Turkey => new(18, 17, Offsets: [0, -7, 5, 4, 7, 0]),
        CalculationMethod.Russia => new(16, 15),
        CalculationMethod.Dubai => new(18.2, 18.2),
        CalculationMethod.Jakim => new(20, 18),
        CalculationMethod.Tunisia => new(18, 18),
        CalculationMethod.Algeria => new(18, 17),
        CalculationMethod.Kemenag => new(20, 18),
        CalculationMethod.Morocco => new(19, 17, Offsets: [0, 0, 5, 0, 5, 0]),
        CalculationMethod.Portugal => new(18, null, IshaMinutes: 77, MaghribMinutes: 3, Offsets: [0, 0, 5, 0, 0, 0]),
        CalculationMethod.Jordan => new(18, 18, MaghribMinutes: 5),
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    /// <summary>api.aladhan.com method id, used to check our results against theirs.</summary>
    public static int AladhanId(CalculationMethod method) => method switch
    {
        CalculationMethod.Jafari => 0,
        CalculationMethod.Karachi => 1,
        CalculationMethod.Isna => 2,
        CalculationMethod.MuslimWorldLeague => 3,
        CalculationMethod.UmmAlQura => 4,
        CalculationMethod.Egypt => 5,
        CalculationMethod.Tehran => 7,
        CalculationMethod.Gulf => 8,
        CalculationMethod.Kuwait => 9,
        CalculationMethod.Qatar => 10,
        CalculationMethod.Singapore => 11,
        CalculationMethod.France => 12,
        CalculationMethod.Turkey => 13,
        CalculationMethod.Russia => 14,
        CalculationMethod.Dubai => 16,
        CalculationMethod.Jakim => 17,
        CalculationMethod.Tunisia => 18,
        CalculationMethod.Algeria => 19,
        CalculationMethod.Kemenag => 20,
        CalculationMethod.Morocco => 21,
        CalculationMethod.Portugal => 22,
        CalculationMethod.Jordan => 23,
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    /// <summary>The convention most mosques use in a country (ISO 3166 two-letter code).</summary>
    public static (CalculationMethod Method, AsrSchool Asr) DefaultFor(string? regionCode)
    {
        var cc = (regionCode ?? "").ToUpperInvariant();
        var method = cc switch
        {
            "US" or "CA" or "MX" => CalculationMethod.Isna,
            "SA" or "YE" => CalculationMethod.UmmAlQura,
            "AE" => CalculationMethod.Dubai,
            "KW" => CalculationMethod.Kuwait,
            "QA" => CalculationMethod.Qatar,
            "BH" or "OM" => CalculationMethod.Gulf,
            "EG" or "SD" or "LY" or "SY" or "LB" or "IQ" => CalculationMethod.Egypt,
            "PK" or "IN" or "BD" or "AF" => CalculationMethod.Karachi,
            "TR" => CalculationMethod.Turkey,
            "FR" => CalculationMethod.France,
            "SG" => CalculationMethod.Singapore,
            "MY" or "BN" => CalculationMethod.Jakim,
            "ID" => CalculationMethod.Kemenag,
            "MA" => CalculationMethod.Morocco,
            "DZ" => CalculationMethod.Algeria,
            "TN" => CalculationMethod.Tunisia,
            "JO" or "PS" => CalculationMethod.Jordan,
            "PT" => CalculationMethod.Portugal,
            "IR" => CalculationMethod.Tehran,
            "RU" => CalculationMethod.Russia,
            _ => CalculationMethod.MuslimWorldLeague,
        };
        var asr = cc is "PK" or "IN" or "BD" or "AF" or "TR" ? AsrSchool.Hanafi : AsrSchool.Standard;
        return (method, asr);
    }

    public static (CalculationMethod Method, AsrSchool Asr) Resolve(CalculationSettings settings, string? regionCode)
    {
        var (m, a) = DefaultFor(regionCode);
        return (settings.Method ?? m, settings.Asr ?? a);
    }
}
