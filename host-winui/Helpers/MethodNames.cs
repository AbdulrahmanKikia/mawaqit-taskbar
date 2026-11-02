namespace Mawaqit.WinUI.Helpers;

/// <summary>Official names of the calculation authorities (proper names, so not translated).</summary>
internal static class MethodNames
{
    public static string Short(CalculationMethod m) => m switch
    {
        CalculationMethod.MuslimWorldLeague => "MWL",
        CalculationMethod.Isna => "ISNA",
        CalculationMethod.Egypt => "Egypt",
        CalculationMethod.UmmAlQura => "Umm al-Qura",
        CalculationMethod.Karachi => "Karachi",
        CalculationMethod.Tehran => "Tehran",
        CalculationMethod.Jafari => "Jafari",
        CalculationMethod.Gulf => "Gulf",
        CalculationMethod.Kuwait => "Kuwait",
        CalculationMethod.Qatar => "Qatar",
        CalculationMethod.Singapore => "MUIS",
        CalculationMethod.France => "UOIF",
        CalculationMethod.Turkey => "Diyanet",
        CalculationMethod.Russia => "Russia",
        CalculationMethod.Dubai => "Dubai",
        CalculationMethod.Jakim => "JAKIM",
        CalculationMethod.Tunisia => "Tunisia",
        CalculationMethod.Algeria => "Algeria",
        CalculationMethod.Kemenag => "Kemenag",
        CalculationMethod.Morocco => "Morocco",
        CalculationMethod.Portugal => "Lisbon",
        CalculationMethod.Jordan => "Jordan",
        _ => m.ToString(),
    };

    public static string Long(CalculationMethod m) => m switch
    {
        CalculationMethod.MuslimWorldLeague => "Muslim World League (18° / 17°)",
        CalculationMethod.Isna => "ISNA — Islamic Society of North America (15° / 15°)",
        CalculationMethod.Egypt => "Egyptian General Authority of Survey (19.5° / 17.5°)",
        CalculationMethod.UmmAlQura => "Umm al-Qura, Makkah (18.5° / 90 min)",
        CalculationMethod.Karachi => "University of Islamic Sciences, Karachi (18° / 18°)",
        CalculationMethod.Tehran => "Institute of Geophysics, Tehran",
        CalculationMethod.Jafari => "Shia Ithna-Ashari, Leva Institute, Qum",
        CalculationMethod.Gulf => "Gulf Region (19.5° / 90 min)",
        CalculationMethod.Kuwait => "Kuwait (18° / 17.5°)",
        CalculationMethod.Qatar => "Qatar (18° / 90 min)",
        CalculationMethod.Singapore => "MUIS — Majlis Ugama Islam Singapura (20° / 18°)",
        CalculationMethod.France => "UOIF — Union des Organisations Islamiques de France (12° / 12°)",
        CalculationMethod.Turkey => "Diyanet İşleri Başkanlığı, Turkey",
        CalculationMethod.Russia => "Spiritual Administration of Muslims of Russia (16° / 15°)",
        CalculationMethod.Dubai => "Dubai (18.2° / 18.2°)",
        CalculationMethod.Jakim => "JAKIM — Jabatan Kemajuan Islam Malaysia (20° / 18°)",
        CalculationMethod.Tunisia => "Tunisia (18° / 18°)",
        CalculationMethod.Algeria => "Algeria (18° / 17°)",
        CalculationMethod.Kemenag => "Kemenag — Indonesia (20° / 18°)",
        CalculationMethod.Morocco => "Morocco (19° / 17°)",
        CalculationMethod.Portugal => "Comunidade Islâmica de Lisboa",
        CalculationMethod.Jordan => "Ministry of Awqaf, Jordan (18° / 18°)",
        _ => m.ToString(),
    };
}
