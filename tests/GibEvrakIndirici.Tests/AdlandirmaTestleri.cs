using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Adlandirma;
using GibEvrakIndirici.Core.Arsiv;

namespace GibEvrakIndirici.Tests;

public class AdlandirmaTestleri
{
    // Gerçek arşivde doğrulanan örnekler (§8.3)
    [Theory]
    [InlineData("ÖRNEŞ GIDA", "ÖRNEŞ_GIDA", "ORNES_GIDA")]
    [InlineData("ÖR TEMİZLİ", "ÖR_TEMİZLİ", "OR_TEM_ZL_")]
    [InlineData("SELİMILKAN", "SELİMILKAN", "SEL_MILKAN")]
    [InlineData("AHMETKAÇ", "AHMETKAÇ", "AHMETKAC")]
    [InlineData("GİYÖRNEK", "GİYÖRNEK", "G_YORNEK")]
    [InlineData("VELİDURSUN", "VELİDURSUN", "VEL_DURSUN")]
    [InlineData("DOĞRUL B", "DOĞRUL_B", "DOGRUL_B")]
    [InlineData("ÜZENGİ MTL", "ÜZENGİ_MTL", "UZENG__MTL")]
    public void KlasorAdlari(string kisa, string x, string y)
    {
        Assert.Equal(x, KlasorAdi.XIcin(kisa));
        Assert.Equal(y, KlasorAdi.YIcin(kisa));
    }

    static BeyannameSatiri Satir(string tur, string donem, string yukleme) => new()
    {
        Oid = "x", Tur = tur, VknTckn = "1234567890", Unvan = "TEST",
        Donem = Donem.Ayristir(donem),
        YuklemeZamani = DateTime.ParseExact(yukleme, "dd.MM.yyyy HH:mm:ss", null),
    };

    [Theory]
    [InlineData("KDV4", "09/2026-09/2026", "27.10.2026 15:06:52", EvrakTuru.Beyanname, 0, "09_EYLUL_KDV4_BEYANNAME_27.10.2026_15_06_52.pdf")]
    [InlineData("KDV1", "09/2026-09/2026", "27.10.2026 15:06:52", EvrakTuru.Tahakkuk, 0, "09_EYLUL_KDV1_TAHAKKUK_27.10.2026_15_06_52.pdf")]
    [InlineData("KGECICI", "07/2026-09/2026", "12.11.2026 15:48:49", EvrakTuru.Beyanname, 0, "03_DONEM_KGECICI_BEYANNAME_12.11.2026_15_48_49.pdf")]
    [InlineData("KURUMLAR", "01/2025-12/2025", "22.04.2026 18:22:38", EvrakTuru.Tahakkuk, 0, "2025_KURUMLAR_TAHAKKUK_22.04.2026_18_22_38.pdf")]
    [InlineData("KDV1", "01/2026-01/2026", "18.02.2026 12:59:29", EvrakTuru.Beyanname, 1, "01_OCAK_KDV1_BEYANNAME_18.02.2026_12_59_29_DÜZ.pdf")]
    [InlineData("KDV1", "01/2026-01/2026", "19.02.2026 10:00:00", EvrakTuru.Tahakkuk, 2, "01_OCAK_KDV1_TAHAKKUK_19.02.2026_10_00_00_DÜZ2.pdf")]
    [InlineData("KDV1", "05/2024-05/2024", "18.10.2026 16:24:25", EvrakTuru.Ihbarname, 0, "05_MAYIS_KDV1_IHBARNAME_18.10.2026_16_24_25.pdf")]
    public void BeyannameDosyaAdlari(string tur, string donem, string yukleme, EvrakTuru e, int duz, string beklenen)
        => Assert.Equal(beklenen, DosyaAdlari.BeyannameDosyasi(Satir(tur, donem, yukleme), e, duz));

    [Fact]
    public void IkinciIhbarnameNumaralanir()
        => Assert.Equal("05_MAYIS_KDV1_IHBARNAME2_18.10.2026_16_24_25.pdf",
            DosyaAdlari.BeyannameDosyasi(Satir("KDV1", "05/2024-05/2024", "18.10.2026 16:24:25"), EvrakTuru.Ihbarname, 0, 2));

    [Fact]
    public void YilKlasoruDonemYilidir()
        => Assert.Equal(2025, DosyaAdlari.YilKlasoru(Donem.Ayristir("01/2025-12/2025")));

    [Theory]
    [InlineData("Asıl, TÜM SİG.KOLLARI/YABNC UYR, 05510-Say.Kan.MYO, 09 / 2026, 01234567890123000", "01", "05510", "ASIL")]
    [InlineData("Ek, TÜM SİG.KOLLARI, 06111-Say.Kan., 09 / 2026, 1", "01", "06111", "EK1")]
    [InlineData("Asıl, SOSYAL GÜVENLİK DESTEK PRİMİ, Kanun Yok, 09 / 2026, 1", "02", "00000", "ASIL")]
    [InlineData("Asıl, SOS.GÜV.DES PRİM BİLDİRGE, Kanun türü yoktur, 09 / 2025, 01234567890123000, Yasal süresinde verilme", "02", "00000", "ASIL")]
    public void SgkAdParcalari(string aciklama, string bt, string kanun, string mahiyet)
        => Assert.Equal((bt, kanun, mahiyet), DosyaAdlari.SgkAdParcalari(aciklama));

    [Fact]
    public void SgkDosyaAdi()
        => Assert.Equal("09_EYLUL_01_05510_ASIL_TAHAKKUK_20261011164618.pdf",
            DosyaAdlari.SgkDosyasi(Donem.Ayristir("09/2026-09/2026"), "01", "05510", "ASIL", EvrakTuru.SgkTahakkuk, new DateTime(2026, 10, 11, 16, 46, 18)));

    [Fact]
    public void DuzeltmeSiralamasi()
    {
        var a = Satir("KDV1", "01/2026-01/2026", "17.02.2026 17:20:28") with { Oid = "a" };
        var b = Satir("KDV1", "01/2026-01/2026", "18.02.2026 12:59:29") with { Oid = "b" };
        var c = Satir("KDV1", "02/2026-02/2026", "12.03.2026 13:01:20") with { Oid = "c" };
        var s = DuzeltmeSiralayici.Sirala([b, c, a]);
        Assert.Equal(0, s["a"]); Assert.Equal(1, s["b"]); Assert.Equal(0, s["c"]);
    }
}

public class ArsivTestleri : IDisposable
{
    readonly string _k = Path.Combine(Path.GetTempPath(), "gib-test-" + Guid.NewGuid().ToString("N"));
    public ArsivTestleri() => Directory.CreateDirectory(_k);
    public void Dispose() => Directory.Delete(_k, true);
    void Dosya(string ad) => File.WriteAllText(Path.Combine(_k, ad), "x");

    const string Onek = "01_OCAK_KDV1_BEYANNAME";
    const string Yeni = "01_OCAK_KDV1_BEYANNAME_17.02.2026_17_20_28.pdf";

    [Fact] public void BosKlasordeYok() => Assert.False(ArsivDenetci.VarMi(_k, Onek, Yeni, 0));
    [Fact] public void TamAdVar() { Dosya(Yeni); Assert.True(ArsivDenetci.VarMi(_k, Onek, Yeni, 0)); }
    [Fact] public void EskiDamgaliDosyaSayilir() { Dosya("01_OCAK_KDV1_BEYANNAME_20260211164515.pdf"); Assert.True(ArsivDenetci.VarMi(_k, Onek, Yeni, 0)); }
    [Fact] public void DamgasizDosyaSayilir() { Dosya("01_OCAK_KDV1_BEYANNAME.pdf"); Assert.True(ArsivDenetci.VarMi(_k, Onek, Yeni, 0)); }

    [Fact]
    public void DuzeltmeIcinIkinciDosyaGerekir()
    {
        Dosya("01_OCAK_KDV1_BEYANNAME.pdf");
        Assert.False(ArsivDenetci.VarMi(_k, Onek, "x_DÜZ.pdf", 1));
        Dosya("01_OCAK_KDV1_BEYANNAME_DÜZ.pdf");
        Assert.True(ArsivDenetci.VarMi(_k, Onek, "x_DÜZ.pdf", 1));
    }

    [Fact]
    public void MahsupVeOdemeSayilmaz()
    {
        Dosya("01_OCAK_KDV1_TAHAKKUK_MAHSUP.pdf");
        Dosya("01_OCAK_KDV1_TAHAKKUK_ÖD.pdf");
        Assert.False(ArsivDenetci.VarMi(_k, "01_OCAK_KDV1_TAHAKKUK", "y.pdf", 0));
    }

    [Fact]
    public void IhbarnameOnekSiniri()
    {
        Dosya("05_MAYIS_KDV1_IHBARNAME2_18.10.2026_16_24_25.pdf");
        Assert.Equal(0, ArsivDenetci.EskiDosyaSayisi(_k, "05_MAYIS_KDV1_IHBARNAME"));
        Assert.Equal(1, ArsivDenetci.EskiDosyaSayisi(_k, "05_MAYIS_KDV1_IHBARNAME2"));
    }

    [Fact]
    public void KlasorYollari()
    {
        var m = new Musteri { KisaAd = "ÖR TEMİZLİ", VergiNo = "1" };
        var b = new BeyannameSatiri { Oid = "o", Tur = "KDV1", VknTckn = "1", Unvan = "u", Donem = Donem.Ayristir("12/2025-12/2025"), YuklemeZamani = DateTime.Now };
        Assert.Equal(Path.Combine(@"Y:\", "OR_TEM_ZL_", "2025", "KDV1"), ArsivDenetci.BeyannameKlasoru(@"Y:\", m, b));
        Assert.Equal(Path.Combine(@"X:\", "2025", "ÖR_TEMİZLİ", "MERKEZ"), ArsivDenetci.SgkKlasoru(@"X:\", m, b.Donem, "MERKEZ"));
    }
}
