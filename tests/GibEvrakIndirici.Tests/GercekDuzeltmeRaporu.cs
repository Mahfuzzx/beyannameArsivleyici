using System.Text.RegularExpressions;
using GibEvrakIndirici.Core.Arsiv;

namespace GibEvrakIndirici.Tests;

/// <summary>
/// Bu makinedeki test indirmesi (D:\dev\y) ile gerçek Y:\ arşivi varsa: yeni düzeltme kuralıyla hangi beyannamelerin
/// _DÜZ sayılacağını raporlar (yalnız çıktı; arşiv yoksa geçer). Çıktıda müşteri adı yazılmaz.
/// </summary>
public class GercekDuzeltmeRaporu(Xunit.Abstractions.ITestOutputHelper cikti)
{
    static readonly Regex Rx = new(@"^(?<onek>.+_BEYANNAME)_(?<z>\d{2}\.\d{2}\.\d{4}_\d{2}_\d{2}_\d{2})(?<duz>_DÜZ\d*)?\.pdf$");

    [Fact]
    public void Rapor()
    {
        const string test = @"D:\dev\y", arsiv = @"Y:\";
        if (!Directory.Exists(test) || !Directory.Exists(arsiv)) return;
        int toplam = 0, yeniDuz = 0;
        foreach (var f in Directory.GetFiles(test, "*_BEYANNAME_*.pdf", SearchOption.AllDirectories))
        {
            var m = Rx.Match(Path.GetFileName(f));
            if (!m.Success || m.Groups["duz"].Success) continue;
            var klasor = Path.GetDirectoryName(Path.Combine(arsiv, Path.GetRelativePath(test, f)))!;
            if (!Directory.Exists(klasor)) continue;
            toplam++;
            var zaman = DateTime.ParseExact(m.Groups["z"].Value, "dd.MM.yyyy_HH_mm_ss", null);
            // Ad KDV'de onay zamanı taşır (yüklemeden en çok ~60 dk sonra) → 70 dk pay
            int n = ArsivDenetci.OncekiYuklemeSayisi(klasor, m.Groups["onek"].Value, zaman.AddMinutes(-70));
            if (n == 0) continue;
            yeniDuz++;
            var arsivAdlari = Directory.GetFiles(klasor, m.Groups["onek"].Value + "*.pdf").Select(Path.GetFileName);
            cikti.WriteLine($"DUZ{n}: {Path.GetFileName(f)}  arşiv: {string.Join(" | ", arsivAdlari)}");
        }
        cikti.WriteLine($"OZET: karşılığı olan {toplam} beyanname, yeni kuralla düzeltme sayılan {yeniDuz}");
    }
}
