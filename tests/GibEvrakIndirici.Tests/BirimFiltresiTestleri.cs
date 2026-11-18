using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Ebyn;
using GibEvrakIndirici.Core.Indirme;
using GibEvrakIndirici.Core.Luca;
using GibEvrakIndirici.Core.Musteriler;
using GibEvrakIndirici.Core.Sgk;

namespace GibEvrakIndirici.Tests;

/// <summary>İki birim: diğer birimin müşterileri "0 " ile başlar; her birim kendi filtresi ve arşiviyle çalışır.</summary>
public class BirimFiltresiTestleri : IDisposable
{
    readonly string _k = Path.Combine(Path.GetTempPath(), "gib-birim-" + Guid.NewGuid().ToString("N"));
    public BirimFiltresiTestleri() => Directory.CreateDirectory(_k);
    public void Dispose() => Directory.Delete(_k, true);

    [Theory]
    [InlineData(MusteriFiltresi.Hepsi, "0 ÖRNEK", true)]
    [InlineData(MusteriFiltresi.Hepsi, "ÖRNEK", true)]
    [InlineData(MusteriFiltresi.BirimOnekiOlmayanlar, "0 ÖRNEK", false)]
    [InlineData(MusteriFiltresi.BirimOnekiOlmayanlar, "ÖRNEK", true)]
    [InlineData(MusteriFiltresi.BirimOnekiOlmayanlar, "0RNEK", true)]   // boşluksuz "0" önek sayılmaz
    [InlineData(MusteriFiltresi.SadeceBirimOnekliler, "0 ÖRNEK", true)]
    [InlineData(MusteriFiltresi.SadeceBirimOnekliler, "ÖRNEK", false)]
    public void FiltreKurali(MusteriFiltresi f, string kisaAd, bool beklenen)
        => Assert.Equal(beklenen, new Ayarlar { Filtre = f }.MusteriDahil(kisaAd));

    [Fact]
    public void KlasordeOnekAtilir()
    {
        var d = new MusteriDeposu();
        d.Yukle([new() { KisaAd = "0 ÖRNEK ŞTİ", VergiNo = "1111111111" }, new() { KisaAd = "ÇİÇEK", VergiNo = "2222222222" }]);
        Assert.Equal("0_ÖRNEK_ŞTİ", d.Bul("1111111111")!.XKlasor);
        Assert.Equal("0_ORNEK_ST_", d.Bul("1111111111")!.YKlasor);

        d.KlasorOnekiniAt("0 ");
        Assert.Equal("ÖRNEK_ŞTİ", d.Bul("1111111111")!.XKlasor);
        Assert.Equal("ORNEK_ST_", d.Bul("1111111111")!.YKlasor);
        Assert.Equal("ÇİÇEK", d.Bul("2222222222")!.XKlasor);        // öneksiz müşteri değişmez
        Assert.Equal("0 ÖRNEK ŞTİ", d.Bul("1111111111")!.KisaAd);   // kısa ad aynen kalır

        // Yeni Excel içe alınınca da geçerli; diske önek yazılmaz
        d.Birlestir([new() { KisaAd = "0 ÖRNEK ŞTİ", VergiNo = "1111111111" }]);
        Assert.Equal("ÖRNEK_ŞTİ", d.Bul("1111111111")!.XKlasor);
        var dosya = Path.Combine(_k, "m.json");
        d.Kaydet(dosya);
        Assert.DoesNotContain("AtilanOnek", File.ReadAllText(dosya));
        Assert.Equal("0_ÖRNEK_ŞTİ", MusteriDeposu.Ac(dosya).Bul("1111111111")!.XKlasor);
    }

    sealed class Tasiyici : IEbynTasiyici
    {
        public int Get;
        public Task<string> PostAsync(string p, CancellationToken ct) => Task.FromResult("");
        public Task<byte[]> GetAsync(string u, CancellationToken ct) { Get++; return Task.FromResult("%PDF-1.4 x"u8.ToArray()); }
    }

    static BeyannameSatiri Satir(string oid, string vkn) => new()
    {
        Oid = oid, Tur = "GGECICI", VknTckn = vkn, Unvan = "u", TahakkukOid = "t" + oid,
        Donem = Donem.Ayristir("07/2026-09/2026"), YuklemeZamani = new DateTime(2026, 11, 3, 13, 0, 0),
    };

    [Fact]
    public async Task DigerBiriminKurulumuYalnizOnunMusterileriniKendiArsivineIndirir()
    {
        var ayar = new Ayarlar { YArsivKok = _k, XArsivKok = _k, Filtre = MusteriFiltresi.SadeceBirimOnekliler, KlasordeBirimOnekiniAt = true };
        var md = new MusteriDeposu();
        md.Yukle([new() { KisaAd = "0 ÖRNEK", VergiNo = "1111111111" }, new() { KisaAd = "BİZİM", VergiNo = "2222222222" }]);
        md.KlasorOnekiniAt(ayar.AtilacakKlasorOneki);
        var t = new Tasiyici();
        var ind = new Indirici(new EbynIstemci(t) { IstekAraligi = TimeSpan.Zero }, md, new IsyeriDeposu(), ayar, _ => Task.FromResult<string?>(null));

        var ozet = await ind.CalistirAsync([Satir("a", "1111111111"), Satir("b", "2222222222")], new Progress<IndirmeOlayi>(), default);
        Assert.Equal(2, ozet.Indirilen);
        Assert.Equal(1, ozet.FiltreDisi);
        Assert.Equal(2, t.Get); // filtre dışı müşteri için GİB'e istek yok
        Assert.True(Directory.Exists(Path.Combine(_k, "ORNEK", "2026", "GGECICI")));
        Assert.False(Directory.Exists(Path.Combine(_k, "B_Z_M")));
    }

    [Fact]
    public void LucaIsyeriMusteriKlasoruIleEklenir()
    {
        var depo = new IsyeriDeposu();
        LucaIsyeriAktarici.DepoyaEkle(depo,
            [new Isyeri { FirmaKisaAd = "0 ÖRNEK", KisaAd = "MERKEZ", Sicil = "21234010101234560340110000" }],
            f => f == "0 ÖRNEK" ? "ÖRNEK" : f);
        Assert.Equal("ÖRNEK", depo.Bul("21234010101234560340110000")!.MusteriXKlasor);
    }
}
