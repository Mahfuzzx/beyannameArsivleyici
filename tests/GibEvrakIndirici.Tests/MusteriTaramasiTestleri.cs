using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Ebyn;
using GibEvrakIndirici.Core.Indirme;

namespace GibEvrakIndirici.Tests;

/// <summary>Seçili müşteri × tür × vergilendirme dönemi taraması (§9.5).</summary>
public class MusteriTaramasiTestleri
{
    static string Satir(string oid, string tur, string donem) =>
        $"""<tr id="row{oid}" title="x.xml 18.10.2026 - 16:24:25"><td></td><td>{tur}</td><td>1111111111</td><td title="ÖRNEK">ÖRNEK</td><td>VD</td><td>{donem}</td><td>Merkez</td><td>18.10.2026 - 16:24:25</td><td><table><tr><td id="durumTD{oid}">Onaylandı</td></tr></table></td></tr>""";

    static string Yanit(string html, string hata = "") =>
        $"<SERVICERESULT><TOKEN>t</TOKEN><SERVERERROR>{hata}</SERVERERROR><EYEKSERROR></EYEKSERROR><HTMLCONTENT>{html}</HTMLCONTENT></SERVICERESULT>";

    sealed class Tasiyici(Func<string, string> yanit) : IEbynTasiyici
    {
        public List<string> Postlar { get; } = [];
        public Task<string> PostAsync(string p, CancellationToken ct) { Postlar.Add(p); return Task.FromResult(yanit(p)); }
        public Task<byte[]> GetAsync(string u, CancellationToken ct) => Task.FromResult(Array.Empty<byte>());
    }

    [Fact]
    public async Task TurBasinaSorguVeDonemSuzgeci()
    {
        var t = new Tasiyici(p => p.Contains("beyannameTanim=KDV1")
            ? Yanit($"<b>1 - 3 / 3</b><table>{Satir("a", "KDV1", "12/2024-12/2024")}{Satir("b", "KDV1", "01/2025-01/2025")}{Satir("c", "KDV1", "06/2025-06/2025")}</table>")
            : Yanit("", "Aradığınız kriterlere uygun beyanname bulunamamıştır."));
        var ist = new EbynIstemci(t) { IstekAraligi = TimeSpan.Zero };

        var l = await ist.MukellefBeyannameleriAsync("1111111111", ["KDV1", "MUHSGK"], new Donem(2025, 1, 2025, 6), new DateTime(2026, 11, 18), null, default);

        Assert.Equal(["b", "c"], l.Select(x => x.Oid).Order()); // 12/2024 aralık dışı
        Assert.Equal(2, t.Postlar.Count);
        var p = t.Postlar[0];
        Assert.Contains("sorguTipiN=1&vergiNo=1111111111", p);
        Assert.Contains("sorguTipiB=1&beyannameTanim=KDV1", p);
        Assert.Contains("donemBasYil=2025&donemBasAy=1&donemBitYil=2025&donemBitAy=6", p);
        Assert.Contains("baslangicTarihi=20250101&bitisTarihi=20261118", p);
        Assert.Contains("sorguTipiD=1&durum=2", p);
    }

    [Fact]
    public async Task TumSayfalarGezilir()
    {
        var t = new Tasiyici(p => p.Contains("pageNo=2")
            ? Yanit($"<b>26 - 26 / 26</b><table>{Satir("z", "KDV1", "03/2025-03/2025")}</table>")
            : Yanit($"<b>1 - 25 / 26</b><table>{string.Concat(Enumerable.Range(0, 25).Select(i => Satir("s" + i, "KDV1", "02/2025-02/2025")))}</table>"));
        var l = await new EbynIstemci(t) { IstekAraligi = TimeSpan.Zero }
            .MukellefBeyannameleriAsync("12345678901", ["KDV1"], new Donem(2025, 1, 2025, 12), new DateTime(2026, 1, 1), null, default);
        Assert.Equal(26, l.Count);
        Assert.Equal(2, t.Postlar.Count);
        Assert.Contains("sorguTipiT=1&tcKimlikNo=12345678901", t.Postlar[0]);
    }

    [Fact]
    public void SahistaTcknOncelikli()
    {
        Assert.Equal("12345678901", MusteriTaramasi.Kimlik(new Musteri { KisaAd = "A", VergiNo = "1111111111", Tckn = "12345678901" }));
        Assert.Equal("1111111111", MusteriTaramasi.Kimlik(new Musteri { KisaAd = "A", VergiNo = "1111111111" }));
        Assert.Null(MusteriTaramasi.Kimlik(new Musteri { KisaAd = "A" }));
    }

    [Fact]
    public void VarsayilanTurlerArsivKlasorlerindenGelir()
    {
        var kok = Path.Combine(Path.GetTempPath(), "gib-tur-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(kok, "ORNEK", "2025", "KDV2"));
            Directory.CreateDirectory(Path.Combine(kok, "ORNEK", "2025", "KURUMLAR"));
            Directory.CreateDirectory(Path.Combine(kok, "ORNEK", "2023", "POSET"));    // aralık dışı yıl
            var l = MusteriTaramasi.VarsayilanTurler([new Musteri { KisaAd = "ORNEK" }], new Ayarlar { YArsivKok = kok }, new Donem(2025, 1, 2026, 6));
            Assert.Equal(["KDV1", "KDV2", "MUHSGK", "GGECICI", "KGECICI", "KURUMLAR"], l);
        }
        finally { Directory.Delete(kok, true); }
    }
}
