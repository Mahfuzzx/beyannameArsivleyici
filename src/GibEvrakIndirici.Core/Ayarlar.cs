using System.Text.Json;

namespace GibEvrakIndirici.Core;

public sealed class Ayarlar
{
    /// <summary>Beyanname/tahakkuk/ihbarname arşivi (\\SUNUCU\BEYANNAMELER\LUCA).</summary>
    public string YArsivKok { get; set; } = @"Y:\";
    /// <summary>SGK tahakkuk/hizmet listesi arşivi (\\SUNUCU\BEYANNAMELER\SGK).</summary>
    public string XArsivKok { get; set; } = @"X:\";
    /// <summary>Açılışta taranacak yükleme dönemi (gün).</summary>
    public int AcilisGunSayisi { get; set; } = 31;
    /// <summary>Arşiv taramasında kaç yıl geriye gidilecek (içinde bulunulan yıl dahil).</summary>
    public int TaramaYilSayisi { get; set; } = 2;
    /// <summary>İki e-Beyanname isteği arası bekleme (GİB sınırı ≥ 1000 ms).</summary>
    public int IstekAraligiMs { get; set; } = 1500;
    /// <summary>
    /// Dosya adında yükleme zamanı yerine beyanname PDF'indeki "Onay Zamanı"nın kullanılacağı türler
    /// (eski programla uyum, §13.1). Virgülle ayrılır.
    /// </summary>
    public string OnayZamaniTurleri { get; set; } = "KDV1,KDV2,KDV4";

    public bool OnayZamaniKullanilir(string tur) =>
        OnayZamaniTurleri.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(tur.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// GİB boş yanıt verdiğinde (kısıtlama) sırayla beklenecek süreler, dakika. Hepsi tükenince indirme durur.
    /// </summary>
    public string KisitlamaBeklemeleriDk { get; set; } = "2,5,10";

    public IReadOnlyList<TimeSpan> KisitlamaBeklemeleri() =>
        KisitlamaBeklemeleriDk.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : -1)
            .Where(d => d > 0).Select(TimeSpan.FromMinutes).ToList();

    /// <summary>Luca üye no (c_musteri_id) — işyeri aktarımı için.</summary>
    public string LucaUyeNo { get; set; } = "";

    public static string VeriKlasoru =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GibEvrakIndirici");

    public static string AyarDosyasi => Path.Combine(VeriKlasoru, "ayarlar.json");
    public static string MusteriDosyasi => Path.Combine(VeriKlasoru, "musteriler.json");
    public static string IsyeriDosyasi => Path.Combine(VeriKlasoru, "isyerleri.json");
    public static string LogKlasoru => Path.Combine(VeriKlasoru, "log");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Ayarlar Ac() =>
        File.Exists(AyarDosyasi) ? JsonSerializer.Deserialize<Ayarlar>(File.ReadAllText(AyarDosyasi)) ?? new() : new();

    public void Kaydet()
    {
        Directory.CreateDirectory(VeriKlasoru);
        File.WriteAllText(AyarDosyasi, JsonSerializer.Serialize(this, Json));
    }
}
