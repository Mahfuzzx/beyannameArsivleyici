using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Arsiv;

namespace GibEvrakIndirici.Tests;

/// <summary>Toplu yazdırma listesi (§9.9).</summary>
public class ArsivListesiTestleri : IDisposable
{
    readonly string _k = Path.Combine(Path.GetTempPath(), "gib-liste-" + Guid.NewGuid().ToString("N"));
    static readonly Musteri A = new() { KisaAd = "ÖRNEK A", VergiNo = "1111111111" };
    static readonly Musteri B = new() { KisaAd = "ÖRNEK B", VergiNo = "2222222222" };
    static readonly EvrakTuru[] Hepsi = [EvrakTuru.Beyanname, EvrakTuru.Tahakkuk, EvrakTuru.Ihbarname];

    public ArsivListesiTestleri() => Directory.CreateDirectory(_k);
    public void Dispose() => Directory.Delete(_k, true);

    void Dosya(Musteri m, int yil, string tur, string ad)
    {
        var k = Path.Combine(_k, m.YKlasor, yil.ToString(), tur);
        Directory.CreateDirectory(k);
        File.WriteAllText(Path.Combine(k, ad), "x");
    }

    [Fact]
    public void DonemVeTureGoreBulur()
    {
        Dosya(A, 2026, "KDV1", "10_EKIM_KDV1_BEYANNAME_26.11.2026_10_00_00.pdf");
        Dosya(A, 2026, "KDV1", "10_EKIM_KDV1_TAHAKKUK_26.11.2026_10_00_00.pdf");
        Dosya(A, 2026, "KDV1", "10_EKIM_KDV1_IHBARNAME_26.11.2026_10_00_00.pdf");
        Dosya(A, 2026, "KDV1", "10_EKIM_KDV1_IHBARNAME2_26.11.2026_10_00_00.pdf");
        Dosya(A, 2026, "KDV1", "10_EKIM_KDV1_TAHAKKUK_MAHSUP.pdf");      // elle eklenen
        Dosya(A, 2026, "KDV1", "09_EYLUL_KDV1_TAHAKKUK_26.10.2026_10_00_00.pdf"); // başka dönem
        Dosya(B, 2026, "KDV1", "10_EKIM_KDV1_TAHAKKUK_25.11.2026_10_00_00.pdf");

        var d = ArsivListesi.DonemOlustur(DonemTipi.Aylik, 2026, 10);
        var t = ArsivListesi.Bul([B, A], _k, "KDV1", d, [EvrakTuru.Tahakkuk], yalnizSon: false);
        Assert.Equal(["10_EKIM_KDV1_TAHAKKUK_26.11.2026_10_00_00.pdf", "10_EKIM_KDV1_TAHAKKUK_25.11.2026_10_00_00.pdf"], t.Select(x => x.Ad));

        var i = ArsivListesi.Bul([A], _k, "KDV1", d, [EvrakTuru.Ihbarname], yalnizSon: false);
        Assert.Equal(2, i.Count);
        Assert.Equal(4, ArsivListesi.Bul([A], _k, "KDV1", d, Hepsi, yalnizSon: false).Count);
    }

    [Fact]
    public void YalnizSonSurum()
    {
        Dosya(A, 2026, "MUHSGK", "09_EYLUL_MUHSGK_TAHAKKUK_20.10.2026_10_00_00.pdf");
        Dosya(A, 2026, "MUHSGK", "09_EYLUL_MUHSGK_TAHAKKUK_22.10.2026_10_00_00_DÜZ.pdf");
        Dosya(A, 2026, "MUHSGK", "09_EYLUL_MUHSGK_TAHAKKUK_23.10.2026_10_00_00_DÜZ2.pdf");
        var d = ArsivListesi.DonemOlustur(DonemTipi.Aylik, 2026, 9);

        Assert.Equal(3, ArsivListesi.Bul([A], _k, "MUHSGK", d, Hepsi, yalnizSon: false).Count);
        var son = Assert.Single(ArsivListesi.Bul([A], _k, "MUHSGK", d, Hepsi, yalnizSon: true));
        Assert.Equal(2, son.Surum);
    }

    [Fact]
    public void CeyrekVeYillikEtiket()
    {
        Dosya(A, 2026, "GGECICI", "03_DONEM_GGECICI_TAHAKKUK_17.11.2026_10_00_00.pdf");
        Dosya(A, 2025, "KURUMLAR", "2025_KURUMLAR_BEYANNAME_30.04.2026_10_00_00.pdf");

        var c = ArsivListesi.DonemOlustur(DonemTipi.UcAylik, 2026, 8);
        Assert.Equal(new Donem(2026, 7, 2026, 9), c);
        Assert.Single(ArsivListesi.Bul([A], _k, "GGECICI", c, Hepsi, true));
        Assert.Single(ArsivListesi.Bul([A], _k, "KURUMLAR", ArsivListesi.DonemOlustur(DonemTipi.Yillik, 2025, 1), Hepsi, true));
    }

    void SgkDosya(Musteri m, int yil, string isyeri, string ad)
    {
        var k = Path.Combine(_k, "X", yil.ToString(), m.XKlasor, isyeri);
        Directory.CreateDirectory(k);
        File.WriteAllText(Path.Combine(k, ad), "x");
    }

    [Fact]
    public void SgkTumIsyerlerindenListelenir()
    {
        Dosya(A, 2026, "MUHSGK", "09_EYLUL_MUHSGK_TAHAKKUK_20.10.2026_10_00_00.pdf");
        SgkDosya(A, 2026, "MERKEZ", "09_EYLUL_01_05510_ASIL_HIZMET_LISTESI_20261020100000.pdf");
        SgkDosya(A, 2026, "MERKEZ", "09_EYLUL_01_05510_ASIL_TAHAKKUK_20261020100000.pdf");
        SgkDosya(A, 2026, "MERKEZ", "09_EYLUL_01_05510_EK1_TAHAKKUK_20261025100000.pdf");
        SgkDosya(A, 2026, "A_SUBE", "09_EYLUL_02_00000_ASIL_TAHAKKUK_20261020100000.pdf");
        SgkDosya(A, 2026, "A_SUBE", "09_EYLUL_01_05510_ASIL_TAHAKKUK_ÖD.pdf");    // elle eklenen
        SgkDosya(A, 2026, "A_SUBE", "08_AGUSTOS_01_05510_ASIL_TAHAKKUK_20260920100000.pdf"); // başka ay
        var d = ArsivListesi.DonemOlustur(DonemTipi.Aylik, 2026, 9);

        var l = ArsivListesi.Bul([A], _k, "MUHSGK", d, [EvrakTuru.Tahakkuk, EvrakTuru.SgkTahakkuk, EvrakTuru.SgkHizmetListesi], true,
            xKok: Path.Combine(_k, "X"));

        Assert.Equal(
            ["09_EYLUL_MUHSGK_TAHAKKUK_20.10.2026_10_00_00.pdf",
             "09_EYLUL_02_00000_ASIL_TAHAKKUK_20261020100000.pdf",          // A_SUBE
             "09_EYLUL_01_05510_ASIL_TAHAKKUK_20261020100000.pdf",          // MERKEZ: tahakkuklar
             "09_EYLUL_01_05510_EK1_TAHAKKUK_20261025100000.pdf",
             "09_EYLUL_01_05510_ASIL_HIZMET_LISTESI_20261020100000.pdf"],  // sonra hizmet listesi
            l.Select(x => x.Ad));
        Assert.Equal("01/05510 EK1", l[3].SgkBilgi);
        Assert.Equal("MERKEZ", l[3].Isyeri);

        // Yalnız hizmet listesi
        Assert.Single(ArsivListesi.Bul([A], _k, "MUHSGK", d, [EvrakTuru.SgkHizmetListesi], true, xKok: Path.Combine(_k, "X")));
    }

    [Fact]
    public void UcAylikMuhtasardaSgkHerAy()
    {
        SgkDosya(A, 2026, "MERKEZ", "07_TEMMUZ_01_05510_ASIL_TAHAKKUK_20260820100000.pdf");
        SgkDosya(A, 2026, "MERKEZ", "09_EYLUL_01_05510_ASIL_TAHAKKUK_20261020100000.pdf");
        var d = ArsivListesi.DonemOlustur(DonemTipi.UcAylik, 2026, 9);
        var l = ArsivListesi.Bul([A], _k, "MUHSGK", d, [EvrakTuru.SgkTahakkuk], true, xKok: Path.Combine(_k, "X"));
        Assert.Equal(["07/2026 01/05510 ASIL", "09/2026 01/05510 ASIL"], l.Select(x => x.SgkBilgi));
    }

    [Theory]
    [InlineData("x_BEYANNAME_01.01.2026_10_00_00", 0)]
    [InlineData("x_BEYANNAME_01.01.2026_10_00_00_DÜZ", 1)]
    [InlineData("x_BEYANNAME_01.01.2026_10_00_00_DÜZ3", 3)]
    public void SurumNo(string ad, int n) => Assert.Equal(n, ArsivListesi.SurumNo(ad));
}
