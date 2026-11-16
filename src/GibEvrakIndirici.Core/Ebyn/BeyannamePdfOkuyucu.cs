using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace GibEvrakIndirici.Core.Ebyn;

/// <summary>
/// e-Beyanname beyanname PDF'inin 1. sayfasındaki "Onay Zamanı : 27.10.2026 - 15:04:17" bilgisini okur (§13.1).
/// Tahakkuk fişinde bu bilgi yoktur.
/// </summary>
public static class BeyannamePdfOkuyucu
{
    // İki biçim görüldü:
    //   e-Beyanname: "Onay Zamanı :27.10.2026 - 15:04:17"
    //   e-Beyan (TUBITAK) : "Onay Zamanı: 18/10/2026 16:24:47"
    // Türkçe karakter fonta göre bozuk gelebilir ("Zamaný") → "Onay Zaman" + herhangi bir harf
    static readonly Regex OnayRx = new(@"Onay\s*Zaman\S*?\s*:\s*(\d{2})[./](\d{2})[./](\d{4})\s*-?\s*(\d{2}:\d{2}:\d{2})", RegexOptions.Compiled);

    public static DateTime? OnayZamani(string metin)
    {
        var m = OnayRx.Match(metin);
        return m.Success && DateTime.TryParseExact($"{m.Groups[1].Value}.{m.Groups[2].Value}.{m.Groups[3].Value} {m.Groups[4].Value}", "dd.MM.yyyy HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public static DateTime? OnayZamani(byte[] pdf)
    {
        try
        {
            using var doc = PdfDocument.Open(pdf);
            if (doc.NumberOfPages == 0) return null;
            var sayfa = doc.GetPage(1);
            var sb = new StringBuilder(sayfa.Text).Append(' ');
            sb.Append(string.Join(" ", sayfa.GetWords().Select(w => w.Text)));
            return OnayZamani(sb.ToString());
        }
        catch
        {
            return null;
        }
    }
}
