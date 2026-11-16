using GibEvrakIndirici.Core.Adlandirma;

namespace GibEvrakIndirici.Core.Arsiv;

/// <summary>
/// Arşivde bir evrakın zaten bulunup bulunmadığını söyler (§8.1).
/// Yeni biçimli tam ad varsa → var.
/// Yoksa aynı önekle başlayan "eski" dosyalar sayılır (damgasız, yyyyMMddHHmmss damgalı, _DÜZ, _DÜZ2 ...);
/// bu dönem için kaçıncı yüklemeyse (sira, 0'dan) o kadar eski dosya varsa → var sayılır.
/// Elle eklenen _MAHSUP, _ÖD dosyaları sayılmaz. Uygulama hiçbir dosyayı silmez/üzerine yazmaz.
/// </summary>
public static class ArsivDenetci
{
    static readonly string[] SayilmayanEkler = ["_MAHSUP", "_ÖD", " ÖD", "_SPEC_HARICI"];

    public static bool VarMi(string klasor, string onek, string hedefAd, int sira)
    {
        if (!Directory.Exists(klasor)) return false;
        if (File.Exists(Path.Combine(klasor, hedefAd))) return true;
        return EskiDosyaSayisi(klasor, onek) > sira;
    }

    public static int EskiDosyaSayisi(string klasor, string onek)
    {
        if (!Directory.Exists(klasor)) return 0;
        int n = 0;
        foreach (var yol in Directory.EnumerateFiles(klasor, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            var ad = Path.GetFileNameWithoutExtension(yol);
            if (!ad.StartsWith(onek, StringComparison.OrdinalIgnoreCase)) continue;
            // önek sınırı: "..._IHBARNAME" "..._IHBARNAME2" ile karışmasın
            if (ad.Length > onek.Length && ad[onek.Length] != '_') continue;
            if (SayilmayanEkler.Any(e => ad.Contains(e, StringComparison.OrdinalIgnoreCase))) continue;
            n++;
        }
        return n;
    }

    /// <summary>Y:\{müşteri}\{yıl}\{TÜR}</summary>
    public static string BeyannameKlasoru(string yKok, Musteri m, BeyannameSatiri b)
        => Path.Combine(yKok, m.YKlasor, DosyaAdlari.YilKlasoru(b.Donem).ToString(), DosyaAdlari.TurKlasoru(b.Tur));

    /// <summary>X:\{yıl}\{müşteri}\{işyeri}</summary>
    public static string SgkKlasoru(string xKok, Musteri m, Donem d, string isyeriKlasoru)
        => Path.Combine(xKok, d.BasYil.ToString(), m.XKlasor, isyeriKlasoru);
}

/// <summary>Aynı mükellef + tür + dönem için yüklemeleri sıralar: 0 = asıl, 1 = _DÜZ, 2 = _DÜZ2 ... (§9.2)</summary>
public static class DuzeltmeSiralayici
{
    public static Dictionary<string, int> Sirala(IEnumerable<BeyannameSatiri> satirlar) =>
        satirlar.GroupBy(s => (s.VknTckn, s.Tur, s.Donem))
                .SelectMany(g => g.OrderBy(s => s.YuklemeZamani).Select((s, i) => (s.Oid, i)))
                .ToDictionary(x => x.Oid, x => x.i);
}
