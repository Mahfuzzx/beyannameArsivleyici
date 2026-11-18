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

    public static int EskiDosyaSayisi(string klasor, string onek) => Dosyalar(klasor, onek).Count();

    /// <summary>Önekle başlayan (önek sınırı '_' ya da ad sonu), elle eklenen ekleri taşımayan arşiv dosyalarının adları (.pdf'siz).</summary>
    public static IEnumerable<string> Dosyalar(string klasor, string onek)
    {
        if (!Directory.Exists(klasor)) yield break;
        foreach (var yol in Directory.EnumerateFiles(klasor, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            var ad = Path.GetFileNameWithoutExtension(yol);
            if (!ad.StartsWith(onek, StringComparison.OrdinalIgnoreCase)) continue;
            // önek sınırı: "..._IHBARNAME" "..._IHBARNAME2" ile karışmasın
            if (ad.Length > onek.Length && ad[onek.Length] != '_') continue;
            if (SayilmayanEkler.Any(e => ad.Contains(e, StringComparison.OrdinalIgnoreCase))) continue;
            yield return ad;
        }
    }

    static readonly System.Text.RegularExpressions.Regex DamgaRx = new(
        @"_(?:(?<g>\d{2})\.(?<a>\d{2})\.(?<y>\d{4})(?:_(?<s>\d{2})_(?<d>\d{2})_(?<n>\d{2}))?|(?<y2>\d{4})(?<a2>\d{2})(?<g2>\d{2})(?<s2>\d{2})(?<d2>\d{2})(?<n2>\d{2}))(?=_|$)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Arşivde bu önekle, adındaki zaman damgası <paramref name="oncesi"/>'nden eski olan dosya sayısı.
    /// Liste penceresinin (son 31 gün) dışında kalan asıl beyannameyi görmek için kullanılır (§9.2):
    /// eski bir dönemin bu ay verilen düzeltmesi, arşivdeki eski asıl dosya sayesinde _DÜZ olarak tanınır.
    /// Yalnız tarihli ad ("..._09.11.2026") gün olarak karşılaştırılır; damgasız adlar sayılmaz (belirsiz).
    /// </summary>
    public static int OncekiYuklemeSayisi(string klasor, string onek, DateTime oncesi)
    {
        int n = 0;
        foreach (var ad in Dosyalar(klasor, onek))
        {
            var m = DamgaRx.Match(ad[onek.Length..]);
            if (!m.Success) continue;
            DateTime zaman;
            bool sadeceGun = false;
            try
            {
                if (m.Groups["y"].Success)
                {
                    var gun = new DateTime(int.Parse(m.Groups["y"].Value), int.Parse(m.Groups["a"].Value), int.Parse(m.Groups["g"].Value));
                    if (m.Groups["s"].Success)
                        zaman = gun.Add(new TimeSpan(int.Parse(m.Groups["s"].Value), int.Parse(m.Groups["d"].Value), int.Parse(m.Groups["n"].Value)));
                    else { zaman = gun; sadeceGun = true; }
                }
                else
                    zaman = new DateTime(int.Parse(m.Groups["y2"].Value), int.Parse(m.Groups["a2"].Value), int.Parse(m.Groups["g2"].Value),
                        int.Parse(m.Groups["s2"].Value), int.Parse(m.Groups["d2"].Value), int.Parse(m.Groups["n2"].Value));
            }
            catch (ArgumentOutOfRangeException) { continue; }
            if (sadeceGun ? zaman < oncesi.Date : zaman < oncesi) n++;
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
