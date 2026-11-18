using System.Globalization;
using System.Text.RegularExpressions;
using GibEvrakIndirici.Core.Ebyn;
using UglyToad.PdfPig;

namespace GibEvrakIndirici.Core.Arsiv;

public enum IcerikTuru { Okunamadi, Beyanname, Tahakkuk, Diger }

/// <summary>
/// Arşivdeki bir beyanname/tahakkuk PDF'inin 1. sayfasından okunanlar (§9.8).
/// Beyanname: "Onay Zamanı : 26.05.2022 - 02:49:45" + VKN/TCKN.
/// Tahakkuk fişi / bildirim alındısı (Form BA/BS): VKN/TCKN + "Vergilendirme Dönemi 04/2022-04/2022" + kabul tarihi; zamanı yok.
/// Resim PDF'lerde (taranmış) metin yoktur → Okunamadi.
/// </summary>
public sealed record EvrakIcerigi(IcerikTuru Tur, DateTime? Onay, Donem? Donem, DateTime? KabulTarihi, string Metin)
{
    static readonly Regex DonemRx = new(@"(\d{2})/(\d{4})\s*-\s*(\d{2})/(\d{4})", RegexOptions.Compiled);
    static readonly Regex TarihRx = new(@"\b(\d{2}/\d{2}/\d{4})\b", RegexOptions.Compiled);

    public bool KimlikIcerir(string vknTckn) =>
        vknTckn.Length > 0 && Regex.IsMatch(Metin, $@"(?<!\d){Regex.Escape(vknTckn)}(?!\d)");

    public static EvrakIcerigi Oku(byte[] pdf)
    {
        string metin;
        try
        {
            using var doc = PdfDocument.Open(pdf);
            if (doc.NumberOfPages == 0) return new(IcerikTuru.Okunamadi, null, null, null, "");
            metin = string.Join(" ", doc.GetPage(1).GetWords().Select(w => w.Text));
        }
        catch (Exception) { return new(IcerikTuru.Okunamadi, null, null, null, ""); }
        return Ayristir(metin);
    }

    public static EvrakIcerigi Ayristir(string metin)
    {
        if (string.IsNullOrWhiteSpace(metin)) return new(IcerikTuru.Okunamadi, null, null, null, "");
        if (BeyannamePdfOkuyucu.OnayZamani(metin) is { } onay)
            return new(IcerikTuru.Beyanname, onay, null, null, metin);

        var buyuk = metin.ToUpper(new CultureInfo("tr-TR"));
        bool tahakkuk = buyuk.Contains("TAHAKKUK F") || buyuk.Contains("ALINDISI");
        if (!tahakkuk) return new(IcerikTuru.Diger, null, null, null, metin);

        Donem? donem = null;
        DateTime? kabul = null;
        var dm = DonemRx.Match(metin);
        if (dm.Success)
        {
            int ba = int.Parse(dm.Groups[1].Value), by = int.Parse(dm.Groups[2].Value), ta = int.Parse(dm.Groups[3].Value), ty = int.Parse(dm.Groups[4].Value);
            if (ba is >= 1 and <= 12 && ta is >= 1 and <= 12) donem = new Donem(by, ba, ty, ta);
            var tm = TarihRx.Match(metin, dm.Index + dm.Length);
            if (tm.Success && DateTime.TryParseExact(tm.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var k))
                kabul = k;
        }
        return new(IcerikTuru.Tahakkuk, null, donem, kabul, metin);
    }
}
