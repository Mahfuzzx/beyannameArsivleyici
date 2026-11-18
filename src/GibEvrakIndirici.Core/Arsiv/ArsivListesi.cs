using System.Text.RegularExpressions;
using GibEvrakIndirici.Core.Adlandirma;

namespace GibEvrakIndirici.Core.Arsiv;

public enum DonemTipi { Aylik, UcAylik, Yillik }

/// <param name="Surum">0 = asıl, 1 = _DÜZ, 2 = _DÜZ2 … (SGK'da hep 0)</param>
/// <param name="Isyeri">SGK: X arşivindeki işyeri klasörü (MERKEZ, şube…); beyannamelerde boş.</param>
/// <param name="SgkBilgi">SGK: "01/05510 ASIL" gibi belge türü / kanun / mahiyet.</param>
public sealed record ArsivEvraki(Musteri Musteri, EvrakTuru Evrak, string Yol, int Surum, string Isyeri = "", string SgkBilgi = "")
{
    public string Ad => Path.GetFileName(Yol);
    public bool Sgk => Evrak is EvrakTuru.SgkTahakkuk or EvrakTuru.SgkHizmetListesi;
}

/// <summary>
/// Bir tür + dönem için müşterilerin Y arşivindeki beyanname / tahakkuk / ihbarname PDF'lerini listeler (toplu yazdırma, §9.9).
/// Yalnız önekli adlar bulunur ({etiket}_{TÜR}_{BELGE}…); eski adlı dosyalar için önce ad düzeltme (§9.8) yapılmalı.
/// Ödeme/mahsup gibi elle eklenenler ve _KOPYA alt klasörleri listelenmez.
/// </summary>
public static class ArsivListesi
{
    static readonly Regex BelgeRx = new(@"^_(?<b>BEYANNAME|TAHAKKUK|IHBARNAME\d*)(_|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex DuzRx = new(@"_DÜZ(?<n>\d*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Türün olağan dönem tipi (kullanıcı değiştirebilir).</summary>
    public static DonemTipi VarsayilanTip(string tur) => DosyaAdlari.TurKlasoru(tur) switch
    {
        "GGECICI" or "KGECICI" => DonemTipi.UcAylik,
        "GELIR" or "KURUMLAR" => DonemTipi.Yillik,
        _ => DonemTipi.Aylik
    };

    /// <summary>Seçilen ay/yıl ve tipten dönem: 3 aylıkta ayın düştüğü çeyrek, yıllıkta o yıl.</summary>
    public static Donem DonemOlustur(DonemTipi tip, int yil, int ay) => tip switch
    {
        DonemTipi.UcAylik => new Donem(yil, (ay - 1) / 3 * 3 + 1, yil, (ay - 1) / 3 * 3 + 3),
        DonemTipi.Yillik => new Donem(yil, 1, yil, 12),
        _ => new Donem(yil, ay, yil, ay)
    };

    public static int SurumNo(string adPdfsiz)
    {
        var m = DuzRx.Match(adPdfsiz);
        return !m.Success ? 0 : m.Groups["n"].Value.Length == 0 ? 1 : int.Parse(m.Groups["n"].Value);
    }

    static readonly Regex SgkRx = new(@"^_(?<bt>\d{2})_(?<kn>\d{5})_(?<mh>ASIL|EK\d+|İPTAL\d+)_(?<b>TAHAKKUK|HIZMET_LISTESI)(_|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <param name="yalnizSon">Her müşteri ve evrak türü için yalnız en son sürüm (en büyük _DÜZ); ihbarnamelerde o sürümün hepsi.
    /// SGK'ya uygulanmaz: asıl / ek / iptal bildirgelerin her biri ayrı belgedir.</param>
    /// <param name="xKok">SGK evrakları istenirse X arşivi kökü: X:\{yıl}\{müşteri}\{işyeri}\{AA}_{AY}_{BT}_{KANUN}_{ASIL|EKn|İPTALn}_{BELGE}_… (§8.2).
    /// Dönemin her ayı ayrı aranır (3 aylık muhtasarda SGK yine aylıktır).</param>
    public static List<ArsivEvraki> Bul(IEnumerable<Musteri> musteriler, string yKok, string tur, Donem d,
        IReadOnlyCollection<EvrakTuru> evraklar, bool yalnizSon, CancellationToken ct = default, string? xKok = null)
    {
        var sonuc = BeyannameleriBul(musteriler, yKok, tur, d, evraklar, yalnizSon, ct);
        bool sgkT = evraklar.Contains(EvrakTuru.SgkTahakkuk), sgkH = evraklar.Contains(EvrakTuru.SgkHizmetListesi);
        if (xKok is null || !(sgkT || sgkH)) return sonuc;

        var sgk = new List<ArsivEvraki>();
        foreach (var m in musteriler)
            for (int a = d.BasYil * 12 + d.BasAy - 1; a <= d.BitYil * 12 + d.BitAy - 1; a++)
            {
                ct.ThrowIfCancellationRequested();
                int yil = a / 12, ay = a % 12 + 1;
                var musteriKlasoru = Path.Combine(xKok, yil.ToString(), m.XKlasor);
                if (!Directory.Exists(musteriKlasoru)) continue;
                var onek = $"{ay:00}_{DosyaAdlari.Aylar[ay - 1]}";
                foreach (var isyeriKlasoru in Directory.EnumerateDirectories(musteriKlasoru).Order(StringComparer.CurrentCulture))
                {
                    var isyeri = Path.GetFileName(isyeriKlasoru);
                    foreach (var ad in ArsivDenetci.Dosyalar(isyeriKlasoru, onek))
                    {
                        var sm = SgkRx.Match(ad[onek.Length..]);
                        if (!sm.Success) continue;
                        var e = sm.Groups["b"].Value == "TAHAKKUK" ? EvrakTuru.SgkTahakkuk : EvrakTuru.SgkHizmetListesi;
                        if (e == EvrakTuru.SgkTahakkuk ? !sgkT : !sgkH) continue;
                        sgk.Add(new ArsivEvraki(m, e, Path.Combine(isyeriKlasoru, ad + ".pdf"), 0, isyeri,
                            $"{(d.AySayisi > 1 ? $"{ay:00}/{yil} " : "")}{sm.Groups["bt"].Value}/{sm.Groups["kn"].Value} {sm.Groups["mh"].Value}"));
                    }
                }
            }
        // Müşteri › (beyanname evrakları) › SGK: işyeri › ay › tahakkuk/hizmet listesi › ad
        return sonuc.Concat(sgk)
            .OrderBy(x => x.Musteri.KisaAd, StringComparer.CurrentCulture)
            .ThenBy(x => x.Sgk)
            .ThenBy(x => x.Sgk ? x.Isyeri : "", StringComparer.CurrentCulture)
            .ThenBy(x => x.Sgk ? Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(x.Yol))) : "", StringComparer.Ordinal) // yıl
            .ThenBy(x => x.Sgk ? x.Ad[..2] : "", StringComparer.Ordinal)                                                            // ay
            .ThenBy(x => x.Sgk ? (int)x.Evrak : 0)
            .ThenBy(x => x.Sgk ? x.Ad : "", StringComparer.Ordinal)
            .ToList();
    }

    static List<ArsivEvraki> BeyannameleriBul(IEnumerable<Musteri> musteriler, string yKok, string tur, Donem d,
        IReadOnlyCollection<EvrakTuru> evraklar, bool yalnizSon, CancellationToken ct)
    {
        var sonuc = new List<ArsivEvraki>();
        var turK = DosyaAdlari.TurKlasoru(tur);
        var onek = $"{DosyaAdlari.DonemEtiketi(d)}_{turK}";
        foreach (var m in musteriler.OrderBy(x => x.KisaAd, StringComparer.CurrentCulture))
        {
            ct.ThrowIfCancellationRequested();
            var klasor = Path.Combine(yKok, m.YKlasor, DosyaAdlari.YilKlasoru(d).ToString(), turK);
            var bulunan = new List<ArsivEvraki>();
            foreach (var ad in ArsivDenetci.Dosyalar(klasor, onek))
            {
                var bm = BelgeRx.Match(ad[onek.Length..]);
                if (!bm.Success) continue;
                var b = bm.Groups["b"].Value;
                var e = b == "BEYANNAME" ? EvrakTuru.Beyanname : b == "TAHAKKUK" ? EvrakTuru.Tahakkuk : EvrakTuru.Ihbarname;
                if (!evraklar.Contains(e)) continue;
                bulunan.Add(new ArsivEvraki(m, e, Path.Combine(klasor, ad + ".pdf"), SurumNo(ad)));
            }
            if (yalnizSon)
                bulunan = bulunan.GroupBy(x => x.Evrak).SelectMany(g =>
                {
                    var enSon = g.Max(x => x.Surum);
                    var son = g.Where(x => x.Surum == enSon);
                    // Aynı sürümden birden fazla beyanname/tahakkuk (eski kopya) → en son yazılanı
                    return g.Key == EvrakTuru.Ihbarname ? son : son.OrderByDescending(x => File.GetLastWriteTimeUtc(x.Yol)).Take(1);
                }).ToList();
            sonuc.AddRange(bulunan.OrderBy(x => x.Evrak).ThenBy(x => x.Surum).ThenBy(x => x.Ad, StringComparer.Ordinal));
        }
        return sonuc;
    }
}
