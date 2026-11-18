using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Arsiv;
using GibEvrakIndirici.Core.Ebyn;
using GibEvrakIndirici.Core.Indirme;
using GibEvrakIndirici.Core.Musteriler;
using GibEvrakIndirici.Core.Sgk;

namespace GibEvrakIndirici.Tests;

/// <summary>SGK/ihbarname evrakları tamamlanan beyanname için GİB'e tekrar alt liste sorgusu atılmaz.</summary>
public class TamamlananTestleri : IDisposable
{
    readonly string _k = Path.Combine(Path.GetTempPath(), "gib-tam-" + Guid.NewGuid().ToString("N"));
    public TamamlananTestleri() => Directory.CreateDirectory(_k);
    public void Dispose() => Directory.Delete(_k, true);

    // "Vergi" satırı (PDF'siz, sayılmaz) + PDF'li bir SGK bildirimi; isteğe bağlı ikinci bildirim PDF'siz (işlemde)
    static string SgkHtml(bool islemdeVar) =>
        """<tr id="rowv" title="SGK Bildirimleri Listesi"><td>10/2026-10/2026</td><td>Vergi</td></tr>""" +
        """<tr id="rows1" title="SGK Bildirimleri Listesi"><td>10/2026-10/2026</td><td>Asıl, TÜM SİG.KOLLARI, 05510-Say.Kan., 10 / 2026, 1</td>""" +
        """<td><img onclick="sgkTahakkukGoruntule('b','s1',false)"/></td><td><img onclick="sgkHizmetGoruntule('b','s1',false)"/></td></tr>""" +
        (islemdeVar ? """<tr id="rows2" title="SGK Bildirimleri Listesi"><td>10/2026-10/2026</td><td>Ek, TÜM SİG.KOLLARI, 05510-Say.Kan., 10 / 2026, 1</td></tr>""" : "");

    sealed class SenkronIlerleme(Action<IndirmeOlayi> a) : IProgress<IndirmeOlayi> { public void Report(IndirmeOlayi o) => a(o); }

    sealed class Tasiyici(bool islemdeVar) : IEbynTasiyici
    {
        public int Post;
        public Task<string> PostAsync(string p, CancellationToken ct)
        {
            Post++;
            return Task.FromResult($"<SERVICERESULT><TOKEN>t</TOKEN><SERVERERROR></SERVERERROR><EYEKSERROR></EYEKSERROR><HTMLCONTENT>{SgkHtml(islemdeVar)}</HTMLCONTENT></SERVICERESULT>");
        }
        public Task<byte[]> GetAsync(string u, CancellationToken ct) => Task.FromResult("%PDF-1.4 x"u8.ToArray());
    }

    (Indirici, Ayarlar) Kur(IEbynTasiyici t, TamamlananDeposu d)
    {
        // SGK evrakları arşivde zaten var (sahte PDF'ten sicil okunamayacağı için indirme yolu burada denenmez)
        var x = Path.Combine(_k, "2026", "ORNEK", "MERKEZ");
        Directory.CreateDirectory(x);
        File.WriteAllText(Path.Combine(x, "10_EKIM_01_05510_ASIL_TAHAKKUK_20261104150000.pdf"), "x");
        File.WriteAllText(Path.Combine(x, "10_EKIM_01_05510_ASIL_HIZMET_LISTESI_20261104150000.pdf"), "x");
        var ayar = new Ayarlar { YArsivKok = _k, XArsivKok = _k, OnayZamaniTurleri = "" };
        var md = new MusteriDeposu();
        md.Yukle([new() { KisaAd = "ORNEK", VergiNo = "1111111111" }]);
        return (new Indirici(new EbynIstemci(t) { IstekAraligi = TimeSpan.Zero }, md, new IsyeriDeposu(), ayar,
            _ => Task.FromResult<string?>(null), tamamlanan: d), ayar);
    }

    static BeyannameSatiri Satir() => new()
    {
        Oid = "b", Tur = "MUHSGK", VknTckn = "1111111111", Unvan = "ORNEK", TahakkukOid = "t", SgkBildirimVar = true,
        Donem = Donem.Ayristir("10/2026-10/2026"), YuklemeZamani = new DateTime(2026, 11, 4, 15, 0, 0),
    };

    [Fact]
    public async Task TamamlanincaIkinciCalistirmadaSorguYok()
    {
        var t = new Tasiyici(islemdeVar: false);
        var d = new TamamlananDeposu();
        var (ind, _) = Kur(t, d);
        var olaylar = new List<string>();
        var ilk = await ind.CalistirAsync([Satir()], new SenkronIlerleme(o => olaylar.Add($"{o.Seviye}: {o.Mesaj}")), default);
        Assert.True(ilk.Indirilen == 2, string.Join("\n", olaylar)); // B, T (SGK zaten var)
        Assert.Equal(0, ilk.Uyari);     // "Vergi" satırı uyarı vermez
        Assert.Equal(1, t.Post);

        var ikinci = await ind.CalistirAsync([Satir()], new Progress<IndirmeOlayi>(), default);
        Assert.Equal(0, ikinci.Indirilen);
        Assert.Equal(1, t.Post); // SGK listesi tekrar sorgulanmadı

        // diske yazılıp açılınca da geçerli
        var dosya = Path.Combine(_k, "tamamlanan.json");
        d.Kaydet(dosya);
        Assert.Equal(2, TamamlananDeposu.Ac(dosya).Sayi); // SGK + SATIR
    }

    [Fact]
    public async Task IslemdeBildirimVarsaYenidenSorgulanir()
    {
        var t = new Tasiyici(islemdeVar: true);
        var (ind, _) = Kur(t, new TamamlananDeposu());
        await ind.CalistirAsync([Satir()], new Progress<IndirmeOlayi>(), default);
        await ind.CalistirAsync([Satir()], new Progress<IndirmeOlayi>(), default);
        Assert.Equal(2, t.Post);
    }

    [Fact]
    public void AnahtarArsivKokunuIcerir()
    {
        var test = new Ayarlar { YArsivKok = @"D:\dev\y\", XArsivKok = @"D:\dev\x\" };
        var gercek = new Ayarlar { YArsivKok = @"Y:\", XArsivKok = @"X:\" };
        Assert.NotEqual(TamamlananDeposu.Anahtar(test, "b", "SGK"), TamamlananDeposu.Anahtar(gercek, "b", "SGK"));
    }
}
