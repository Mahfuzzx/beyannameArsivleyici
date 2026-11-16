using GibEvrakIndirici.Core.Luca;
using GibEvrakIndirici.Core.Musteriler;
using GibEvrakIndirici.Core.Sgk;

namespace GibEvrakIndirici.Tests;

public class SgkLucaTestleri(Xunit.Abstractions.ITestOutputHelper cikti)
{
    // §11.1: işkolu kodu farklı olsa da eşleşmeli
    [Theory]
    [InlineData("21234010101234560340110000", "25678010101234560340110000")]
    [InlineData("21111080807654320340220000", "24321080807654320340220000")]
    [InlineData("29999010103333330340330000", "29999010103333330340330000")]
    public void SicilAnahtariIskoluKodunuYokSayar(string pdf, string luca)
        => Assert.Equal(SicilAnahtari.Anahtar(pdf), SicilAnahtari.Anahtar(luca));

    [Fact]
    public void FarkliIsyerleriFarkliAnahtar()
        => Assert.NotEqual(SicilAnahtari.Anahtar("21111080807654320340220000"), SicilAnahtari.Anahtar("29999010103333330340330000"));

    [Fact]
    public void DepoEslesmesi()
    {
        var d = new IsyeriDeposu();
        d.Ekle(new IsyeriDeposu.Kayit("ÖRNEK_ŞAHIS", "ANTEP", "24321080807654320340220000", "Luca"));
        Assert.Equal("ANTEP", d.Bul("211110808076543203402-20/000")?.IsyeriKlasor);
    }

    [Fact]
    public void LucaFirmalariKimlikBilgisiOkumadanAyristirilir()
    {
        var html = """
            <table><tr><td>Şirket - İşyeri</td><td>Kullanıcı Adı</td></tr>
            <tr><td>ÖR TEMİZLİ - MERKEZ<input type="hidden" name="SIRKET_ID" value="1000001"><input type="hidden" name="ISYERI_ID" value="2000001"></td>
                <td><input type="text" name="KULLANICI_ADI" value="gizli"></td><td><input type="password" name="SIFRE" value="gizli"></td></tr>
            <tr><td>ÖR TEMİZLİ - ŞUBE<input type="hidden" name="SIRKET_ID" value="1000001"><input type="hidden" name="ISYERI_ID" value="2000002"></td></tr>
            <tr><td>ÖRNEK GÜV2 - SUNGURLU<input type="hidden" name="SIRKET_ID" value="111"><input type="hidden" name="ISYERI_ID" value="5"></td></tr>
            </table>
            """;
        var f = LucaIsyeriAktarici.FirmalariAyristir(html);
        Assert.Equal([new LucaFirma("1000001", "ÖR TEMİZLİ"), new LucaFirma("111", "ÖRNEK GÜV2")], f);
    }

    [Fact]
    public void LucaIsyeriListesiAyristirilir()
    {
        var html = """
            <table><tr><td>Kısa Adı</td><td>Uzun Adı</td><td>SSK Sicil No</td><td>Açıklama</td><td>İşe Başlama Tarihi</td><td>İşin Bitiş Tarihi</td></tr>
            <tr><td>ANTEP</td><td>ÖRNEK KİŞİ</td><td>24321080807654320340220000</td><td></td><td>07/12/2020</td><td></td></tr>
            <tr><td>MALTEPE</td><td>ÖRNEK KİŞİ - DEKOR</td><td>29999010103333330340330000</td><td></td><td>16/02/2017</td><td>31/12/2020</td></tr>
            </table>
            """;
        var l = LucaIsyeriAktarici.IsyerleriniAyristir(html, "ÖRNEK ŞAHIS");
        Assert.Equal(2, l.Count);
        Assert.Equal("ANTEP", l[0].KisaAd);
        Assert.Null(l[0].Bitis);
        Assert.Equal(new DateTime(2017, 2, 16), l[1].Baslama);
        Assert.Equal(new DateTime(2020, 12, 31), l[1].Bitis);

        var d = new IsyeriDeposu();
        LucaIsyeriAktarici.DepoyaEkle(d, l);
        Assert.Equal(("ÖRNEK_ŞAHIS", "MALTEPE"), (d.Bul("29999010103333330340330000")!.MusteriXKlasor, d.Bul("29999010103333330340330000")!.IsyeriKlasor));
    }

    [Fact]
    public void MusteriVknVeyaTcknIleBulunur()
    {
        var d = new MusteriDeposu();
        d.Yukle([new() { KisaAd = "ÖRNEK GRUP", VergiNo = "0012345678" }, new() { KisaAd = "ÖRNEKHAKYI", VergiNo = "9876543210", Tckn = "12345678901" }]);
        Assert.Equal("ÖRNEK GRUP", d.Bul("0012345678")?.KisaAd);
        Assert.Equal("ÖRNEKHAKYI", d.Bul(" 12345678901 ")?.KisaAd);
        Assert.Null(d.Bul("1"));
    }

    // ---- Bu makinedeki gerçek dosyalarla (yoksa sessizce geçer) ----

    [Fact]
    public void GercekSgkPdfindenSicilOkunur()
    {
        var klasor = $@"X:\{DateTime.Today.Year}";
        if (!Directory.Exists(klasor)) return;
        var pdf = Directory.EnumerateFiles(klasor, "*_TAHAKKUK_*.pdf", SearchOption.AllDirectories).FirstOrDefault();
        if (pdf is null) return;
        var sicil = SgkPdfOkuyucu.SicilOku(pdf);
        cikti.WriteLine($"GERCEK PDF okundu: {(sicil is null ? "sicil yok" : "sicil var")}");
        Assert.Matches(@"^\d{26}$", sicil);
    }

    [Fact]
    public void GercekMusteriExceliOkunur()
    {
        var dosya = Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "musteri_listesi_filtre_raporu_*.xlsx").FirstOrDefault();
        if (dosya is null) return;
        var l = MusteriExcelOkuyucu.Oku(dosya);
        cikti.WriteLine($"GERCEK EXCEL: {l.Count} musteri");
        Assert.True(l.Count > 100);
        Assert.Contains(l, m => m.VergiNo.Length == 10 && m.VergiNo[0] == '0'); // baştaki sıfır korunur
    }
}
