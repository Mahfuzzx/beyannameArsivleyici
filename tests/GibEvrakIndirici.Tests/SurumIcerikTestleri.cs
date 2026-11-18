using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Arsiv;
using GibEvrakIndirici.Core.Ebyn;
using GibEvrakIndirici.Core.Indirme;
using GibEvrakIndirici.Core.Musteriler;
using GibEvrakIndirici.Core.Sgk;

namespace GibEvrakIndirici.Tests;

/// <summary>
/// Düzeltme sürümleri arşivde PDF içeriğiyle (onay zamanı) tanınır (§9.6). 2026-11-18 olayının yeniden üretimi:
/// GİB'de 3 sürüm (v1, v2, v3); ofis arşivinde elle "X_BEYANNAME.pdf" (= v1) ve "X_BEYANNAME_DÜZ.pdf" (= v3) var.
/// Eski önek sayımı v3'ü de kopya olarak indiriyordu; doğru sonuç yalnız v2'nin inmesi.
/// Bu makinedeki test indirmesinden 3 farklı onay zamanlı gerçek beyanname PDF'i kullanılır (yoksa test geçer).
/// </summary>
public class SurumIcerikTestleri : IDisposable
{
    readonly string _k = Path.Combine(Path.GetTempPath(), "gib-surum-" + Guid.NewGuid().ToString("N"));
    public SurumIcerikTestleri() => Directory.CreateDirectory(_k);
    public void Dispose() => Directory.Delete(_k, true);

    static List<(byte[] Pdf, DateTime Onay)>? GercekPdfler(int n)
    {
        if (!Directory.Exists(@"D:\dev\y")) return null;
        var l = new List<(byte[], DateTime)>();
        foreach (var f in Directory.EnumerateFiles(@"D:\dev\y", "*_KDV1_BEYANNAME_*.pdf", SearchOption.AllDirectories))
        {
            var b = File.ReadAllBytes(f);
            if (BeyannamePdfOkuyucu.OnayZamani(b) is { } oz && l.All(x => x.Item2 != oz)) l.Add((b, oz));
            if (l.Count == n) return l;
        }
        return null;
    }

    sealed class Tasiyici(Dictionary<string, byte[]> beyannameler) : IEbynTasiyici
    {
        public List<string> Getler { get; } = [];
        public Task<string> PostAsync(string p, CancellationToken ct) => Task.FromResult("");
        public Task<byte[]> GetAsync(string u, CancellationToken ct)
        {
            Getler.Add(u);
            if (u.Contains("BEYANNAMEGORUNTULE"))
                return Task.FromResult(beyannameler[u[(u.IndexOf("beyannameOid=", StringComparison.Ordinal) + 13)..]]);
            return Task.FromResult("%PDF-1.4 tahakkuk"u8.ToArray());
        }
    }

    [Fact]
    public async Task ElleAdlanmisSurumlerIcerikleTaninir()
    {
        var pdf = GercekPdfler(3);
        if (pdf is null) return;
        var klasor = Path.Combine(_k, "ORNEK", "2024", "KDV1");
        Directory.CreateDirectory(klasor);
        // ofis arşivi: v1 damgasız, v3 elle "_DÜZ"; tahakkuklar da aynı adlarla
        File.WriteAllBytes(Path.Combine(klasor, "08_AGUSTOS_KDV1_BEYANNAME.pdf"), pdf[0].Pdf);
        File.WriteAllBytes(Path.Combine(klasor, "08_AGUSTOS_KDV1_BEYANNAME_DÜZ.pdf"), pdf[2].Pdf);
        File.WriteAllText(Path.Combine(klasor, "08_AGUSTOS_KDV1_TAHAKKUK.pdf"), "eski");
        File.WriteAllText(Path.Combine(klasor, "08_AGUSTOS_KDV1_TAHAKKUK_DÜZ.pdf"), "eski");

        var ayar = new Ayarlar { YArsivKok = _k, XArsivKok = _k };
        var md = new MusteriDeposu();
        md.Yukle([new() { KisaAd = "ORNEK", VergiNo = "1111111111" }]);
        var t = new Tasiyici(new() { ["v1"] = pdf[0].Pdf, ["v2"] = pdf[1].Pdf, ["v3"] = pdf[2].Pdf });
        var depo = new TamamlananDeposu();
        var ind = new Indirici(new EbynIstemci(t) { IstekAraligi = TimeSpan.Zero }, md, new IsyeriDeposu(), ayar,
            _ => Task.FromResult<string?>(null), tamamlanan: depo);
        var satirlar = new[] { "v1", "v2", "v3" }.Select((oid, i) => new BeyannameSatiri
        {
            Oid = oid, Tur = "KDV1", VknTckn = "1111111111", Unvan = "ORNEK", TahakkukOid = "t" + oid,
            Donem = Donem.Ayristir("08/2024-08/2024"), YuklemeZamani = new DateTime(2024, 9, 26 + i, 10, 0, 0),
        }).ToList();

        var ozet = await ind.CalistirAsync(satirlar, new Progress<IndirmeOlayi>(), default);

        string[] eski = ["08_AGUSTOS_KDV1_BEYANNAME.pdf", "08_AGUSTOS_KDV1_BEYANNAME_DÜZ.pdf", "08_AGUSTOS_KDV1_TAHAKKUK.pdf", "08_AGUSTOS_KDV1_TAHAKKUK_DÜZ.pdf"];
        var yeni = Directory.GetFiles(klasor).Select(Path.GetFileName).Where(f => !eski.Contains(f)).Order().ToList();
        var v2 = pdf[1].Onay.ToString("dd.MM.yyyy_HH_mm_ss");
        Assert.Equal([$"08_AGUSTOS_KDV1_BEYANNAME_{v2}_DÜZ.pdf", $"08_AGUSTOS_KDV1_TAHAKKUK_{v2}_DÜZ.pdf"], yeni);
        Assert.Equal(2, ozet.Indirilen);
        Assert.Equal(6, Directory.GetFiles(klasor).Length);

        // İkinci çalıştırma: hepsi önbellekten tamam → GİB'e hiç istek yok
        int once = t.Getler.Count;
        var ikinci = await ind.CalistirAsync(satirlar, new Progress<IndirmeOlayi>(), default);
        Assert.Equal(0, ikinci.Indirilen);
        Assert.Equal(once, t.Getler.Count);
    }

    [Fact]
    public async Task EslesenSurumunEksikTahakkukuAyniAdlaIner()
    {
        var pdf = GercekPdfler(1);
        if (pdf is null) return;
        var klasor = Path.Combine(_k, "ORNEK", "2023", "KDV1");
        Directory.CreateDirectory(klasor);
        File.WriteAllBytes(Path.Combine(klasor, "03_MART_KDV1_BEYANNAME_TEMIZLI_MART_KDV_AYLIK_.XML_-.pdf"), pdf[0].Pdf); // tahakkuku yok

        var md = new MusteriDeposu();
        md.Yukle([new() { KisaAd = "ORNEK", VergiNo = "1111111111" }]);
        var ind = new Indirici(new EbynIstemci(new Tasiyici(new() { ["v1"] = pdf[0].Pdf })) { IstekAraligi = TimeSpan.Zero }, md, new IsyeriDeposu(),
            new Ayarlar { YArsivKok = _k, XArsivKok = _k }, _ => Task.FromResult<string?>(null));
        var satir = new BeyannameSatiri
        {
            Oid = "v1", Tur = "KDV1", VknTckn = "1111111111", Unvan = "ORNEK", TahakkukOid = "t",
            Donem = Donem.Ayristir("03/2023-03/2023"), YuklemeZamani = new DateTime(2023, 4, 26, 10, 0, 0),
        };
        var ozet = await ind.CalistirAsync([satir], new Progress<IndirmeOlayi>(), default);
        Assert.Equal(1, ozet.Indirilen); // yalnız tahakkuk
        Assert.True(File.Exists(Path.Combine(klasor, "03_MART_KDV1_TAHAKKUK_TEMIZLI_MART_KDV_AYLIK_.XML_-.pdf")));
        Assert.Equal(2, Directory.GetFiles(klasor).Length);
    }
}
