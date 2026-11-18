using System.Text.Json;
using GibEvrakIndirici.Core.Ebyn;

namespace GibEvrakIndirici.Core.Arsiv;

/// <summary>
/// Arşivdeki PDF'lerin içinden okunan bilgilerin kalıcı önbelleği (§9.7). Her dosya bir kez okunur; boyutu ya da
/// son yazma zamanı değişirse yeniden okunur. Şimdilik beyanname PDF'lerinin onay zamanı tutulur.
/// Dosya: %APPDATA%\GibEvrakIndirici\arsiv-dizini.json (repoda değil; müşteri klasör adlarını içerir).
/// </summary>
public sealed class ArsivDizini
{
    public sealed record Kayit(long Boyut, DateTime YazmaUtc, DateTime? Onay);

    readonly Dictionary<string, Kayit> _kayitlar = new(StringComparer.OrdinalIgnoreCase);
    readonly object _kilit = new();
    bool _degisti;

    public int Sayi { get { lock (_kilit) return _kayitlar.Count; } }
    /// <summary>Bu oturumda diskten okunan (önbellekte olmayan) PDF sayısı.</summary>
    public int OkunanPdf { get; private set; }

    /// <summary>Beyanname PDF'inin onay zamanı; okunamazsa (resim PDF, bozuk) null.</summary>
    public DateTime? BeyannameOnay(string yol)
    {
        FileInfo fi;
        try { fi = new FileInfo(yol); if (!fi.Exists) return null; }
        catch (Exception) { return null; }
        lock (_kilit)
            if (_kayitlar.TryGetValue(yol, out var k) && k.Boyut == fi.Length && k.YazmaUtc == fi.LastWriteTimeUtc) return k.Onay;

        DateTime? onay;
        try { onay = BeyannamePdfOkuyucu.OnayZamani(File.ReadAllBytes(yol)); }
        catch (Exception) { return null; } // okunamadı (kilitli vb.) → önbelleğe yazma, sonra yeniden dene
        lock (_kilit)
        {
            _kayitlar[yol] = new Kayit(fi.Length, fi.LastWriteTimeUtc, onay);
            _degisti = true;
            OkunanPdf++;
        }
        return onay;
    }

    /// <summary>Dosya yeniden adlandırıldı: okunmuş bilgi yeni yola taşınır (içerik aynı, yeniden okumaya gerek yok).</summary>
    public void Tasindi(string eski, string yeni)
    {
        lock (_kilit)
        {
            if (!_kayitlar.Remove(eski, out var k)) return;
            try { var fi = new FileInfo(yeni); _kayitlar[yeni] = k with { Boyut = fi.Length, YazmaUtc = fi.LastWriteTimeUtc }; }
            catch (Exception) { }
            _degisti = true;
        }
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static ArsivDizini Ac(string dosya)
    {
        var d = new ArsivDizini();
        try
        {
            if (File.Exists(dosya))
                foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, Kayit>>(File.ReadAllText(dosya)) ?? [])
                    d._kayitlar[k] = v;
        }
        catch (JsonException) { } // bozuksa baştan kurulur
        return d;
    }

    public void Kaydet(string dosya)
    {
        Dictionary<string, Kayit> kopya;
        lock (_kilit)
        {
            if (!_degisti && File.Exists(dosya)) return;
            kopya = new(_kayitlar, StringComparer.OrdinalIgnoreCase);
            _degisti = false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(dosya)!);
        var gecici = dosya + ".tmp";
        File.WriteAllText(gecici, JsonSerializer.Serialize(kopya, Json));
        File.Move(gecici, dosya, overwrite: true); // uygulamanın kendi önbellek dosyası (arşiv değil)
    }
}
