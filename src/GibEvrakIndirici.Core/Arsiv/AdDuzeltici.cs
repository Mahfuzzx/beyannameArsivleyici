using System.Text.RegularExpressions;
using GibEvrakIndirici.Core.Adlandirma;

namespace GibEvrakIndirici.Core.Arsiv;

/// <summary>Arşiv dosya adının biçimi (§9.8).</summary>
public enum AdBicimi
{
    /// <summary>{etiket}_{TÜR}_{BELGE}_{dd.MM.yyyy_HH_mm_ss}[_DÜZn] — dokunulmaz.</summary>
    Standart,
    /// <summary>Önekli ama zamanı standart değil (damgasız, yyyyMMddHHmmss, XML adlı). Program bunları önek sayımıyla zaten tanır.</summary>
    Onekli,
    /// <summary>Önek yok: çok eski program (".xml_BYN/_THK") ya da elle verilmiş ad ("2025 02 B"). Program bunları tanımaz.</summary>
    Eski,
    /// <summary>Ödeme, mahsup, alındı, ihbarname vb.: hiç dokunulmaz.</summary>
    Haric
}

public enum OneriDurumu
{
    /// <summary>Yeni ad önerildi.</summary>
    Onerilen,
    /// <summary>Yeni adla bir dosya zaten var (ya da aynı klasörde başka bir dosya aynı ada eşleşti) → bu dosya kopya olabilir.</summary>
    Kopya,
    /// <summary>Birden fazla GİB sürümüne uyuyor.</summary>
    Belirsiz,
    /// <summary>GİB listesindeki hiçbir sürüme uymadı (dönem aralık dışı, başka mükellef, beyanname/tahakkuk değil).</summary>
    Eslesmedi,
    /// <summary>PDF metni yok (taranmış resim) ya da açılamadı.</summary>
    Okunamadi,
    /// <summary>Sürüm bulundu ama dosya başka yıl/tür klasöründe; yeniden adlandırılmaz.</summary>
    YanlisKlasor
}

/// <param name="Klasor">Dosyanın bulunduğu klasör.</param>
/// <param name="EskiAd">Mevcut dosya adı (.pdf ile).</param>
/// <param name="YeniAd">Önerilen ad (.pdf ile); yalnız <see cref="OneriDurumu.Onerilen"/> ve <see cref="OneriDurumu.Kopya"/>'da dolu.</param>
public sealed record AdOnerisi(string Klasor, string EskiAd, string? YeniAd, OneriDurumu Durum, AdBicimi Bicim, string Tur, string Aciklama)
{
    public string EskiYol => Path.Combine(Klasor, EskiAd);
    public string? YeniYol => YeniAd is null ? null : Path.Combine(Klasor, YeniAd);
}

/// <summary>
/// Arşivdeki standart dışı adlı beyanname/tahakkuk PDF'lerine, PDF içeriği ve GİB listesine göre doğru adı önerir (§9.8).
/// Hiçbir dosyaya dokunmaz; uygulama <see cref="AdDuzeltmeGunlugu"/> ile, kullanıcı onayından sonra yapılır.
/// <list type="bullet">
/// <item>Beyanname: PDF'teki onay zamanı, GİB'deki sürümlerden birinin [yükleme − 1 dk, min(sonraki yükleme, yükleme + 1 gün)) aralığına düşmeli;
///   PDF'te mükellefin VKN/TCKN'si geçmeli. Ad zamanı: KDV türlerinde onay zamanı, diğerlerinde GİB yükleme zamanı; düzeltme eki GİB sırasından.</item>
/// <item>Tahakkuk: zaman bilgisi yok → önce adının beyanname karşılığı (".xml_THK" ↔ ".xml_BYN", " T" ↔ " B", "_TAHAKKUK_" ↔ "_BEYANNAME_")
///   aynı klasörde aranır ve onun sürümünü alır; yoksa PDF'teki dönem + kabul tarihiyle tek sürüme iniyorsa o.</item>
/// </list>
/// </summary>
public static class AdDuzeltici
{
    static readonly Regex StandartRx = new(
        @"^(\d{2}_[A-Z]+|\d{2}_DONEM|\d{4})_[A-Z0-9]+_(BEYANNAME|TAHAKKUK|IHBARNAME\d*)_\d{2}\.\d{2}\.\d{4}_\d{2}_\d{2}_\d{2}(_DÜZ\d*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex OnekliRx = new(
        @"^(\d{2}_[A-Z]+|\d{2}_DONEM|\d{4})_[A-Z0-9]+_(?<belge>BEYANNAME|TAHAKKUK|IHBARNAME\d*)(_|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly string[] HaricEkler = ["ÖD", "MAHSUP", "SPEC_HARICI", ".xml_ALN", "KOPYA", "ODEME", "ÖDEME", "DEKONT"];

    // Türkçe karaktersiz yazılmış ödeme işareti: "…_TAHAKKUK_OD", "2025 03 T OD" (tek başına duran OD)
    static readonly Regex OdemeRx = new(@"(?<=^|[ _\-.])OD(?=$|[ _\-.])", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static AdBicimi Bicim(string adPdfsiz)
    {
        if (StandartRx.IsMatch(adPdfsiz)) return AdBicimi.Standart;
        if (HaricEkler.Any(e => adPdfsiz.Contains(e, StringComparison.OrdinalIgnoreCase)) || OdemeRx.IsMatch(adPdfsiz)) return AdBicimi.Haric;
        var m = OnekliRx.Match(adPdfsiz);
        if (m.Success) return m.Groups["belge"].Value.StartsWith("IHBARNAME") ? AdBicimi.Haric : AdBicimi.Onekli;
        return AdBicimi.Eski;
    }

    /// <summary>Bir tahakkuk adının olası beyanname karşılıkları (aynı klasörde aranır).</summary>
    public static IEnumerable<string> BeyannameKarsiliklari(string tahakkukPdfsiz)
    {
        if (tahakkukPdfsiz.Contains(".xml_THK", StringComparison.OrdinalIgnoreCase))
            yield return Regex.Replace(tahakkukPdfsiz, @"\.xml_THK", ".xml_BYN", RegexOptions.IgnoreCase);
        if (tahakkukPdfsiz.Contains("_TAHAKKUK_", StringComparison.Ordinal) || tahakkukPdfsiz.EndsWith("_TAHAKKUK", StringComparison.Ordinal))
            yield return Regex.Replace(tahakkukPdfsiz, @"_TAHAKKUK(?=_|$)", "_BEYANNAME");
        // Elle verilmiş kısa ad: "2025 02 T", "05 2017 T 170630", "2024_T" → tek başına duran T harfi B olur
        var kisa = Regex.Replace(tahakkukPdfsiz, @"(?<=^|[ _])T(?=$|[ _])", "B");
        if (kisa != tahakkukPdfsiz) yield return kisa;
    }

    sealed record Surum(BeyannameSatiri B, int Sira, DateTime? Sonraki);

    /// <summary>
    /// Bir müşterinin Y arşivindeki seçili tür klasörlerini (yıl aralığında) tarar ve ad önerileri üretir.
    /// </summary>
    /// <param name="gib">Bu müşterinin GİB'deki onaylı beyannameleri (dönem başından bugüne yüklenenler; tüm sürümler).</param>
    /// <param name="oku">PDF içeriğini okur (testte sahte verilebilir).</param>
    /// <param name="oneklileriDeKat">Önekli ama zamanı standart olmayan adlar da önerilsin mi.</param>
    public static List<AdOnerisi> Oner(Musteri m, IReadOnlyList<BeyannameSatiri> gib, Ayarlar ayarlar, IEnumerable<string> turler,
        int basYil, int bitYil, Func<string, EvrakIcerigi> oku, bool oneklileriDeKat, CancellationToken ct = default)
    {
        var sonuc = new List<AdOnerisi>();
        var kimlikler = gib.Select(b => b.VknTckn).Append(m.Tckn).Append(m.VergiNo)
            .Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToList();
        var surumler = gib.GroupBy(b => (Tur: DosyaAdlari.TurKlasoru(b.Tur), b.Donem))
            .SelectMany(g =>
            {
                var l = g.OrderBy(b => b.YuklemeZamani).ToList();
                return l.Select((b, i) => new Surum(b, i, i + 1 < l.Count ? l[i + 1].YuklemeZamani : null));
            })
            .ToList();

        foreach (var tur in turler.Select(DosyaAdlari.TurKlasoru).Distinct())
        {
            var turSurumleri = surumler.Where(s => DosyaAdlari.TurKlasoru(s.B.Tur) == tur).ToList();
            for (int yil = basYil; yil <= bitYil; yil++)
            {
                ct.ThrowIfCancellationRequested();
                var klasor = Path.Combine(ayarlar.YArsivKok, m.YKlasor, yil.ToString(), tur);
                if (!Directory.Exists(klasor)) continue;
                sonuc.AddRange(KlasorOner(klasor, tur, turSurumleri, kimlikler, ayarlar, oku, oneklileriDeKat));
            }
        }
        return sonuc;
    }

    static List<AdOnerisi> KlasorOner(string klasor, string tur, List<Surum> surumler, List<string> kimlikler, Ayarlar ayarlar,
        Func<string, EvrakIcerigi> oku, bool oneklileriDeKat)
    {
        var dosyalar = Directory.EnumerateFiles(klasor, "*.pdf", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList();
        var mevcut = new HashSet<string>(dosyalar, StringComparer.OrdinalIgnoreCase);
        var adaylar = dosyalar.Select(a => (Ad: a, Bicim: Bicim(a)))
            .Where(x => x.Bicim == AdBicimi.Eski || (oneklileriDeKat && x.Bicim == AdBicimi.Onekli))
            .ToList();
        var onerilen = new List<AdOnerisi>();
        // Beyanname adı (pdf'siz) → eşleştiği sürüm; tahakkuklar eşini buradan bulur
        var beyannameSurumu = new Dictionary<string, Surum>(StringComparer.OrdinalIgnoreCase);
        var icerikler = new Dictionary<string, EvrakIcerigi>(StringComparer.OrdinalIgnoreCase);
        EvrakIcerigi Icerik(string ad) => icerikler.TryGetValue(ad, out var i) ? i : icerikler[ad] = oku(Path.Combine(klasor, ad + ".pdf"));
        AdOnerisi Oneri(string ad, AdBicimi b, string? yeni, OneriDurumu d, string acik) => new(klasor, ad + ".pdf", yeni, d, b, tur, acik);

        // Standart adlı (ya da eşleşmiş) beyanname: içeriğindeki onay tek sürüme uyuyorsa o sürüm
        Surum? BeyannameninSurumu(string ad)
        {
            if (beyannameSurumu.TryGetValue(ad, out var s)) return s;
            if (!mevcut.Contains(ad) || Bicim(ad) != AdBicimi.Standart) return null;
            if (Icerik(ad).Onay is not { } o) return null;
            var u = surumler.Where(x => OnayAraliginda(o, x)).ToList();
            return u.Count == 1 ? beyannameSurumu[ad] = u[0] : null;
        }
        // Bir sürümün arşivdeki beyannamesinin onay zamanı (KDV'de tahakkuk adı bu zamanı taşır)
        DateTime? SurumOnayi(Surum su)
        {
            foreach (var (ad, s) in beyannameSurumu)
                if (s == su && Icerik(ad).Onay is { } o) return o;
            var onek = DosyaAdlari.BeyannameOnek(su.B, EvrakTuru.Beyanname);
            foreach (var ad in dosyalar.Where(a => a.StartsWith(onek + "_", StringComparison.OrdinalIgnoreCase)))
                if (BeyannameninSurumu(ad) == su && Icerik(ad).Onay is { } o) return o;
            return null;
        }
        // Bu sürümün bu evrakı arşivde başka bir standart adla (ör. eski programın yükleme zamanlı adı) zaten var mı?
        // Tahakkuk, aynı adlı beyanname eşinin sürümüyle tanınır.
        string? SurumunStandardi(Surum su, EvrakTuru e)
        {
            var onek = DosyaAdlari.BeyannameOnek(su.B, e);
            foreach (var ad in dosyalar.Where(a => a.StartsWith(onek + "_", StringComparison.OrdinalIgnoreCase) && Bicim(a) == AdBicimi.Standart))
            {
                var beyanname = e == EvrakTuru.Beyanname ? ad : Regex.Replace(ad, "_TAHAKKUK(?=_)", "_BEYANNAME");
                if (BeyannameninSurumu(beyanname) == su) return ad;
            }
            return null;
        }
        AdOnerisi OneriVeyaKopya(string ad, AdBicimi b, string yeni, Surum su, EvrakTuru e, string acik) =>
            SurumunStandardi(su, e) is { } mevcutAd && !string.Equals(mevcutAd, yeni, StringComparison.OrdinalIgnoreCase)
                ? Oneri(ad, b, yeni + ".pdf", OneriDurumu.Kopya, $"bu sürüm arşivde {mevcutAd}.pdf adıyla zaten var — kopya olabilir")
                : Oneri(ad, b, yeni + ".pdf", OneriDurumu.Onerilen, acik);

        // 1) Beyannameler
        var tahakkuklar = new List<(string Ad, AdBicimi Bicim, EvrakIcerigi Ic)>();
        foreach (var (ad, bicim) in adaylar)
        {
            var ic = Icerik(ad);
            switch (ic.Tur)
            {
                case IcerikTuru.Okunamadi:
                    onerilen.Add(Oneri(ad, bicim, null, OneriDurumu.Okunamadi, "PDF metni okunamadı (taranmış olabilir)"));
                    continue;
                case IcerikTuru.Diger:
                    onerilen.Add(Oneri(ad, bicim, null, OneriDurumu.Eslesmedi, "beyanname ya da tahakkuk değil"));
                    continue;
                case IcerikTuru.Tahakkuk:
                    tahakkuklar.Add((ad, bicim, ic));
                    continue;
            }
            if (!kimlikler.Any(ic.KimlikIcerir))
            {
                onerilen.Add(Oneri(ad, bicim, null, OneriDurumu.Eslesmedi, "PDF'teki VKN/TCKN bu müşterinin değil"));
                continue;
            }
            var onay = ic.Onay!.Value;
            var uyan = surumler.Where(s => OnayAraliginda(onay, s)).ToList();
            if (uyan.Count == 0)
            {
                onerilen.Add(Oneri(ad, bicim, null, OneriDurumu.Eslesmedi, $"onay {onay:dd.MM.yyyy HH:mm:ss} hiçbir GİB sürümüne uymadı"));
                continue;
            }
            if (uyan.Count > 1)
            {
                onerilen.Add(Oneri(ad, bicim, null, OneriDurumu.Belirsiz, $"onay {onay:dd.MM.yyyy HH:mm:ss} {uyan.Count} sürüme uyuyor"));
                continue;
            }
            var su = uyan[0];
            if (!KlasorUyar(klasor, su))
            {
                onerilen.Add(Oneri(ad, bicim, null, OneriDurumu.YanlisKlasor, $"{su.B.Tur} {su.B.Donem} sürümü; beklenen klasör {DosyaAdlari.YilKlasoru(su.B.Donem)}\\{tur}"));
                continue;
            }
            beyannameSurumu[ad] = su;
            var yeni = StandartAd(su, EvrakTuru.Beyanname, ayarlar, onay);
            onerilen.Add(OneriVeyaKopya(ad, bicim, yeni, su, EvrakTuru.Beyanname, $"onay {onay:dd.MM.yyyy HH:mm:ss} → {su.B.Donem} {SiraMetni(su.Sira)}, yükleme {su.B.YuklemeZamani:dd.MM.yyyy HH:mm}"));
        }

        // 2) Tahakkuklar: eşi olan beyannamenin sürümü, yoksa PDF'teki dönem + kabul tarihi
        foreach (var (ad, bicim, ic) in tahakkuklar)
        {
            if (!kimlikler.Any(ic.KimlikIcerir))
            {
                onerilen.Add(Oneri(ad, bicim, null, OneriDurumu.Eslesmedi, "PDF'teki VKN/TCKN bu müşterinin değil"));
                continue;
            }
            Surum? su = null;
            string dayanak = "";
            foreach (var es in BeyannameKarsiliklari(ad))
                if (BeyannameninSurumu(es) is { } s) { su = s; dayanak = $"eşi: {es}.pdf"; break; }

            if (su is not null && ic.Donem is { } d0 && d0 != su.B.Donem)
            {
                onerilen.Add(Oneri(ad, bicim, null, OneriDurumu.Belirsiz, $"eşinin dönemi {su.B.Donem}, tahakkukta {d0}"));
                continue;
            }
            if (su is null)
            {
                if (ic.Donem is not { } d)
                {
                    onerilen.Add(Oneri(ad, bicim, null, OneriDurumu.Eslesmedi, "eşi yok, tahakkukta dönem okunamadı"));
                    continue;
                }
                var ayni = surumler.Where(s => s.B.Donem == d).ToList();
                if (ayni.Count > 1 && ic.KabulTarihi is { } k)
                    ayni = ayni.Where(s => k >= s.B.YuklemeZamani.Date && k <= (s.Sonraki is { } n && n < s.B.YuklemeZamani.AddDays(1) ? n : s.B.YuklemeZamani.AddDays(1)).Date).ToList();
                if (ayni.Count != 1)
                {
                    onerilen.Add(Oneri(ad, bicim, null, ayni.Count == 0 ? OneriDurumu.Eslesmedi : OneriDurumu.Belirsiz,
                        ayni.Count == 0 ? $"eşi yok, {d} için GİB sürümü yok" : $"eşi yok, {d} döneminde {ayni.Count} sürüm"));
                    continue;
                }
                su = ayni[0];
                dayanak = $"tahakkuktaki dönem {d}" + (ic.KabulTarihi is { } kt ? $", kabul {kt:dd.MM.yyyy}" : "");
                if (!KlasorUyar(klasor, su))
                {
                    onerilen.Add(Oneri(ad, bicim, null, OneriDurumu.YanlisKlasor, $"{su.B.Tur} {su.B.Donem} sürümü; beklenen klasör {DosyaAdlari.YilKlasoru(su.B.Donem)}\\{tur}"));
                    continue;
                }
            }
            // Zaman beyannameninkiyle aynı olmalı: KDV'de onay zamanı (eşin PDF'inden) → eşin önerilen/standart adından türetilir
            var esOnay = SurumOnayi(su);
            if (ayarlar.OnayZamaniKullanilir(su.B.Tur) && esOnay is null)
            {
                onerilen.Add(Oneri(ad, bicim, null, OneriDurumu.Belirsiz, $"{su.B.Donem} {SiraMetni(su.Sira)}: beyannamesi arşivde okunamadı, onay zamanı bilinmiyor"));
                continue;
            }
            var yeni = StandartAd(su, EvrakTuru.Tahakkuk, ayarlar, esOnay);
            onerilen.Add(OneriVeyaKopya(ad, bicim, yeni, su, EvrakTuru.Tahakkuk, $"{dayanak} → {su.B.Donem} {SiraMetni(su.Sira)}"));
        }

        // 3) Çakışmalar: hedef ad diskte varsa ya da iki dosya aynı ada çıktıysa → kopya (ikincisinden itibaren)
        var alinan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < onerilen.Count; i++)
        {
            var o = onerilen[i];
            if (o.Durum != OneriDurumu.Onerilen) continue;
            var yeniPdfsiz = Path.GetFileNameWithoutExtension(o.YeniAd!);
            if (mevcut.Contains(yeniPdfsiz))
                onerilen[i] = o with { Durum = OneriDurumu.Kopya, Aciklama = $"{o.YeniAd} arşivde zaten var — bu dosya kopya olabilir" };
            else if (!alinan.Add(yeniPdfsiz))
                onerilen[i] = o with { Durum = OneriDurumu.Kopya, Aciklama = $"başka bir dosya da {o.YeniAd} adına eşleşti — kopya olabilir" };
        }
        return onerilen;
    }

    static bool OnayAraliginda(DateTime onay, Surum s)
    {
        var yuk = s.B.YuklemeZamani;
        var ust = s.Sonraki is { } n && n < yuk + Indirme.Indirici.EnUzunOnaySuresi ? n : yuk + Indirme.Indirici.EnUzunOnaySuresi;
        return onay >= yuk.AddMinutes(-1) && onay < ust;
    }

    static bool KlasorUyar(string klasor, Surum s) =>
        string.Equals(Path.GetFileName(Path.GetDirectoryName(klasor)), DosyaAdlari.YilKlasoru(s.B.Donem).ToString(), StringComparison.Ordinal);

    static string StandartAd(Surum s, EvrakTuru e, Ayarlar ayarlar, DateTime? onay)
    {
        var zaman = ayarlar.OnayZamaniKullanilir(s.B.Tur) && onay is not null ? onay : s.B.YuklemeZamani;
        return Path.GetFileNameWithoutExtension(DosyaAdlari.BeyannameDosyasi(s.B, e, s.Sira, zaman: zaman));
    }

    static string SiraMetni(int sira) => sira switch { 0 => "asıl", 1 => "1. düzeltme", _ => $"{sira}. düzeltme" };
}
