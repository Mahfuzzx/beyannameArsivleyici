using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace GibEvrakIndirici.Core.Ebyn;

/// <summary>e-Beyanname dispatch POST yanıtı: &lt;SERVICERESULT&gt;&lt;TOKEN/&gt;&lt;SERVERERROR/&gt;&lt;HTMLCONTENT/&gt;</summary>
public sealed record EbynYanit(string? Token, string Hata, string Html, bool OturumDustu = false)
{
    public bool Basarili => string.IsNullOrWhiteSpace(Hata);

    static readonly Regex TokenRx = new(@"<TOKEN>([^<]*)</TOKEN>", RegexOptions.Compiled);
    static readonly Regex HataRx = new(@"<SERVERERROR>([\s\S]*?)</SERVERERROR>", RegexOptions.Compiled);
    // Oturum zaman aşımı: <SERVICERESULT><EYEKSERROR>Oturum zamanaşımına uğradı, yeni oturum açınız.</EYEKSERROR></SERVICERESULT>
    static readonly Regex EyeksRx = new(@"<EYEKSERROR>([\s\S]*?)</EYEKSERROR>", RegexOptions.Compiled);
    static readonly Regex HtmlRx = new(@"<HTMLCONTENT>([\s\S]*?)</HTMLCONTENT>", RegexOptions.Compiled);

    public static EbynYanit Ayristir(string metin)
    {
        var tok = TokenRx.Match(metin);
        var hata = HataRx.Match(metin);
        var eyeks = EyeksRx.Match(metin);
        var html = HtmlRx.Match(metin);
        var hataMetni = hata.Success ? WebUtility.HtmlDecode(hata.Groups[1].Value).Trim() : "";
        // Normal yanıtlarda da boş <EYEKSERROR></EYEKSERROR> bulunur; yalnızca içi doluysa oturum düşmüştür.
        var eyeksMetni = eyeks.Success ? WebUtility.HtmlDecode(eyeks.Groups[1].Value).Trim() : "";
        if (eyeksMetni.Length > 0 && hataMetni.Length == 0) hataMetni = eyeksMetni;
        return new EbynYanit(
            tok.Success && tok.Groups[1].Value.Length > 0 ? tok.Groups[1].Value : null,
            hataMetni,
            html.Success ? html.Groups[1].Value : metin,
            eyeksMetni.Length > 0);
    }
}

public sealed record BeyannameListesiSayfasi(IReadOnlyList<BeyannameSatiri> Satirlar, int Toplam, int SayfaSayisi);

/// <summary>e-Beyanname HTML parçalarını ayrıştırır (bkz. GIB_AKIS_NOTLARI.md §4–§5).</summary>
public static class EbynAyristirici
{
    public const int SayfaBoyu = 25;

    static readonly Regex SatirBasiRx = new(@"<tr id=""row(?<oid>[^""]+)""[^>]*>", RegexOptions.Compiled);
    static readonly Regex TdRx = new(@"<td(?<attr>[^>]*)>(?<ic>[\s\S]*?)</td>", RegexOptions.Compiled);
    static readonly Regex TitleRx = new(@"title=""(?<t>[^""]*)""", RegexOptions.Compiled);
    static readonly Regex EtiketRx = new(@"<[^>]+>", RegexOptions.Compiled);
    // Sayfalama metni "1 - 25 / 348" (boşluklu). Dönem "09/2026-09/2026" ile karışmasın diye
    // boşluklar zorunlu ve yalnızca ilk satırdan önceki kısımda aranır.
    static readonly Regex ToplamRx = new(@"(\d+) - (\d+) / (\d+)", RegexOptions.Compiled);
    static readonly Regex DurumRx = new(@"id=""durumTD[^""]*"">(?<d>[\s\S]*?)</td>", RegexOptions.Compiled);
    static readonly Regex TahakkukRx = new(@"tahakkukGoruntule\('(?<b>[^']+)'\s*,\s*'(?<t>[^']+)'", RegexOptions.Compiled);
    static readonly Regex IhbarnameRx = new(@"ihbarnameGoruntule\('(?<b>[^']+)'\s*,\s*'(?<t>[^']+)'\s*,\s*'(?<r>[^']+)'", RegexOptions.Compiled);
    static readonly Regex SgkPdfRx = new(@"sgkTahakkukGoruntule\('(?<b>[^']+)'\s*,\s*'(?<s>[^']+)'", RegexOptions.Compiled);

    public static string Metin(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(EtiketRx.Replace(html, " ")), @"\s+", " ").Trim();

    /// <summary>BEYANNAMELISTESI yanıtının HTMLCONTENT kısmını ayrıştırır.</summary>
    public static BeyannameListesiSayfasi BeyannameListesi(string html)
    {
        var satirlar = new List<BeyannameSatiri>();
        var basliklar = SatirBasiRx.Matches(html);
        for (int i = 0; i < basliklar.Count; i++)
        {
            var bas = basliklar[i];
            int son = i + 1 < basliklar.Count ? basliklar[i + 1].Index : html.Length;
            var parca = html[bas.Index..son];
            var satir = SatirAyristir(bas.Groups["oid"].Value, parca);
            if (satir != null) satirlar.Add(satir);
        }

        var tm = ToplamRx.Match(basliklar.Count > 0 ? html[..basliklar[0].Index] : html);
        int toplam = tm.Success ? int.Parse(tm.Groups[3].Value, CultureInfo.InvariantCulture) : satirlar.Count;
        int sayfa = Math.Max(1, (int)Math.Ceiling(toplam / (double)SayfaBoyu));
        return new BeyannameListesiSayfasi(satirlar, toplam, sayfa);
    }

    static BeyannameSatiri? SatirAyristir(string oid, string parca)
    {
        // Durum sütunundaki iç tablodan önceki düz hücreler: [checkbox, tür, vkn, unvan, vd, dönem, şube, yükleme]
        int icTablo = parca.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
        var duz = icTablo > 0 ? parca[..icTablo] : parca;
        var hucreler = TdRx.Matches(duz).Select(m => (attr: m.Groups["attr"].Value, ic: m.Groups["ic"].Value)).ToList();
        if (hucreler.Count < 8) return null;

        string H(int i) => Metin(hucreler[i].ic);
        var unvanTitle = TitleRx.Match(hucreler[3].attr);
        var unvan = unvanTitle.Success ? WebUtility.HtmlDecode(unvanTitle.Groups["t"].Value) : H(3);

        if (!DateTime.TryParseExact(H(7), "dd.MM.yyyy - HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var yukleme))
            return null;

        var thk = TahakkukRx.Match(parca);
        var durum = DurumRx.Match(parca);
        return new BeyannameSatiri
        {
            Oid = oid,
            Tur = H(1),
            VknTckn = H(2),
            Unvan = unvan.Trim(),
            VergiDairesi = H(4),
            Donem = Donem.Ayristir(H(5)),
            Sube = H(6),
            YuklemeZamani = yukleme,
            Durum = durum.Success ? Metin(durum.Groups["d"].Value) : "",
            TahakkukOid = thk.Success ? thk.Groups["t"].Value : null,
            IhbarnameVar = parca.Contains("ihbarnameleriGetirInDetay(", StringComparison.Ordinal),
            SgkBildirimVar = parca.Contains("beyannameSGKBildirimleriGoster(", StringComparison.Ordinal),
        };
    }

    /// <summary>IHBARNAMELERIGETIR yanıtı.</summary>
    public static IReadOnlyList<IhbarnameBilgisi> Ihbarnameler(string html)
    {
        var liste = new List<IhbarnameBilgisi>();
        foreach (Match m in IhbarnameRx.Matches(html))
        {
            // Açıklama: aynı <tr> içindeki metin (ör. "V.U.K 344 Vergi Ziyaı")
            int trBas = html.LastIndexOf("<tr", m.Index, StringComparison.OrdinalIgnoreCase);
            int trSon = html.IndexOf("</tr>", m.Index, StringComparison.OrdinalIgnoreCase);
            var aciklama = trBas >= 0 && trSon > trBas ? Metin(html[trBas..trSon]) : "";
            liste.Add(new IhbarnameBilgisi(m.Groups["b"].Value, m.Groups["t"].Value, m.Groups["r"].Value, aciklama));
        }
        return liste;
    }

    /// <summary>THKESASBILGISGKMESAJLARI yanıtı: SGK bildirim satırları.</summary>
    public static IReadOnlyList<SgkBildirimi> SgkBildirimleri(string html, string beyannameOid)
    {
        var pdfli = SgkPdfRx.Matches(html).Select(m => m.Groups["s"].Value).ToHashSet();
        var liste = new List<SgkBildirimi>();
        var basliklar = SatirBasiRx.Matches(html);
        for (int i = 0; i < basliklar.Count; i++)
        {
            var bas = basliklar[i];
            if (!bas.Value.Contains("SGK Bildirimleri", StringComparison.Ordinal)) continue;
            int son = i + 1 < basliklar.Count ? basliklar[i + 1].Index : html.Length;
            var hucreler = TdRx.Matches(html[bas.Index..son]).Select(m => Metin(m.Groups["ic"].Value)).ToList();
            var sgkOid = bas.Groups["oid"].Value;
            liste.Add(new SgkBildirimi
            {
                BeyannameOid = beyannameOid,
                SgkTahakkukOid = sgkOid,
                Donem = hucreler.ElementAtOrDefault(0) ?? "",
                Aciklama = hucreler.ElementAtOrDefault(1) ?? "",
                PdfVar = pdfli.Contains(sgkOid),
            });
        }
        return liste;
    }
}
