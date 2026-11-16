using System.Text.Json;
using ClosedXML.Excel;

namespace GibEvrakIndirici.Core.Musteriler;

/// <summary>Müşteri listesi; VKN veya TCKN ile arama (§8.3).</summary>
public sealed class MusteriDeposu
{
    readonly List<Musteri> _liste = [];
    Dictionary<string, Musteri> _kimlik = new();

    public IReadOnlyList<Musteri> Liste => _liste;

    public void Yukle(IEnumerable<Musteri> musteriler)
    {
        _liste.Clear();
        _liste.AddRange(musteriler);
        _kimlik = new Dictionary<string, Musteri>();
        foreach (var m in _liste)
        {
            if (!string.IsNullOrWhiteSpace(m.VergiNo)) _kimlik.TryAdd(m.VergiNo.Trim(), m);
            if (!string.IsNullOrWhiteSpace(m.Tckn)) _kimlik.TryAdd(m.Tckn.Trim(), m);
        }
    }

    public Musteri? Bul(string vknTckn) => _kimlik.GetValueOrDefault(vknTckn.Trim());

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public void Kaydet(string dosya)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dosya)!);
        File.WriteAllText(dosya, JsonSerializer.Serialize(_liste, Json));
    }

    public static MusteriDeposu Ac(string dosya)
    {
        var d = new MusteriDeposu();
        if (File.Exists(dosya)) d.Yukle(JsonSerializer.Deserialize<List<Musteri>>(File.ReadAllText(dosya)) ?? []);
        return d;
    }

    /// <summary>
    /// Yeni Excel'i içe alırken elle verilmiş klasör adlarını (XKlasorOzel/YKlasorOzel) korur.
    /// </summary>
    public void Birlestir(IEnumerable<Musteri> yeni)
    {
        var eski = _liste.ToDictionary(m => m.KisaAd);
        Yukle(yeni.Select(m => eski.TryGetValue(m.KisaAd, out var e)
            ? m with { XKlasorOzel = e.XKlasorOzel, YKlasorOzel = e.YKlasorOzel }
            : m));
    }
}

/// <summary>
/// Luca "Müşteri Listesi" Excel'i: başlık satırı "Kısa Ad | Uzun Ad | Vergi Dairesi | Vergi No | T.C. Kimlik Numarası | ..."
/// </summary>
public static class MusteriExcelOkuyucu
{
    public static List<Musteri> Oku(string dosya)
    {
        using var wb = new XLWorkbook(dosya);
        var ws = wb.Worksheets.First();
        var son = ws.LastRowUsed()?.RowNumber() ?? 0;

        // Başlık satırını bul
        int baslik = 0;
        var kolon = new Dictionary<string, int>();
        for (int r = 1; r <= Math.Min(10, son) && baslik == 0; r++)
        {
            foreach (var c in ws.Row(r).CellsUsed())
            {
                var t = c.GetString().Trim();
                if (t.Equals("Kısa Ad", StringComparison.OrdinalIgnoreCase)) baslik = r;
                kolon[t] = c.Address.ColumnNumber;
            }
            if (baslik == 0) kolon.Clear();
        }
        if (baslik == 0) throw new InvalidDataException("Excel'de \"Kısa Ad\" başlığı bulunamadı.");

        int K(params string[] adlar) => adlar.Select(a => kolon.GetValueOrDefault(a)).FirstOrDefault(x => x > 0);
        int cKisa = K("Kısa Ad"), cUzun = K("Uzun Ad"), cVd = K("Vergi Dairesi"), cVkn = K("Vergi No"), cTc = K("T.C. Kimlik Numarası", "TC Kimlik No");

        string H(int r, int c) => c > 0 ? ws.Cell(r, c).GetFormattedString().Trim() : "";

        var liste = new List<Musteri>();
        for (int r = baslik + 1; r <= son; r++)
        {
            var kisa = H(r, cKisa);
            if (string.IsNullOrWhiteSpace(kisa)) continue;
            liste.Add(new Musteri
            {
                KisaAd = kisa,
                UzunAd = H(r, cUzun),
                VergiDairesi = H(r, cVd),
                VergiNo = H(r, cVkn),
                Tckn = H(r, cTc),
            });
        }
        return liste;
    }
}
