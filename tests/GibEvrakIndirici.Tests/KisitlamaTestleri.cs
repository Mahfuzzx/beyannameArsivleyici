using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Ebyn;
using GibEvrakIndirici.Core.Indirme;
using GibEvrakIndirici.Core.Musteriler;
using GibEvrakIndirici.Core.Sgk;

namespace GibEvrakIndirici.Tests;

/// <summary>GİB boş yanıt (kısıtlama) ve oturum zaman aşımı davranışı (§12.1).</summary>
public class KisitlamaTestleri
{
    const string ZamanAsimi = "<SERVICERESULT><EYEKSERROR>Oturum zamanaşımına uğradı, yeni oturum açınız.</EYEKSERROR></SERVICERESULT>";
    static readonly byte[] Pdf = "%PDF-1.4 x"u8.ToArray();

    /// <summary>İlk <paramref name="bosSayisi"/> GET boş döner, sonra PDF.</summary>
    sealed class Tasiyici(int bosSayisi, string? getYaniti = null) : IEbynTasiyici
    {
        public int Get;
        public Task<string> PostAsync(string p, CancellationToken ct) => Task.FromResult(ZamanAsimi);
        public Task<byte[]> GetAsync(string u, CancellationToken ct)
        {
            Get++;
            if (getYaniti != null) return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(getYaniti));
            return Task.FromResult(Get <= bosSayisi ? [] : Pdf);
        }
    }

    static EbynIstemci Istemci(IEbynTasiyici t) => new(t) { IstekAraligi = TimeSpan.Zero, BosYanitBeklemesi = TimeSpan.Zero };

    [Fact]
    public void EyeksErrorOturumDustuSayilir()
    {
        var y = EbynYanit.Ayristir(ZamanAsimi);
        Assert.True(y.OturumDustu);
        Assert.False(y.Basarili);
        Assert.Contains("zamanaşımı", y.Hata);
    }

    [Fact]
    public void BosEyeksErrorNormalYanittir()
    {
        var y = EbynYanit.Ayristir("<SERVICERESULT><TOKEN>abc</TOKEN><SERVERERROR></SERVERERROR><EYEKSERROR></EYEKSERROR><HTMLCONTENT>x</HTMLCONTENT></SERVICERESULT>");
        Assert.False(y.OturumDustu);
        Assert.True(y.Basarili);
    }

    [Fact]
    public async Task PostOturumHatasiAtar()
        => await Assert.ThrowsAsync<EbynOturumHatasi>(() => Istemci(new Tasiyici(0)).PostAsync("cmd=BEYANNAMELISTESI", default));

    [Fact]
    public async Task IkiBosYanittaKisitlamaHatasi()
    {
        var t = new Tasiyici(99);
        var ex = await Assert.ThrowsAsync<EbynKisitlamaHatasi>(() => Istemci(t).PdfAsync(EbynKomutlari.BeyannamePdf("o"), default));
        Assert.Equal(2, t.Get); // sunucuyu yormamak için 3. deneme yapılmaz
        Assert.Contains("boş yanıt", ex.Message);
    }

    [Fact]
    public async Task TekBosYanittanSonraPdfAlinir()
    {
        var t = new Tasiyici(1);
        Assert.Equal(Pdf, await Istemci(t).PdfAsync(EbynKomutlari.BeyannamePdf("o"), default));
    }

    [Fact]
    public async Task PdfIsteginde_OturumDusunceHemenDurur()
    {
        var t = new Tasiyici(0, ZamanAsimi);
        await Assert.ThrowsAsync<EbynOturumHatasi>(() => Istemci(t).PdfAsync(EbynKomutlari.BeyannamePdf("o"), default));
        Assert.Equal(1, t.Get);
    }

    static (Indirici, List<TimeSpan>, string) Kur(IEbynTasiyici t, string beklemeler)
    {
        var kok = Path.Combine(Path.GetTempPath(), "gib-kis-" + Guid.NewGuid().ToString("N"));
        var ayar = new Ayarlar { YArsivKok = kok, XArsivKok = kok, KisitlamaBeklemeleriDk = beklemeler, OnayZamaniTurleri = "" };
        var md = new MusteriDeposu();
        md.Yukle([new() { KisaAd = "ABC", VergiNo = "1111111111" }]);
        var bekleme = new List<TimeSpan>();
        var ind = new Indirici(Istemci(t), md, new IsyeriDeposu(), ayar, _ => Task.FromResult<string?>(null),
            bekle: (s, _) => { bekleme.Add(s); return Task.CompletedTask; });
        return (ind, bekleme, kok);
    }

    static BeyannameSatiri Satir(string oid, int dk) => new()
    {
        Oid = oid, Tur = "GGECICI", VknTckn = "1111111111", Unvan = "ABC", TahakkukOid = "t" + oid,
        Donem = Donem.Ayristir("07/2026-09/2026"), YuklemeZamani = new DateTime(2026, 11, 3, 13, dk, 0),
    };

    [Fact]
    public async Task KisitlamadaBekleyipAyniEvraklaDevamEder()
    {
        // 1. satır: beyanname OK, tahakkuk 2 boş (kısıtlama) → bekle → tahakkuk OK; 2. satır normal
        var t = new SiraliTasiyici([Pdf, [], [], Pdf, Pdf, Pdf]);
        var (ind, bekleme, kok) = Kur(t, "2,5");
        try
        {
            var ozet = await ind.CalistirAsync([Satir("a", 1), Satir("b", 2)], new Progress<IndirmeOlayi>(), default);
            Assert.Equal([TimeSpan.FromMinutes(2)], bekleme);
            Assert.Equal(4, ozet.Indirilen);
            Assert.Equal(0, ozet.Hata);
            Assert.False(ozet.Durduruldu);
        }
        finally { if (Directory.Exists(kok)) Directory.Delete(kok, true); }
    }

    [Fact]
    public async Task KisitlamaSurerseDurur()
    {
        var t = new Tasiyici(int.MaxValue);
        var (ind, bekleme, kok) = Kur(t, "2,5");
        try
        {
            var ozet = await ind.CalistirAsync([Satir("a", 1), Satir("b", 2)], new Progress<IndirmeOlayi>(), default);
            Assert.True(ozet.Durduruldu);
            Assert.Equal([TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)], bekleme);
            Assert.Equal(1, ozet.Hata);
            Assert.Equal(6, t.Get); // 3 tur × 2 deneme; 2. satıra hiç geçilmez
        }
        finally { if (Directory.Exists(kok)) Directory.Delete(kok, true); }
    }

    sealed class SiraliTasiyici(byte[][] yanitlar) : IEbynTasiyici
    {
        int i;
        public Task<string> PostAsync(string p, CancellationToken ct) => Task.FromResult("");
        public Task<byte[]> GetAsync(string u, CancellationToken ct) => Task.FromResult(yanitlar[i++]);
    }
}
