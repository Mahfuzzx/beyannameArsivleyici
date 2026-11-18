using GibEvrakIndirici.Core.Ebyn;

namespace GibEvrakIndirici.Tests;

/// <summary>GİB PDF isteği sayacı (§12.5).</summary>
public class IstekSayaciTestleri : IDisposable
{
    readonly string _k = Path.Combine(Path.GetTempPath(), "gib-sayac-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_k)) Directory.Delete(_k, true); }

    [Fact]
    public void PencerelerVeKalicilik()
    {
        var simdi = new DateTime(2026, 11, 18, 23, 30, 0);
        var s = new IstekSayaci(_k, () => simdi);
        simdi = new DateTime(2026, 11, 18, 21, 0, 0); s.Kaydet(IstekSonucu.Pdf);   // 2,5 saat önce
        simdi = new DateTime(2026, 11, 18, 23, 0, 0); s.Kaydet(IstekSonucu.Pdf);
        simdi = new DateTime(2026, 11, 18, 23, 10, 0); s.Kaydet(IstekSonucu.Bos);
        simdi = new DateTime(2026, 11, 19, 0, 20, 0); s.Kaydet(IstekSonucu.Pdf);   // ertesi gün

        Assert.Equal(new IstekSayaci.Ozet(Bugun: 1, BugunBos: 0, Son60Dk: 1, Son24Saat: 4), s.Al());
        Assert.Equal(4, s.ProgramToplami);

        // Program yeniden açılınca dosyalardan okunur
        var yeni = new IstekSayaci(_k, () => simdi);
        Assert.Equal(s.Al(), yeni.Al());
        Assert.Equal(0, yeni.ProgramToplami);
    }

    sealed class Tasiyici(params byte[][] yanitlar) : IEbynTasiyici
    {
        int i;
        public Task<string> PostAsync(string p, CancellationToken ct) => Task.FromResult("");
        public Task<byte[]> GetAsync(string u, CancellationToken ct) => Task.FromResult(yanitlar[i++]);
    }

    [Fact]
    public async Task IstemciHerIstegiSayarVeKisitlamadaOzetVerir()
    {
        var sayac = new IstekSayaci(_k);
        var pdf = "%PDF-1.4 x"u8.ToArray();
        var istemci = new EbynIstemci(new Tasiyici(pdf, [], []))
            { IstekAraligi = TimeSpan.Zero, BosYanitBeklemesi = TimeSpan.Zero, Sayac = sayac };

        await istemci.PdfAsync(EbynKomutlari.BeyannamePdf("o"), default);
        var ex = await Assert.ThrowsAsync<EbynKisitlamaHatasi>(() => istemci.PdfAsync(EbynKomutlari.BeyannamePdf("o"), default));

        var o = sayac.Al();
        Assert.Equal(3, o.Bugun);
        Assert.Equal(2, o.BugunBos);
        Assert.Contains("bugün 3 (boş yanıt 2)", ex.Message);
    }
}
