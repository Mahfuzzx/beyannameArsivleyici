using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Arsiv;

namespace GibEvrakIndirici.Tests;

/// <summary>Eski adlı arşiv dosyalarına PDF içeriği + GİB listesiyle ad önerisi (§9.8).</summary>
public class AdDuzeltmeTestleri : IDisposable
{
    readonly string _k = Path.Combine(Path.GetTempPath(), "gib-ad-" + Guid.NewGuid().ToString("N"));
    readonly Dictionary<string, EvrakIcerigi> _icerik = new(StringComparer.OrdinalIgnoreCase);
    const string Vkn = "1111111111";
    static readonly Musteri M = new() { KisaAd = "ORNEK", VergiNo = Vkn };
    Ayarlar Ayar => new() { YArsivKok = _k, XArsivKok = _k };

    public AdDuzeltmeTestleri() => Directory.CreateDirectory(_k);
    public void Dispose() => Directory.Delete(_k, true);

    string Klasor(int yil, string tur) { var k = Path.Combine(_k, "ORNEK", yil.ToString(), tur); Directory.CreateDirectory(k); return k; }

    void Beyanname(string klasor, string ad, DateTime onay, string vkn = Vkn)
    {
        File.WriteAllText(Path.Combine(klasor, ad), "x");
        _icerik[ad] = EvrakIcerigi.Ayristir($"KATMA DEĞER VERGİSİ BEYANNAMESİ Onay Zamanı : {onay:dd.MM.yyyy - HH:mm:ss} Vergi Kimlik Numarası {vkn}");
    }

    void Tahakkuk(string klasor, string ad, string donem, string kabul)
    {
        File.WriteAllText(Path.Combine(klasor, ad), "x");
        _icerik[ad] = EvrakIcerigi.Ayristir($"TAHAKKUK FİŞİ VERGİ KİMLİK NUMARASI {Vkn} Vergilendirme Dönemi Tarihi {donem} {kabul} {kabul} N TAHAKKUK");
    }

    EvrakIcerigi Oku(string yol) => _icerik.TryGetValue(Path.GetFileName(yol), out var i) ? i : EvrakIcerigi.Ayristir("");

    static BeyannameSatiri Satir(string tur, string donem, DateTime yukleme) => new()
    {
        Oid = Guid.NewGuid().ToString("N"), Tur = tur, VknTckn = Vkn, Unvan = "ÖRNEK", Donem = Donem.Ayristir(donem), YuklemeZamani = yukleme, TahakkukOid = "t"
    };

    List<AdOnerisi> Oner(IReadOnlyList<BeyannameSatiri> gib, string tur, int yil, bool onekli = false) =>
        AdDuzeltici.Oner(M, gib, Ayar, [tur], yil, yil, Oku, onekli);

    [Theory]
    [InlineData("04_NISAN_KDV1_BEYANNAME_26.05.2022_02_49_45", AdBicimi.Standart)]
    [InlineData("04_NISAN_KDV1_BEYANNAME_26.05.2022_02_49_45_DÜZ2", AdBicimi.Standart)]
    [InlineData("04_NISAN_ORNEK FRM_NISAN_KDV(AYLIK).xml_BYN", AdBicimi.Eski)]
    [InlineData("2025 02 B DÜZ", AdBicimi.Eski)]
    [InlineData("02_SUBAT_FORMBA_BEYANNAME_ORN_SUBAT_FORMBA_AYLIK_.XML_-", AdBicimi.Onekli)]
    [InlineData("10_EKIM_KDV1_BEYANNAME_20241128075953", AdBicimi.Onekli)]
    [InlineData("02 2024 T ÖD", AdBicimi.Haric)]
    [InlineData("03_MART_KDV2_TAHAKKUK_OD", AdBicimi.Haric)]
    [InlineData("2025 03 T od", AdBicimi.Haric)]
    [InlineData("10_EKIM_ORNEK_KODLU_BYN", AdBicimi.Eski)]
    [InlineData("04_ORNEK_NISAN_FormBA(AYLIK).xml_ALN", AdBicimi.Haric)]
    [InlineData("04_NISAN_KDV1_IHBARNAME_ORNEK", AdBicimi.Haric)]
    public void AdBicimiTanınır(string ad, AdBicimi beklenen) => Assert.Equal(beklenen, AdDuzeltici.Bicim(ad));

    [Fact]
    public void EskiProgramAdlariKdvdeOnayZamaniylaAdlanir()
    {
        var k = Klasor(2022, "KDV1");
        var onay = new DateTime(2022, 5, 26, 2, 49, 45);
        Beyanname(k, "04_NISAN_ORNEK FRM_NISAN_KDV(AYLIK).xml_BYN.pdf", onay);
        Tahakkuk(k, "04_NISAN_ORNEK FRM_NISAN_KDV(AYLIK).xml_THK.pdf", "04/2022-04/2022", "26/05/2022");
        var gib = new[] { Satir("KDV1", "04/2022-04/2022", new DateTime(2022, 5, 26, 2, 49, 0)) };

        var o = Oner(gib, "KDV1", 2022);

        Assert.All(o, x => Assert.Equal(OneriDurumu.Onerilen, x.Durum));
        Assert.Contains(o, x => x.YeniAd == "04_NISAN_KDV1_BEYANNAME_26.05.2022_02_49_45.pdf");
        Assert.Contains(o, x => x.YeniAd == "04_NISAN_KDV1_TAHAKKUK_26.05.2022_02_49_45.pdf");
    }

    [Fact]
    public void DuzeltmeSirasiGibdenDiğerTurlerdeYuklemeZamani()
    {
        var k = Klasor(2018, "MUHSGK");
        var y1 = new DateTime(2018, 5, 20, 10, 0, 0);
        var y2 = new DateTime(2018, 6, 2, 9, 30, 0);
        Beyanname(k, "04 2018 B.pdf", y1.AddMinutes(5));
        Beyanname(k, "04 2018 B DÜZ.pdf", y2.AddMinutes(3));
        Tahakkuk(k, "04 2018 T DÜZ.pdf", "04/2018-04/2018", "02/06/2018");
        var gib = new[] { Satir("MUHSGK", "04/2018-04/2018", y1), Satir("MUHSGK", "04/2018-04/2018", y2) };

        var o = Oner(gib, "MUHSGK", 2018).ToDictionary(x => x.EskiAd, x => x.YeniAd);

        Assert.Equal("04_NISAN_MUHSGK_BEYANNAME_20.05.2018_10_00_00.pdf", o["04 2018 B.pdf"]);
        Assert.Equal("04_NISAN_MUHSGK_BEYANNAME_02.06.2018_09_30_00_DÜZ.pdf", o["04 2018 B DÜZ.pdf"]);
        Assert.Equal("04_NISAN_MUHSGK_TAHAKKUK_02.06.2018_09_30_00_DÜZ.pdf", o["04 2018 T DÜZ.pdf"]); // eşinden
    }

    [Fact]
    public void EşsizTahakkukDönemVeKabulTarihindenBulunur()
    {
        var k = Klasor(2018, "MUHSGK");
        var y1 = new DateTime(2018, 5, 20, 10, 0, 0);
        var y2 = new DateTime(2018, 6, 2, 9, 30, 0);
        Tahakkuk(k, "eski tahakkuk.pdf", "04/2018-04/2018", "20/05/2018");
        var gib = new[] { Satir("MUHSGK", "04/2018-04/2018", y1), Satir("MUHSGK", "04/2018-04/2018", y2) };

        var o = Assert.Single(Oner(gib, "MUHSGK", 2018));
        Assert.Equal("04_NISAN_MUHSGK_TAHAKKUK_20.05.2018_10_00_00.pdf", o.YeniAd);
    }

    [Fact]
    public void HedefVarsaKopyaDenirVeDokunulmaz()
    {
        var k = Klasor(2022, "KDV1");
        var onay = new DateTime(2022, 5, 26, 2, 49, 45);
        Beyanname(k, "04_NISAN_KDV1_BEYANNAME_26.05.2022_02_49_45.pdf", onay);
        Beyanname(k, "eski.xml_BYN.pdf", onay);
        var gib = new[] { Satir("KDV1", "04/2022-04/2022", new DateTime(2022, 5, 26, 2, 49, 0)) };

        var o = Assert.Single(Oner(gib, "KDV1", 2022));
        Assert.Equal(OneriDurumu.Kopya, o.Durum);
    }

    [Fact]
    public void SurumBaskaStandartAdlaVarsaKopyaDenir()
    {
        // Eski program KDV'yi yükleme zamanıyla adlamış; elle konmuş kopyanın doğru adı (onay zamanı) farklı çıkar
        var k = Klasor(2026, "KDV1");
        var yuk = new DateTime(2026, 8, 27, 14, 2, 23);
        var onay = new DateTime(2026, 8, 27, 14, 2, 36);
        Beyanname(k, "07_TEMMUZ_KDV1_BEYANNAME_27.08.2026_14_02_23.pdf", onay);
        Tahakkuk(k, "07_TEMMUZ_KDV1_TAHAKKUK_27.08.2026_14_02_23.pdf", "07/2026-07/2026", "27/08/2026");
        Beyanname(k, "07_TEMMUZ_KDV1_BEYANNAME.pdf", onay);
        Tahakkuk(k, "07_TEMMUZ_KDV1_TAHAKKUK.pdf", "07/2026-07/2026", "27/08/2026");
        var gib = new[] { Satir("KDV1", "07/2026-07/2026", yuk) };

        var o = Oner(gib, "KDV1", 2026, onekli: true);
        Assert.Equal(2, o.Count);
        Assert.All(o, x => Assert.Equal(OneriDurumu.Kopya, x.Durum));
        Assert.Contains(o, x => x.Aciklama.Contains("07_TEMMUZ_KDV1_TAHAKKUK_27.08.2026_14_02_23.pdf"));
    }

    [Fact]
    public void DuzEkiEksikStandartAdlarDuzeltilir()
    {
        // Eski program düzeltmeyi _DÜZ'süz kaydetmiş: aynı dönemde iki "asıl"
        var k = Klasor(2025, "KDV1");
        var y1 = new DateTime(2025, 5, 28, 11, 43, 0);
        var y2 = new DateTime(2026, 1, 28, 17, 34, 0);
        var o1 = y1.AddSeconds(66);
        var o2 = y2.AddSeconds(35);
        Beyanname(k, "04_NISAN_KDV1_BEYANNAME_28.05.2025_11_44_06.pdf", o1);
        Tahakkuk(k, "04_NISAN_KDV1_TAHAKKUK_28.05.2025_11_44_06.pdf", "04/2025-04/2025", "28/05/2025");
        Beyanname(k, "04_NISAN_KDV1_BEYANNAME_28.01.2026_17_34_35.pdf", o2);
        Tahakkuk(k, "04_NISAN_KDV1_TAHAKKUK_28.01.2026_17_34_35.pdf", "04/2025-04/2025", "28/01/2026");
        File.WriteAllText(Path.Combine(k, "04_NISAN_KDV1_IHBARNAME2_28.01.2026_17_34_35.pdf"), "x");
        var gib = new[] { Satir("KDV1", "04/2025-04/2025", y1), Satir("KDV1", "04/2025-04/2025", y2) };

        var o = Oner(gib, "KDV1", 2025);

        Assert.Equal(3, o.Count);
        Assert.All(o, x => Assert.Equal(OneriDurumu.Onerilen, x.Durum));
        var d = o.ToDictionary(x => x.EskiAd, x => x.YeniAd);
        Assert.Equal("04_NISAN_KDV1_BEYANNAME_28.01.2026_17_34_35_DÜZ.pdf", d["04_NISAN_KDV1_BEYANNAME_28.01.2026_17_34_35.pdf"]);
        Assert.Equal("04_NISAN_KDV1_TAHAKKUK_28.01.2026_17_34_35_DÜZ.pdf", d["04_NISAN_KDV1_TAHAKKUK_28.01.2026_17_34_35.pdf"]);
        Assert.Equal("04_NISAN_KDV1_IHBARNAME2_28.01.2026_17_34_35_DÜZ.pdf", d["04_NISAN_KDV1_IHBARNAME2_28.01.2026_17_34_35.pdf"]);
    }

    [Fact]
    public void YanlisDuzEkiDuzelirDogrusuVarsaKopya()
    {
        var k = Klasor(2018, "MUHSGK");
        var y1 = new DateTime(2018, 5, 20, 10, 0, 0);
        var y2 = new DateTime(2018, 6, 2, 9, 30, 0);
        var y3 = new DateTime(2018, 6, 5, 9, 0, 0);
        // 2. düzeltme _DÜZ ile kaydedilmiş (olmalı: _DÜZ2); 1. düzeltme hem doğru hem eksiz adla var
        Beyanname(k, "04_NISAN_MUHSGK_BEYANNAME_05.06.2018_09_00_00_DÜZ.pdf", y3.AddMinutes(2));
        Beyanname(k, "04_NISAN_MUHSGK_BEYANNAME_02.06.2018_09_30_00_DÜZ.pdf", y2.AddMinutes(2));
        Beyanname(k, "04_NISAN_MUHSGK_BEYANNAME_02.06.2018_09_30_00.pdf", y2.AddMinutes(2));
        var gib = new[] { Satir("MUHSGK", "04/2018-04/2018", y1), Satir("MUHSGK", "04/2018-04/2018", y2), Satir("MUHSGK", "04/2018-04/2018", y3) };

        var o = Oner(gib, "MUHSGK", 2018).ToDictionary(x => x.EskiAd);

        Assert.Equal(2, o.Count);
        Assert.Equal(OneriDurumu.Onerilen, o["04_NISAN_MUHSGK_BEYANNAME_05.06.2018_09_00_00_DÜZ.pdf"].Durum);
        Assert.Equal("04_NISAN_MUHSGK_BEYANNAME_05.06.2018_09_00_00_DÜZ2.pdf", o["04_NISAN_MUHSGK_BEYANNAME_05.06.2018_09_00_00_DÜZ.pdf"].YeniAd);
        Assert.Equal(OneriDurumu.Kopya, o["04_NISAN_MUHSGK_BEYANNAME_02.06.2018_09_30_00.pdf"].Durum);
    }

    [Fact]
    public void BaskaMukellefVeUyumsuzOnayEslesmez()
    {
        var k = Klasor(2022, "KDV1");
        Beyanname(k, "a.xml_BYN.pdf", new DateTime(2022, 5, 26, 2, 49, 45), vkn: "2222222222");
        Beyanname(k, "b.xml_BYN.pdf", new DateTime(2022, 8, 1, 10, 0, 0));
        File.WriteAllText(Path.Combine(k, "c 2022 B.pdf"), "x"); // resim PDF
        var gib = new[] { Satir("KDV1", "04/2022-04/2022", new DateTime(2022, 5, 26, 2, 49, 0)) };

        var o = Oner(gib, "KDV1", 2022).ToDictionary(x => x.EskiAd, x => x.Durum);
        Assert.Equal(OneriDurumu.Eslesmedi, o["a.xml_BYN.pdf"]);
        Assert.Equal(OneriDurumu.Eslesmedi, o["b.xml_BYN.pdf"]);
        Assert.Equal(OneriDurumu.Okunamadi, o["c 2022 B.pdf"]);
    }

    [Fact]
    public void YanlisYilKlasorundekiYenidenAdlandirilmaz()
    {
        var k = Klasor(2023, "KDV1");
        Beyanname(k, "x.xml_BYN.pdf", new DateTime(2022, 5, 26, 2, 49, 45));
        var gib = new[] { Satir("KDV1", "04/2022-04/2022", new DateTime(2022, 5, 26, 2, 49, 0)) };

        Assert.Equal(OneriDurumu.YanlisKlasor, Assert.Single(Oner(gib, "KDV1", 2023)).Durum);
    }

    [Fact]
    public void OnekliAdlarYalnizIstenirseOnerilir()
    {
        var k = Klasor(2017, "FORMBA");
        var y = new DateTime(2017, 3, 29, 12, 9, 0);
        Beyanname(k, "02_SUBAT_FORMBA_BEYANNAME_ORN_SUBAT_FORMBA_AYLIK_.XML_-.pdf", y.AddSeconds(42));
        Tahakkuk(k, "02_SUBAT_FORMBA_TAHAKKUK_ORN_SUBAT_FORMBA_AYLIK_.XML_-.pdf", "02/2017-02/2017", "29/03/2017");
        var gib = new[] { Satir("FORMBA", "02/2017-02/2017", y) };

        Assert.Empty(Oner(gib, "FORMBA", 2017));
        var o = Oner(gib, "FORMBA", 2017, onekli: true);
        Assert.Contains(o, x => x.YeniAd == "02_SUBAT_FORMBA_BEYANNAME_29.03.2017_12_09_00.pdf");
        Assert.Contains(o, x => x.YeniAd == "02_SUBAT_FORMBA_TAHAKKUK_29.03.2017_12_09_00.pdf");
    }

    [Fact]
    public void UygulaVeGeriAl()
    {
        var k = Klasor(2022, "KDV1");
        var onay = new DateTime(2022, 5, 26, 2, 49, 45);
        Beyanname(k, "eski.xml_BYN.pdf", onay);
        var gib = new[] { Satir("KDV1", "04/2022-04/2022", new DateTime(2022, 5, 26, 2, 49, 0)) };
        var oneri = Oner(gib, "KDV1", 2022);
        var gunluk = Path.Combine(_k, "gunluk", "a.tsv");

        var s = AdDuzeltmeGunlugu.Uygula(oneri, gunluk);
        Assert.Equal(1, s.Basarili);
        Assert.True(File.Exists(Path.Combine(k, "04_NISAN_KDV1_BEYANNAME_26.05.2022_02_49_45.pdf")));
        Assert.False(File.Exists(Path.Combine(k, "eski.xml_BYN.pdf")));

        // İkinci uygulama: kaynak yok → dokunulmaz
        Assert.Equal(0, AdDuzeltmeGunlugu.Uygula(oneri, Path.Combine(_k, "gunluk", "b.tsv")).Basarili);

        var g = AdDuzeltmeGunlugu.GeriAl(gunluk);
        Assert.Equal(1, g.Basarili);
        Assert.True(File.Exists(Path.Combine(k, "eski.xml_BYN.pdf")));
        Assert.False(File.Exists(gunluk));
    }

    [Fact]
    public void UygulaHedefOlusmussaUzerineYazmaz()
    {
        var k = Klasor(2022, "KDV1");
        var onay = new DateTime(2022, 5, 26, 2, 49, 45);
        Beyanname(k, "eski.xml_BYN.pdf", onay);
        var gib = new[] { Satir("KDV1", "04/2022-04/2022", new DateTime(2022, 5, 26, 2, 49, 0)) };
        var oneri = Oner(gib, "KDV1", 2022);
        File.WriteAllText(Path.Combine(k, "04_NISAN_KDV1_BEYANNAME_26.05.2022_02_49_45.pdf"), "yeni");

        var s = AdDuzeltmeGunlugu.Uygula(oneri, Path.Combine(_k, "g.tsv"));
        Assert.Equal(0, s.Basarili);
        Assert.Equal("yeni", File.ReadAllText(Path.Combine(k, "04_NISAN_KDV1_BEYANNAME_26.05.2022_02_49_45.pdf")));
        Assert.True(File.Exists(Path.Combine(k, "eski.xml_BYN.pdf")));
    }

    [Fact]
    public void TahakkukIcerigiOkunur()
    {
        var i = EvrakIcerigi.Ayristir("Ba BİLDİRİM ALINDISI VERGİ KİMLİK NUMARASI 1111111111 : DÖNEM 02/2017-02/2017 : KABUL TARİHİ 29/03/2017 :");
        Assert.Equal(IcerikTuru.Tahakkuk, i.Tur);
        Assert.Equal(Donem.Ayristir("02/2017-02/2017"), i.Donem);
        Assert.Equal(new DateTime(2017, 3, 29), i.KabulTarihi);
        Assert.True(i.KimlikIcerir("1111111111"));
        Assert.False(i.KimlikIcerir("111111111"));
    }
}
