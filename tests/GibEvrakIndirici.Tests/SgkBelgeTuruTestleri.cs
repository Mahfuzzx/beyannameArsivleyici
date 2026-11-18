using System.Text.RegularExpressions;
using GibEvrakIndirici.Core.Sgk;

namespace GibEvrakIndirici.Tests;

/// <summary>SGK belge türü tahakkuk PDF'inden: SGDP (destek primi) = 02, diğerleri 01 (§9.4).</summary>
public class SgkBelgeTuruTestleri(Xunit.Abstractions.ITestOutputHelper cikti)
{
    [Theory]
    [InlineData("TAHAKKUK FİŞİ (5510) ASIL ... 1 SOSYAL GÜVENLİK DESTEK PRİMİ 12.000,00", "02")]
    [InlineData("SOSYAL GÜVENLİKDESTEK PRİMİ", "02")]          // PDF'te kelimeler bitişik gelebiliyor
    [InlineData("SOSYAL GÜVENLİK DES TEK PR İMİ", "02")]       // ya da bölünmüş
    [InlineData("TAHAKKUK FİŞİ (5510) ASIL 1 KISA VADELİ SİGORTA KOLLARI PRİMİ", "01")]
    [InlineData("SOSYAL GÜVENLİK KURUMU BAŞKANLIĞI", "01")]   // kurum adı tek başına 02 yapmaz
    public void MetindenBelgeTuru(string metin, string beklenen) => Assert.Equal(beklenen, SgkPdfOkuyucu.BelgeTuru(metin));

    static readonly Regex AdRx = new(@"^\d\d_[A-Z]+_(\d\d)_\d{5}_");

    /// <summary>Gerçek X:\ arşivinde (varsa) tahakkuk PDF'inden okunan belge türü dosya adındakiyle aynı olmalı. Elle konan _MAHSUP vb. hariç.</summary>
    [Fact]
    public void GercekArsivTahakkuklariylaUyusur()
    {
        var kok = $@"X:\{DateTime.Today.Year}";
        if (!Directory.Exists(kok)) return;
        int dogru = 0; var yanlis = new List<string>();
        foreach (var f in Directory.EnumerateFiles(kok, "*_TAHAKKUK*.pdf", SearchOption.AllDirectories).Take(400))
        {
            var ad = Path.GetFileName(f);
            var m = AdRx.Match(ad);
            if (!m.Success || ad.Contains("MAHSUP", StringComparison.OrdinalIgnoreCase) || ad.Contains("ÖD", StringComparison.Ordinal)) continue;
            string bt;
            try { bt = SgkPdfOkuyucu.Oku(File.ReadAllBytes(f)).BelgeTuru; } catch { continue; }
            if (bt == m.Groups[1].Value) dogru++; else yanlis.Add(ad);
        }
        cikti.WriteLine($"GERCEK SGK belge türü: doğru {dogru}, yanlış {yanlis.Count}");
        foreach (var y in yanlis.Take(10)) cikti.WriteLine("  YANLIS: " + y);
        Assert.Empty(yanlis);
    }
}
