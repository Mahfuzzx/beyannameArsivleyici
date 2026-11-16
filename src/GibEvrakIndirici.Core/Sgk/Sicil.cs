using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace GibEvrakIndirici.Core.Sgk;

/// <summary>
/// SGK işyeri sicil no (26 hane). İşkolu kodu (2.–5. haneler) SGK tarafında değişebildiği için
/// eşleştirmede kullanılmaz: anahtar = 1. hane + 6.–26. haneler (§11.1).
/// </summary>
public static class SicilAnahtari
{
    public static string Rakamlar(string s) => new(s.Where(char.IsAsciiDigit).ToArray());

    public static string? Anahtar(string sicil)
    {
        var r = Rakamlar(sicil);
        return r.Length == 26 ? r[0] + r[5..] : null;
    }
}

/// <summary>SGK tahakkuk / hizmet listesi PDF'inden işyeri sicil numarasını okur.</summary>
public static class SgkPdfOkuyucu
{
    // PDF'te tek parça: "211110808076543203402-20/000"
    static readonly Regex SicilRx = new(@"(\d{21})-(\d{2})/(\d{3})", RegexOptions.Compiled);

    public static string? SicilOku(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        var sb = new StringBuilder();
        foreach (var p in doc.GetPages())
        {
            sb.Append(p.Text).Append(' ');
            sb.Append(string.Join(" ", p.GetWords().Select(w => w.Text))).Append(' ');
        }
        var m = SicilRx.Match(sb.ToString());
        return m.Success ? m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value : null;
    }

    public static string? SicilOku(string dosya) => SicilOku(File.ReadAllBytes(dosya));
}

/// <summary>Sicil anahtarı → (müşteri X klasörü, işyeri klasörü) eşlemesi. Luca'dan ve X:\ PDF'lerinden beslenir.</summary>
public sealed class IsyeriDeposu
{
    public sealed record Kayit(string MusteriXKlasor, string IsyeriKlasor, string Sicil, string Kaynak, DateTime? Bitis = null);

    readonly Dictionary<string, Kayit> _kayitlar = new();
    public IReadOnlyCollection<Kayit> Kayitlar => _kayitlar.Values;

    public void Ekle(Kayit k, bool ustuneYaz = true)
    {
        var a = SicilAnahtari.Anahtar(k.Sicil);
        if (a is null) return;
        if (ustuneYaz || !_kayitlar.ContainsKey(a)) _kayitlar[a] = k;
    }

    public Kayit? Bul(string sicil) => SicilAnahtari.Anahtar(sicil) is { } a ? _kayitlar.GetValueOrDefault(a) : null;

    public IEnumerable<Kayit> MusteriIsyerleri(string musteriXKlasor) =>
        _kayitlar.Values.Where(k => k.MusteriXKlasor.Equals(musteriXKlasor, StringComparison.OrdinalIgnoreCase));

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public void Kaydet(string dosya)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dosya)!);
        File.WriteAllText(dosya, JsonSerializer.Serialize(_kayitlar.Values, Json));
    }
    public static IsyeriDeposu Ac(string dosya)
    {
        var d = new IsyeriDeposu();
        if (File.Exists(dosya))
            foreach (var k in JsonSerializer.Deserialize<List<Kayit>>(File.ReadAllText(dosya)) ?? []) d.Ekle(k);
        return d;
    }

    /// <summary>
    /// X:\{yıl}\{müşteri}\{işyeri}\ altındaki en yeni tahakkuk PDF'inden sicil okuyarak eşleme öğrenir.
    /// Luca'dan gelen kayıtların üzerine yazmaz.
    /// </summary>
    public int XArsivindenOgren(string xKok, IProgress<string>? ilerleme = null, CancellationToken ct = default)
    {
        int n = 0;
        if (!Directory.Exists(xKok)) return 0;
        foreach (var yil in Directory.EnumerateDirectories(xKok).OrderByDescending(x => x))
        foreach (var musteri in Directory.EnumerateDirectories(yil))
        foreach (var isyeri in Directory.EnumerateDirectories(musteri))
        {
            ct.ThrowIfCancellationRequested();
            var pdf = Directory.EnumerateFiles(isyeri, "*TAHAKKUK*.pdf").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
            if (pdf is null) continue;
            try
            {
                var sicil = SgkPdfOkuyucu.SicilOku(pdf);
                if (sicil is null || Bul(sicil) is not null) continue;
                Ekle(new Kayit(Path.GetFileName(musteri), Path.GetFileName(isyeri), sicil, "X arşivi"), ustuneYaz: false);
                n++;
                ilerleme?.Report($"Öğrenildi: {Path.GetFileName(musteri)} / {Path.GetFileName(isyeri)}");
            }
            catch (Exception ex) { ilerleme?.Report($"Okunamadı: {pdf} ({ex.Message})"); }
        }
        return n;
    }
}
