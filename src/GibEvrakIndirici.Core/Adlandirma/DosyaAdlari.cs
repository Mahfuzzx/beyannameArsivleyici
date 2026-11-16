using System.Globalization;
using System.Text.RegularExpressions;

namespace GibEvrakIndirici.Core.Adlandirma;

/// <summary>
/// Arşiv dosya adları (bkz. GIB_AKIS_NOTLARI.md §8.1, §8.2, §9.2).
/// Y:\{müşteri}\{dönem yılı}\{TÜR}\{etiket}_{TÜR}_{BEYANNAME|TAHAKKUK|IHBARNAME[n]}_{dd.MM.yyyy_HH_mm_ss}[_DÜZ[n]].pdf
/// X:\{yıl}\{müşteri}\{işyeri}\{AA}_{AY}_{BT}_{KANUN}_{ASIL|EKn|İPTALn}_{TAHAKKUK|HIZMET_LISTESI}_{yyyyMMddHHmmss}.pdf
/// </summary>
public static class DosyaAdlari
{
    public static readonly string[] Aylar =
        ["OCAK", "SUBAT", "MART", "NISAN", "MAYIS", "HAZIRAN", "TEMMUZ", "AGUSTOS", "EYLUL", "EKIM", "KASIM", "ARALIK"];

    public const string ZamanBicimi = "dd.MM.yyyy_HH_mm_ss";
    public const string EskiDamgaBicimi = "yyyyMMddHHmmss";

    /// <summary>Arşivdeki yıl klasörü = vergilendirme döneminin (bitiş) yılı.</summary>
    public static int YilKlasoru(Donem d) => d.BitYil;

    /// <summary>
    /// Dönem etiketi:
    ///  tek ay          → "09_EYLUL"
    ///  12 ay (yıllık)  → "2025"
    ///  3 ay (çeyrek)   → "03_DONEM" (bitiş ayı / 3)
    ///  diğer           → başlangıç ayı biçimi
    /// </summary>
    public static string DonemEtiketi(Donem d)
    {
        if (d.AySayisi == 12 && d.BasAy == 1) return d.BitYil.ToString(CultureInfo.InvariantCulture);
        if (d.AySayisi == 3 && d.BitAy % 3 == 0) return $"{d.BitAy / 3:00}_DONEM";
        if (d.AySayisi == 1) return $"{d.BasAy:00}_{Aylar[d.BasAy - 1]}";
        return $"{d.BasAy:00}_{Aylar[d.BasAy - 1]}";
    }

    /// <summary>Tür klasörü adı (GİB kodu, ör. KDV1, MUHSGK, GELIR).</summary>
    public static string TurKlasoru(string tur) => tur.Trim().ToUpperInvariant();

    /// <summary>"{etiket}_{TÜR}_{BELGE}" — var-mı kontrolündeki ortak önek.</summary>
    public static string BeyannameOnek(BeyannameSatiri b, EvrakTuru evrak, int ihbarnameSira = 1)
        => $"{DonemEtiketi(b.Donem)}_{TurKlasoru(b.Tur)}_{BelgeKelimesi(evrak, ihbarnameSira)}";

    /// <param name="duzeltmeSira">0 = asıl, 1 = ilk düzeltme (_DÜZ), 2 = _DÜZ2 ...</param>
    /// <param name="zaman">Dosya adındaki zaman; verilmezse yükleme zamanı. KDV türlerinde beyanname PDF'indeki onay zamanı verilir (§13.1).</param>
    public static string BeyannameDosyasi(BeyannameSatiri b, EvrakTuru evrak, int duzeltmeSira, int ihbarnameSira = 1, DateTime? zaman = null)
        => $"{BeyannameOnek(b, evrak, ihbarnameSira)}_{(zaman ?? b.YuklemeZamani).ToString(ZamanBicimi, CultureInfo.InvariantCulture)}{DuzeltmeEki(duzeltmeSira)}.pdf";

    public static string DuzeltmeEki(int sira) => sira switch
    {
        <= 0 => "",
        1 => "_DÜZ",
        _ => $"_DÜZ{sira}"
    };

    static string BelgeKelimesi(EvrakTuru e, int ihbarnameSira) => e switch
    {
        EvrakTuru.Beyanname => "BEYANNAME",
        EvrakTuru.Tahakkuk => "TAHAKKUK",
        EvrakTuru.Ihbarname => ihbarnameSira <= 1 ? "IHBARNAME" : $"IHBARNAME{ihbarnameSira}",
        _ => throw new ArgumentOutOfRangeException(nameof(e), "SGK evrakları için SgkDosyasi kullanın")
    };

    // ---------------- SGK ----------------

    static readonly Regex KanunRegex = new(@"\b(\d{5})-", RegexOptions.Compiled);

    /// <summary>
    /// SGK bildirim açıklamasından ad parçalarını çıkarır.
    /// "Asıl, TÜM SİG.KOLLARI/YABNC UYR, 05510-Say.Kan.MYO, 09 / 2026, 0707..." → (01, 05510, ASIL)
    /// Belge türü eşlemesi kesin değil; bilinmeyen metin "01" sayılır (TODO: gerçek örneklerle genişlet).
    /// </summary>
    public static (string BelgeTuru, string Kanun, string Mahiyet) SgkAdParcalari(string aciklama, int ekIptalSira = 1)
    {
        var parcalar = aciklama.Split(',', StringSplitOptions.TrimEntries);
        var mahiyetMetni = parcalar.Length > 0 ? parcalar[0] : "";
        var mahiyet = mahiyetMetni.StartsWith("Ek", StringComparison.OrdinalIgnoreCase) ? $"EK{ekIptalSira}"
                    : mahiyetMetni.StartsWith("İptal", StringComparison.OrdinalIgnoreCase) || mahiyetMetni.StartsWith("Iptal", StringComparison.OrdinalIgnoreCase) ? $"İPTAL{ekIptalSira}"
                    : "ASIL";

        var turMetni = parcalar.Length > 1 ? parcalar[1].ToUpper(new CultureInfo("tr-TR")) : "";
        var belgeTuru = turMetni.Contains("DESTEK") || turMetni.Contains("SGDP") ? "02" : "01";

        var km = KanunRegex.Match(aciklama);
        var kanun = km.Success ? km.Groups[1].Value : "00000";
        return (belgeTuru, kanun, mahiyet);
    }

    public static string SgkDosyasi(Donem d, string belgeTuru, string kanun, string mahiyet, EvrakTuru evrak, DateTime damga)
    {
        var belge = evrak switch
        {
            EvrakTuru.SgkTahakkuk => "TAHAKKUK",
            EvrakTuru.SgkHizmetListesi => "HIZMET_LISTESI",
            _ => throw new ArgumentOutOfRangeException(nameof(evrak))
        };
        return $"{d.BasAy:00}_{Aylar[d.BasAy - 1]}_{belgeTuru}_{kanun}_{mahiyet}_{belge}_{damga.ToString(EskiDamgaBicimi, CultureInfo.InvariantCulture)}.pdf";
    }

    public static string SgkOnek(Donem d, string belgeTuru, string kanun, string mahiyet, EvrakTuru evrak)
        => $"{d.BasAy:00}_{Aylar[d.BasAy - 1]}_{belgeTuru}_{kanun}_{mahiyet}_{(evrak == EvrakTuru.SgkTahakkuk ? "TAHAKKUK" : "HIZMET_LISTESI")}";
}
