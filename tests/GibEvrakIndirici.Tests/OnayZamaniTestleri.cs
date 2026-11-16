using GibEvrakIndirici.Core.Ebyn;
using Xunit.Abstractions;

namespace GibEvrakIndirici.Tests;

public class OnayZamaniTestleri(ITestOutputHelper cikti)
{
    [Theory]
    [InlineData("VERGİ DAİRESİ MÜDÜRLÜĞÜ Onay Zamanı : 27.10.2026 - 15:04:17 MATRAH")]
    [InlineData("lıkYılAy2026EylülVERGİ DAİRESİ MÜDÜRLÜĞÜOnay Zamanı :27.10.2026 - 15:04:17MATRAH")]
    [InlineData("Onay Zamaný : 27.10.2026 - 15:04:17")]
    [InlineData("Mükellefler İçin)Onay Zamanı: 27/10/2026 15:04:17MÜKELLEF BİLGİLERİ")]
    public void MetindenOkunur(string metin)
        => Assert.Equal(new DateTime(2026, 10, 27, 15, 4, 17), BeyannamePdfOkuyucu.OnayZamani(metin));

    [Fact]
    public void YoksaNull() => Assert.Null(BeyannamePdfOkuyucu.OnayZamani("Kabul Tarihi 27.10.2026"));

    /// <summary>Bu makinedeki test indirmesindeki tüm beyanname PDF'leri (yoksa geçer).</summary>
    [Fact]
    public void GercekBeyannamePdflerindenOkunur()
    {
        const string kok = @"D:\dev\y";
        if (!Directory.Exists(kok)) return;
        var dosyalar = Directory.GetFiles(kok, "*_BEYANNAME_*.pdf", SearchOption.AllDirectories);
        var okunamayan = dosyalar.Where(f => BeyannamePdfOkuyucu.OnayZamani(File.ReadAllBytes(f)) is null).ToList();
        cikti.WriteLine($"GERCEK: {dosyalar.Length} beyanname PDF, okunamayan {okunamayan.Count}");
        foreach (var f in okunamayan.Take(10)) cikti.WriteLine("  OKUNAMADI: " + Path.GetFileName(f));
        Assert.Empty(okunamayan);
    }

    sealed class SahteTasiyici(byte[] bynPdf) : IEbynTasiyici
    {
        public Task<string> PostAsync(string p, CancellationToken ct) => Task.FromResult("");
        public Task<byte[]> GetAsync(string u, CancellationToken ct) =>
            Task.FromResult(u.Contains("BEYANNAMEGORUNTULE") ? bynPdf : "%PDF-1.4 tahakkuk"u8.ToArray());
    }

    /// <summary>KDV1'de dosyalar beyanname PDF'indeki onay zamanıyla adlandırılır (gerçek PDF ile; yoksa geçer).</summary>
    [Fact]
    public async Task KdvDosyalariOnayZamaniylaAdlandirilir()
    {
        var kaynak = Directory.Exists(@"D:\dev\y")
            ? Directory.GetFiles(@"D:\dev\y", "09_EYLUL_KDV1_BEYANNAME_*.pdf", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (kaynak is null || BeyannamePdfOkuyucu.OnayZamani(File.ReadAllBytes(kaynak)) is not { } onay) return;
        var beklenenZaman = onay.ToString("dd.MM.yyyy_HH_mm_ss");

        var kok = Path.Combine(Path.GetTempPath(), "gib-onay-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ayar = new GibEvrakIndirici.Core.Ayarlar { YArsivKok = kok, XArsivKok = kok };
            var md = new GibEvrakIndirici.Core.Musteriler.MusteriDeposu();
            md.Yukle([new() { KisaAd = "ÖRNEKSEMET", VergiNo = "1234567891" }]);
            var ind = new GibEvrakIndirici.Core.Indirme.Indirici(
                new EbynIstemci(new SahteTasiyici(File.ReadAllBytes(kaynak))) { IstekAraligi = TimeSpan.Zero },
                md, new GibEvrakIndirici.Core.Sgk.IsyeriDeposu(), ayar, _ => Task.FromResult<string?>(null));
            var satir = new GibEvrakIndirici.Core.BeyannameSatiri
            {
                Oid = "o1", Tur = "KDV1", VknTckn = "1234567891", Unvan = "ÖRNEK SEMET", TahakkukOid = "t1",
                Donem = GibEvrakIndirici.Core.Donem.Ayristir("09/2026-09/2026"), YuklemeZamani = new DateTime(2026, 10, 27, 14, 20, 23),
            };
            var ozet = await ind.CalistirAsync([satir], new Progress<GibEvrakIndirici.Core.Indirme.IndirmeOlayi>(), default);
            var klasor = Path.Combine(kok, "ORNEKSEMET", "2026", "KDV1");
            var adlar = Directory.GetFiles(klasor).Select(Path.GetFileName).Order().ToList();
            cikti.WriteLine(string.Join(", ", adlar));
            // yükleme zamanı (14:20:23 uydurma) değil, PDF'teki onay zamanı kullanılır
            Assert.Equal([$"09_EYLUL_KDV1_BEYANNAME_{beklenenZaman}.pdf", $"09_EYLUL_KDV1_TAHAKKUK_{beklenenZaman}.pdf"], adlar);
            Assert.Equal(2, ozet.Indirilen);
        }
        finally { if (Directory.Exists(kok)) Directory.Delete(kok, true); }
    }

    /// <summary>Yeni kuralla üretilecek adların gerçek Y:\ arşiviyle örtüşmesini raporlar (yoksa geçer).</summary>
    [Fact]
    public void YeniKuralArsivleOrtusmesi()
    {
        if (!Directory.Exists(@"D:\dev\y") || !Directory.Exists(@"Y:\")) return;
        var ayar = new GibEvrakIndirici.Core.Ayarlar();
        var rx = new System.Text.RegularExpressions.Regex(@"_(\d{2}\.\d{2}\.\d{4}_\d{2}_\d{2}_\d{2})(_DÜZ\d*)?\.pdf$");
        var sayac = new Dictionary<string, int>();
        foreach (var f in Directory.GetFiles(@"D:\dev\y", "*_BEYANNAME_*.pdf", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(@"D:\dev\y", f);
            var hedefKlasor = Path.GetDirectoryName(Path.Combine(@"Y:\", rel))!;
            if (!Directory.Exists(hedefKlasor)) continue;
            var m = rx.Match(f);
            var ad = Path.GetFileName(f);
            var onek = ad[..ad.IndexOf("_" + m.Groups[1].Value, StringComparison.Ordinal)];
            var arsiv = Directory.GetFiles(hedefKlasor, onek + "_*.pdf").Select(Path.GetFileName).Where(x => rx.IsMatch(x!)).ToList();
            if (arsiv.Count == 0) continue;
            var tur = Path.GetFileName(hedefKlasor);
            var zaman = ayar.OnayZamaniKullanilir(tur) ? BeyannamePdfOkuyucu.OnayZamani(File.ReadAllBytes(f))?.ToString("dd.MM.yyyy_HH_mm_ss") ?? m.Groups[1].Value : m.Groups[1].Value;
            var anahtar = $"{tur}: " + (arsiv.Any(x => x!.Contains(zaman)) ? "birebir" : "farkli");
            sayac[anahtar] = sayac.GetValueOrDefault(anahtar) + 1;
        }
        foreach (var kv in sayac.OrderBy(k => k.Key)) cikti.WriteLine($"ORTUSME {kv.Key} = {kv.Value}");
    }
}
