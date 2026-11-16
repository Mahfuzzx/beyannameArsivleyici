using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using GibEvrakIndirici.Core.Adlandirma;
using GibEvrakIndirici.Core.Sgk;

namespace GibEvrakIndirici.Core.Luca;

/// <summary>Kullanıcının açtığı Luca Mali Müşavir Paketi oturumu üzerinden GET istekleri (WebView2 içinde).</summary>
public interface ILucaTasiyici
{
    /// <summary>GET https://auygs.luca.com.tr/Luca/{yol} — UTF-8 metin.</summary>
    Task<string> GetAsync(string yol, CancellationToken ct);
    /// <summary>Üst çerçevedeki seçili firma/dönem (iş bitince geri dönmek için).</summary>
    Task<(string SirketId, string DonemId, string DonemTxt)?> AktifFirmaAsync(CancellationToken ct);
}

public sealed record LucaFirma(string SirketId, string FirmaKisaAd);

/// <summary>
/// Luca'dan tüm firmaların SGK işyerlerini toplu çeker (§11.0):
///  1) editEBildirgeGirisDosyaAction → firma–işyeri çiftleri + SIRKET_ID (kimlik bilgileri OKUNMAZ)
///  2) her firma: TopFrameAction?SIRKET_ID= → listIsyeriAction → Kısa Adı / Sicil / Başlama / Bitiş
///  3) kullanıcının asıl firmasına geri dönülür
/// Yalnızca okuma yapar.
/// </summary>
public sealed class LucaIsyeriAktarici(ILucaTasiyici tasiyici)
{
    static readonly Regex TrRx = new(@"<tr[^>]*>([\s\S]*?)</tr>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex TdRx = new(@"<td[^>]*>([\s\S]*?)</td>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex SirketRx = new(@"name=""SIRKET_ID""[^>]*value=""(\d+)""|value=""(\d+)""[^>]*name=""SIRKET_ID""", RegexOptions.Compiled);
    static readonly Regex SicilRx = new(@"^\d{26}$", RegexOptions.Compiled);

    static string Metin(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();

    /// <summary>"E-Bildirge Giriş Dosyası" sayfasından firmaları çıkarır. Satırlardaki kullanıcı adı/şifre alanlarına dokunmaz.</summary>
    public static List<LucaFirma> FirmalariAyristir(string html)
    {
        var firmalar = new Dictionary<string, LucaFirma>();
        foreach (Match tr in TrRx.Matches(html))
        {
            var ic = tr.Groups[1].Value;
            if (ic.Contains("<tr", StringComparison.OrdinalIgnoreCase)) continue;
            var sm = SirketRx.Match(ic);
            if (!sm.Success) continue;
            var id = sm.Groups[1].Success ? sm.Groups[1].Value : sm.Groups[2].Value;
            var ilk = TdRx.Match(ic);
            if (!ilk.Success) continue;
            var etiket = Metin(ilk.Groups[1].Value);           // "ÖR TEMİZLİ - MERKEZ"
            var ayir = etiket.LastIndexOf(" - ", StringComparison.Ordinal);
            var firma = ayir > 0 ? etiket[..ayir] : etiket;
            firmalar.TryAdd(id, new LucaFirma(id, firma));
        }
        return firmalar.Values.ToList();
    }

    /// <summary>listIsyeriAction yanıtından işyerlerini çıkarır.</summary>
    public static List<Isyeri> IsyerleriniAyristir(string html, string firmaKisaAd)
    {
        var liste = new List<Isyeri>();
        foreach (Match tr in TrRx.Matches(html))
        {
            var ic = tr.Groups[1].Value;
            if (ic.Contains("<tr", StringComparison.OrdinalIgnoreCase)) continue;
            var h = TdRx.Matches(ic).Select(m => Metin(m.Groups[1].Value)).ToList();
            int s = h.FindIndex(x => SicilRx.IsMatch(x));
            if (s < 2) continue;
            liste.Add(new Isyeri
            {
                FirmaKisaAd = firmaKisaAd,
                KisaAd = h[s - 2],
                UzunAd = h[s - 1],
                Sicil = h[s],
                Baslama = Tarih(h.Skip(s + 1).FirstOrDefault(x => Tarih(x) is not null)),
                Bitis = h.Skip(s + 1).Select(Tarih).Where(x => x is not null).Skip(1).FirstOrDefault(),
            });
        }
        return liste;
    }

    static DateTime? Tarih(string? s) =>
        DateTime.TryParseExact(s?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public async Task<List<Isyeri>> AktarAsync(string uyeNo, IProgress<string>? ilerleme, CancellationToken ct)
    {
        var onceki = await tasiyici.AktifFirmaAsync(ct);
        var sonuc = new List<Isyeri>();
        try
        {
            var firmalar = FirmalariAyristir(await tasiyici.GetAsync($"editEBildirgeGirisDosyaAction.do?c_musteri_id={uyeNo}", ct));
            int i = 0;
            foreach (var f in firmalar)
            {
                ct.ThrowIfCancellationRequested();
                ilerleme?.Report($"Luca işyerleri: {++i}/{firmalar.Count} {f.FirmaKisaAd}");
                await tasiyici.GetAsync($"TopFrameAction.do?time={Zaman()}&SIRKET_ID={f.SirketId}", ct);
                var html = await tasiyici.GetAsync($"listIsyeriAction.do?c_musteri_id={uyeNo}&time={Zaman()}", ct);
                sonuc.AddRange(IsyerleriniAyristir(html, f.FirmaKisaAd));
            }
        }
        finally
        {
            if (onceki is { } o)
                await tasiyici.GetAsync($"TopFrameAction.do?time={Zaman()}&SIRKET_ID={o.SirketId}&DONEM_ID={o.DonemId}&DONEM_TXT={Uri.EscapeDataString(o.DonemTxt)}", CancellationToken.None);
        }
        return sonuc;
    }

    static long Zaman() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Luca işyerlerini sicil deposuna ekler (müşteri X klasörü = firma kısa adından).</summary>
    public static int DepoyaEkle(IsyeriDeposu depo, IEnumerable<Isyeri> isyerleri)
    {
        int n = 0;
        foreach (var i in isyerleri)
        {
            depo.Ekle(new IsyeriDeposu.Kayit(KlasorAdi.XIcin(i.FirmaKisaAd), KlasorAdi.XIcin(i.KisaAd), i.Sicil, "Luca", i.Bitis));
            n++;
        }
        return n;
    }
}
