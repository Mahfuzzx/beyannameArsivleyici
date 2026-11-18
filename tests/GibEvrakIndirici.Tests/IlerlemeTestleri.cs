using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Ebyn;
using GibEvrakIndirici.Core.Indirme;
using GibEvrakIndirici.Core.Musteriler;
using GibEvrakIndirici.Core.Sgk;

namespace GibEvrakIndirici.Tests;

/// <summary>İlerleme çubuğu bildirimleri: (biten, toplam) 0'dan toplama kadar artar.</summary>
public class IlerlemeTestleri
{
    sealed class Senkron<T>(List<T> l) : IProgress<T> { public void Report(T v) => l.Add(v); }

    sealed class Tasiyici : IEbynTasiyici
    {
        public Task<string> PostAsync(string p, CancellationToken ct) =>
            Task.FromResult("<SERVICERESULT><TOKEN>t</TOKEN><SERVERERROR>Aradığınız kriterlere uygun beyanname bulunamamıştır.</SERVERERROR><HTMLCONTENT></HTMLCONTENT></SERVICERESULT>");
        public Task<byte[]> GetAsync(string u, CancellationToken ct) => Task.FromResult("%PDF-1.4 x"u8.ToArray());
    }

    [Fact]
    public async Task IndirmeSatirSatirBildirilir()
    {
        var kok = Path.Combine(Path.GetTempPath(), "gib-ilr-" + Guid.NewGuid().ToString("N"));
        try
        {
            var md = new MusteriDeposu();
            md.Yukle([new() { KisaAd = "ORNEK", VergiNo = "1111111111" }]);
            var ind = new Indirici(new EbynIstemci(new Tasiyici()) { IstekAraligi = TimeSpan.Zero }, md, new IsyeriDeposu(),
                new Ayarlar { YArsivKok = kok, XArsivKok = kok }, _ => Task.FromResult<string?>(null));
            var satirlar = Enumerable.Range(0, 3).Select(i => new BeyannameSatiri
            {
                Oid = "o" + i, Tur = "GGECICI", VknTckn = i == 1 ? "9999999999" : "1111111111", Unvan = "u", TahakkukOid = "t" + i,
                Donem = Donem.Ayristir("07/2026-09/2026"), YuklemeZamani = new DateTime(2026, 11, 3, 13, i, 0),
            }).ToList();
            var bildirimler = new List<(int, int)>();
            await ind.CalistirAsync(satirlar, new Progress<IndirmeOlayi>(), default, new Senkron<(int, int)>(bildirimler));
            Assert.Equal([(0, 3), (0, 3), (1, 3), (2, 3), (3, 3)], bildirimler);
        }
        finally { if (Directory.Exists(kok)) Directory.Delete(kok, true); }
    }

    [Fact]
    public async Task ListelemeAyAyBildirilir()
    {
        var b = new List<(int, int)>();
        await new EbynIstemci(new Tasiyici()) { IstekAraligi = TimeSpan.Zero }
            .OnayliBeyannamelerAsync(new DateTime(2026, 9, 15), new DateTime(2026, 11, 18), null, default, new Senkron<(int, int)>(b));
        Assert.Equal([(0, 3), (1, 3), (2, 3), (3, 3)], b);
    }

    [Fact]
    public async Task MusteriTaramasiTurBasinaBildirir()
    {
        int n = 0;
        await new EbynIstemci(new Tasiyici()) { IstekAraligi = TimeSpan.Zero }
            .MukellefBeyannameleriAsync("1111111111", ["KDV1", "MUHSGK", "GGECICI"], new Donem(2025, 1, 2025, 12), new DateTime(2026, 1, 1), null, default, () => n++);
        Assert.Equal(3, n);
    }
}
