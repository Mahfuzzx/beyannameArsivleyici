using System.Text.Json;

namespace GibEvrakIndirici.Core.Arsiv;

/// <summary>
/// İhbarname ve SGK evrakları eksiksiz indirilmiş beyannameler (OID). Bunlar için GİB'e alt liste sorgusu
/// (IHBARNAMELERIGETIR / THKESASBILGISGKMESAJLARI) tekrar atılmaz; GİB bu sorgularda da istekler arası ≥1 sn ister (§12.3),
/// atlanan satırlar boşuna beklemesin. Anahtar arşiv köklerini içerir: test klasöründe tamamlanan, gerçek arşivde tamam sayılmaz.
/// </summary>
public sealed class TamamlananDeposu
{
    readonly HashSet<string> _anahtarlar = new(StringComparer.Ordinal);
    readonly object _kilit = new();

    public int Sayi { get { lock (_kilit) return _anahtarlar.Count; } }

    public static string Anahtar(Ayarlar a, string beyannameOid, string tur) =>
        $"{a.YArsivKok.TrimEnd('\\', '/').ToUpperInvariant()}|{a.XArsivKok.TrimEnd('\\', '/').ToUpperInvariant()}|{tur}|{beyannameOid}";

    public bool Var(string anahtar) { lock (_kilit) return _anahtarlar.Contains(anahtar); }
    public void Ekle(string anahtar) { lock (_kilit) _anahtarlar.Add(anahtar); }

    public static TamamlananDeposu Ac(string dosya)
    {
        var d = new TamamlananDeposu();
        if (File.Exists(dosya))
            foreach (var a in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(dosya)) ?? []) d._anahtarlar.Add(a);
        return d;
    }

    public void Kaydet(string dosya)
    {
        List<string> liste;
        lock (_kilit) liste = _anahtarlar.Order(StringComparer.Ordinal).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(dosya)!);
        var gecici = dosya + ".tmp";
        File.WriteAllText(gecici, JsonSerializer.Serialize(liste));
        File.Move(gecici, dosya, overwrite: true); // uygulamanın kendi durum dosyası (arşiv değil)
    }
}
