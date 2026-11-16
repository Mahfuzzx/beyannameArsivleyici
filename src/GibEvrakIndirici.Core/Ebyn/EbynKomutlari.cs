using System.Globalization;

namespace GibEvrakIndirici.Core.Ebyn;

/// <summary>
/// e-Beyanname dispatch komutları — YALNIZCA okuma/görüntüleme.
/// Onay, iptal, paket gönderme gibi geri alınamaz komutlar (§7) burada yoktur ve
/// <see cref="Dogrula"/> beyaz liste dışındaki her komutu reddeder.
/// </summary>
public static class EbynKomutlari
{
    static readonly HashSet<string> IzinliPost = new(StringComparer.Ordinal)
    {
        "BEYANNAMELISTESI", "IHBARNAMELERIGETIR", "THKESASBILGISGKMESAJLARI",
    };

    static readonly HashSet<string> IzinliImaj = new(StringComparer.Ordinal)
    {
        "BEYANNAMEGORUNTULE", "TAHAKKUKGORUNTULE", "IHBARNAMEGORUNTULE",
        "SGKTAHAKKUKGORUNTULE", "SGKHIZMETGORUNTULE", "PDF_SGK_TOPLU_TAHAKKUK_GORUNTULE",
    };

    /// <summary>Gönderilecek parametre dizesini beyaz listeye göre denetler; izinsizse istisna fırlatır.</summary>
    public static void Dogrula(string parametreler)
    {
        var p = Parametreler(parametreler);
        p.TryGetValue("cmd", out var cmd);
        if (cmd == "IMAJ")
        {
            if (!p.TryGetValue("subcmd", out var sub) || !IzinliImaj.Contains(sub))
                throw new InvalidOperationException($"İzin verilmeyen görüntüleme komutu: {sub}");
            return;
        }
        if (cmd is null || !IzinliPost.Contains(cmd))
            throw new InvalidOperationException($"İzin verilmeyen e-Beyanname komutu: {cmd}");
    }

    static Dictionary<string, string> Parametreler(string s) =>
        s.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
         .Select(x => x.Split('=', 2))
         .GroupBy(x => x[0])
         .ToDictionary(g => g.Key, g => g.First().Length > 1 ? g.First()[1] : "");

    static string T(DateTime d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Yükleme tarihi aralığına göre onaylı beyannameler (aralık ≤ 1 ay olmalı).</summary>
    public static string ListeYuklemeTarihi(DateTime bas, DateTime bit, int sayfa = 1)
        => $"cmd=BEYANNAMELISTESI&sorguTipiZ=1&baslangicTarihi={T(bas)}&bitisTarihi={T(bit)}&sorguTipiD=1&durum=2" + Sayfa(sayfa);

    /// <summary>
    /// Tek mükellef + tür + dönem: uzun yükleme aralığı kabul edilir (§9.1).
    /// vknTckn 10 haneyse VKN, 11 haneyse TCKN olarak gönderilir.
    /// </summary>
    public static string ListeMukellefTurDonem(string vknTckn, string tur, Donem donem, DateTime yukBas, DateTime yukBit, int sayfa = 1)
    {
        var kimlik = vknTckn.Trim().Length == 11 ? $"&sorguTipiT=1&tcKimlikNo={vknTckn.Trim()}" : $"&sorguTipiN=1&vergiNo={vknTckn.Trim()}";
        return "cmd=BEYANNAMELISTESI" + kimlik +
               $"&sorguTipiB=1&beyannameTanim={Uri.EscapeDataString(tur)}" +
               $"&sorguTipiP=1&donemBasYil={donem.BasYil}&donemBasAy={donem.BasAy}&donemBitYil={donem.BitYil}&donemBitAy={donem.BitAy}" +
               $"&sorguTipiZ=1&baslangicTarihi={T(yukBas)}&bitisTarihi={T(yukBit)}&sorguTipiD=1&durum=2" + Sayfa(sayfa);
    }

    static string Sayfa(int s) => s > 1 ? $"&pageNo={s}" : "";

    public static string Ihbarnameler(string beyannameOid) => $"cmd=IHBARNAMELERIGETIR&beyannameOid={beyannameOid}";
    public static string SgkBildirimleri(string beyannameOid) => $"cmd=THKESASBILGISGKMESAJLARI&beyannameOid={beyannameOid}";

    // ---- PDF (GET dispatch?...) — TOKEN taşıyıcı tarafından eklenir ----
    public static string BeyannamePdf(string bOid) => $"dispatch?cmd=IMAJ&subcmd=BEYANNAMEGORUNTULE&beyannameOid={bOid}";
    public static string TahakkukPdf(string bOid, string tOid) => $"dispatch?cmd=IMAJ&subcmd=TAHAKKUKGORUNTULE&beyannameOid={bOid}&tahakkukOid={tOid}";
    public static string IhbarnamePdf(IhbarnameBilgisi i) => $"dispatch?cmd=IMAJ&subcmd=IHBARNAMEGORUNTULE&beyannameOid={i.BeyannameOid}&tahakkukOid={i.TahakkukOid}&tarhOid={i.TarhOid}";
    public static string SgkTahakkukPdf(SgkBildirimi s) => $"dispatch?cmd=IMAJ&subcmd=SGKTAHAKKUKGORUNTULE&beyannameOid={s.BeyannameOid}&sgkTahakkukOid={s.SgkTahakkukOid}";
    public static string SgkHizmetPdf(SgkBildirimi s) => $"dispatch?cmd=IMAJ&subcmd=SGKHIZMETGORUNTULE&beyannameOid={s.BeyannameOid}&sgkTahakkukOid={s.SgkTahakkukOid}";

    /// <summary>[bas, bit] aralığını en fazla 1 aylık dilimlere böler (ay sonu sınırlı).</summary>
    public static IEnumerable<(DateTime Bas, DateTime Bit)> AylikDilimler(DateTime bas, DateTime bit)
    {
        var b = bas.Date;
        while (b <= bit.Date)
        {
            var ayinSonu = new DateTime(b.Year, b.Month, DateTime.DaysInMonth(b.Year, b.Month));
            var s = ayinSonu < bit.Date ? ayinSonu : bit.Date;
            yield return (b, s);
            b = s.AddDays(1);
        }
    }
}
