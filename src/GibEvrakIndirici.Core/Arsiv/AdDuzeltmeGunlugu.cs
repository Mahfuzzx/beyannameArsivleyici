using System.Text;

namespace GibEvrakIndirici.Core.Arsiv;

/// <summary>
/// Onaylanan ad düzeltmelerini uygular ve geri alınabilmesi için günlüğe yazar (§9.8).
/// Günlük: %APPDATA%\GibEvrakIndirici\ad-duzeltme\yyyy-MM-dd_HHmmss.tsv — satır başına "eski yol TAB yeni yol" (UTF-8).
/// Hiçbir dosyanın üzerine yazılmaz, hiçbir dosya silinmez: yalnız File.Move(overwrite: false).
/// </summary>
public static class AdDuzeltmeGunlugu
{
    public sealed record Sonuc(int Basarili, int Atlanan, IReadOnlyList<string> Hatalar, string? GunlukDosyasi);

    public static string YeniGunlukYolu(string klasor, DateTime zaman) =>
        Path.Combine(klasor, $"{zaman:yyyy-MM-dd_HHmmss}.tsv");

    /// <summary>Seçilen önerileri uygular. Her başarılı taşıma hemen günlüğe yazılır (yarıda kesilse de geri alınabilir).</summary>
    public static Sonuc Uygula(IEnumerable<AdOnerisi> secilen, string gunlukDosyasi, ArsivDizini? dizin = null)
    {
        int ok = 0, atla = 0;
        var hatalar = new List<string>();
        Directory.CreateDirectory(Path.GetDirectoryName(gunlukDosyasi)!);
        using var w = new StreamWriter(gunlukDosyasi, append: true, new UTF8Encoding(false));
        foreach (var o in secilen)
        {
            if (o.Durum != OneriDurumu.Onerilen || o.YeniYol is null) { atla++; continue; }
            var (eski, yeni) = (o.EskiYol, o.YeniYol);
            try
            {
                if (!File.Exists(eski)) { atla++; hatalar.Add($"{o.EskiAd}: dosya artık yok"); continue; }
                if (File.Exists(yeni)) { atla++; hatalar.Add($"{o.EskiAd}: {o.YeniAd} zaten var, dokunulmadı"); continue; }
                File.Move(eski, yeni, overwrite: false);
                w.WriteLine($"{eski}\t{yeni}");
                w.Flush();
                dizin?.Tasindi(eski, yeni);
                ok++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                hatalar.Add($"{o.EskiAd}: {ex.Message}");
            }
        }
        return new Sonuc(ok, atla, hatalar, ok > 0 ? gunlukDosyasi : null);
    }

    /// <summary>Bir günlükteki taşımaları sondan başa geri alır (yeni → eski). Eski adla bir dosya oluşmuşsa o satıra dokunmaz.</summary>
    public static Sonuc GeriAl(string gunlukDosyasi, ArsivDizini? dizin = null)
    {
        int ok = 0, atla = 0;
        var hatalar = new List<string>();
        var satirlar = File.ReadAllLines(gunlukDosyasi, Encoding.UTF8).Where(s => s.Contains('\t')).Reverse().ToList();
        foreach (var s in satirlar)
        {
            var p = s.Split('\t');
            var (eski, yeni) = (p[0], p[1]);
            try
            {
                if (!File.Exists(yeni)) { atla++; hatalar.Add($"{Path.GetFileName(yeni)}: dosya yok (taşınmış ya da zaten geri alınmış)"); continue; }
                if (File.Exists(eski)) { atla++; hatalar.Add($"{Path.GetFileName(eski)}: eski adla dosya var, dokunulmadı"); continue; }
                File.Move(yeni, eski, overwrite: false);
                dizin?.Tasindi(yeni, eski);
                ok++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                hatalar.Add($"{Path.GetFileName(yeni)}: {ex.Message}");
            }
        }
        if (ok > 0) File.Move(gunlukDosyasi, gunlukDosyasi + ".geri-alindi", overwrite: false);
        return new Sonuc(ok, atla, hatalar, gunlukDosyasi);
    }
}
