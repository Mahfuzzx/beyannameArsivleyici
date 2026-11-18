using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Arsiv;
using GibEvrakIndirici.Core.Ebyn;
using GibEvrakIndirici.Core.Indirme;
using GibEvrakIndirici.Core.Musteriler;
using GibEvrakIndirici.Core.Sgk;

namespace GibEvrakIndirici.Tests;

/// <summary>Liste penceresi dışında kalan asıl beyanname arşivden tanınır → düzeltme _DÜZ olur (§9.2).</summary>
public class DuzeltmeArsivTestleri : IDisposable
{
    readonly string _k = Path.Combine(Path.GetTempPath(), "gib-duz-" + Guid.NewGuid().ToString("N"));
    public DuzeltmeArsivTestleri() => Directory.CreateDirectory(_k);
    public void Dispose() => Directory.Delete(_k, true);
    void Dosya(string ad) => File.WriteAllText(Path.Combine(_k, ad), "x");

    const string Onek = "2020_KURUMLAR_BEYANNAME";
    static readonly DateTime Yukleme = new(2026, 11, 9, 15, 10, 29);

    [Fact]
    public void EskiDamgalarSayilir()
    {
        Dosya("2020_KURUMLAR_BEYANNAME_22.04.2021_14_36_57.pdf");   // yeni biçim
        Dosya("2020_KURUMLAR_BEYANNAME_20210423101010_DÜZ.pdf");    // eski damga + düzeltme
        Dosya("2020_KURUMLAR_BEYANNAME_MAHSUP.pdf");                // sayılmaz
        Dosya("2020_KURUMLAR_BEYANNAME.pdf");                        // damgasız → belirsiz, sayılmaz
        Dosya("2020_KURUMLAR_TAHAKKUK_22.04.2021_14_36_57.pdf");    // başka önek
        Assert.Equal(2, ArsivDenetci.OncekiYuklemeSayisi(_k, Onek, Yukleme));
    }

    [Fact]
    public void AyniBeyannameninKendiDosyasiSayilmaz()
    {
        Dosya("2020_KURUMLAR_BEYANNAME_09.11.2026_15_10_29.pdf");   // aynı damga
        Dosya("2020_KURUMLAR_BEYANNAME_09.11.2026.pdf");             // aynı gün, elle (yalnız tarih)
        Assert.Equal(0, ArsivDenetci.OncekiYuklemeSayisi(_k, Onek, Yukleme.AddMinutes(-10)));
    }

    [Fact]
    public void YalnizTarihliOncekiGunSayilir()
    {
        Dosya("2020_KURUMLAR_BEYANNAME_08.11.2026.pdf");
        Assert.Equal(1, ArsivDenetci.OncekiYuklemeSayisi(_k, Onek, Yukleme));
    }

    sealed class PdfTasiyici : IEbynTasiyici
    {
        public Task<string> PostAsync(string p, CancellationToken ct) => Task.FromResult("");
        public Task<byte[]> GetAsync(string u, CancellationToken ct) => Task.FromResult("%PDF-1.4 x"u8.ToArray());
    }

    [Fact]
    public async Task PencereDisiAsilVarsaDuzeltmeIndirilir()
    {
        var ayar = new Ayarlar { YArsivKok = _k, XArsivKok = _k, OnayZamaniTurleri = "" };
        var md = new MusteriDeposu();
        md.Yukle([new() { KisaAd = "ORNEK", VergiNo = "1111111111" }]);
        var klasor = Path.Combine(_k, "ORNEK", "2020", "KURUMLAR");
        Directory.CreateDirectory(klasor);
        File.WriteAllText(Path.Combine(klasor, "2020_KURUMLAR_BEYANNAME_22.04.2021_14_36_57.pdf"), "eski");
        File.WriteAllText(Path.Combine(klasor, "2020_KURUMLAR_TAHAKKUK_22.04.2021_14_36_57.pdf"), "eski");

        var ind = new Indirici(new EbynIstemci(new PdfTasiyici()) { IstekAraligi = TimeSpan.Zero }, md, new IsyeriDeposu(), ayar,
            _ => Task.FromResult<string?>(null));
        var satir = new BeyannameSatiri
        {
            Oid = "o", Tur = "KURUMLAR", VknTckn = "1111111111", Unvan = "ORNEK", TahakkukOid = "t",
            Donem = Donem.Ayristir("01/2020-12/2020"), YuklemeZamani = Yukleme,
        };
        Assert.Equal(1, ind.Durum(satir, 0).DuzeltmeSira);
        Assert.False(ind.Durum(satir, 0).Beyanname);

        var ozet = await ind.CalistirAsync([satir], new Progress<IndirmeOlayi>(), default);
        Assert.Equal(2, ozet.Indirilen);
        Assert.True(File.Exists(Path.Combine(klasor, "2020_KURUMLAR_BEYANNAME_09.11.2026_15_10_29_DÜZ.pdf")));
        Assert.True(File.Exists(Path.Combine(klasor, "2020_KURUMLAR_TAHAKKUK_09.11.2026_15_10_29_DÜZ.pdf")));
        Assert.Equal("eski", File.ReadAllText(Path.Combine(klasor, "2020_KURUMLAR_BEYANNAME_22.04.2021_14_36_57.pdf")));

        // ikinci çalıştırmada aynı dosyalar "zaten var"
        var ikinci = await ind.CalistirAsync([satir], new Progress<IndirmeOlayi>(), default);
        Assert.Equal(0, ikinci.Indirilen);
    }
}
