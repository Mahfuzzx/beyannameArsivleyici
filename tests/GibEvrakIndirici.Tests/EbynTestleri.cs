using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Ebyn;

namespace GibEvrakIndirici.Tests;

/// <summary>e-Beyanname yanıt yapısı canlı oturumdan alındı; isim/numaralar uydurmadır.</summary>
public class EbynTestleri
{
    const string Satir1 = """
        <script>beyannameTipleri['9Ga%7Bzl%C4%B1sN%7B%5DfAZ'] = 'KDV1';</script>
        <tr id="row9Ga%7Bzl%C4%B1sN%7B%5DfAZ" class="blAG" title="TUBITAK   18.10.2026 - 16:24:25">
         <td id="checkboxTD9Ga%7Bzl%C4%B1sN%7B%5DfAZ" align=center></td>
        <td>KDV1</td>
        <td align=left>
         1111111111</td>
        <td title="ÖRNEK OTOMOTİV İNŞAAT TURİZM LİMİTED ŞİRKETİ">ÖRNEK OTOMOTİV...</td>
        <td align=left>ULUÇINAR VD</td>
        <td align=center>09/2026-09/2026</td>
        	<td align=center>Merkez</td>
        	<td align=left>18.10.2026 - 16:24:25</td>
        <td>
        	<table><tr>
        		<td id="durumTD9Ga%7Bzl%C4%B1sN%7B%5DfAZ"><img src="images/ok.gif"/>&nbsp;Onaylandı</td>
        		<td id="bynPDF9Ga"><img onclick="beyannameGoruntule('9Ga%7Bzl%C4%B1sN%7B%5DfAZ',false,false)"/></td>
        		<td id="thkPDF9Ga"><img onclick="tahakkukGoruntule('9Ga%7Bzl%C4%B1sN%7B%5DfAZ','6bmvcgozk81myi',false,false)"/></td>
        		<td id="ihb9Ga"></td>
        	</tr></table>
        </td>
        <td align="center" style="width: 20px"></td>
        </tr>
        """;

    const string Satir2 = """
        <tr id="row11mvyz06ox1fro" class="blKG" title="2026_OCAK_KDV(AYLIK).xml 17.02.2026 - 17:20:28">
         <td></td><td>MUHSGK</td><td align=left>22222222222</td><td>AYŞE ÖRNEK</td><td>KÜÇÜKYALI VD</td>
         <td align=center>07/2026-09/2026</td><td align=center>Merkez</td><td align=left>17.02.2026 - 17:20:28</td>
         <td><table><tr><td id="durumTD11m"><img/>&nbsp;Onaylandı</td>
           <td><img onclick="tahakkukGoruntule('11mvyz06ox1fro','0zmvw9p1z11g7u',false,false)"/></td>
           <td><img onclick="ihbarnameleriGetirInDetay('11mvyz06ox1fro',false)"/></td></tr></table></td>
         <td><img onclick="beyannameSGKBildirimleriGoster('11mvyz06ox1fro', false)"/></td>
        </tr>
        """;

    static string Liste(string toplam) => $"<form><table><tr><td><b><font size=\"2\">{toplam}</font></b></td></tr></table><table>{Satir1}{Satir2}</table></form>";

    [Fact]
    public void YanitAyristirilir()
    {
        var y = EbynYanit.Ayristir("\n\n<SERVICERESULT>\n\t<TOKEN>abc123</TOKEN>\n\t<SERVERERROR>Yükleme Tarih Aralığı 1 aydan fazla girilmemelidir.</SERVERERROR>\n\t<EYEKSERROR></EYEKSERROR>\t\n\t<HTMLCONTENT><b>x</b></HTMLCONTENT>\n</SERVICERESULT>");
        Assert.Equal("abc123", y.Token);
        Assert.False(y.Basarili);
        Assert.Contains("1 aydan", y.Hata);
        Assert.Equal("<b>x</b>", y.Html);
    }

    [Fact]
    public void BeyannameListesiAyristirilir()
    {
        var l = EbynAyristirici.BeyannameListesi(Liste("1 - 25 / 348"));
        Assert.Equal(348, l.Toplam);
        Assert.Equal(14, l.SayfaSayisi);
        Assert.Equal(2, l.Satirlar.Count);

        var a = l.Satirlar[0];
        Assert.Equal("9Ga%7Bzl%C4%B1sN%7B%5DfAZ", a.Oid);
        Assert.Equal("KDV1", a.Tur);
        Assert.Equal("1111111111", a.VknTckn);
        Assert.Equal("ÖRNEK OTOMOTİV İNŞAAT TURİZM LİMİTED ŞİRKETİ", a.Unvan);
        Assert.Equal("ULUÇINAR VD", a.VergiDairesi);
        Assert.Equal(new Donem(2026, 9, 2026, 9), a.Donem);
        Assert.Equal(new DateTime(2026, 10, 18, 16, 24, 25), a.YuklemeZamani);
        Assert.Equal("Onaylandı", a.Durum);
        Assert.Equal("6bmvcgozk81myi", a.TahakkukOid);
        Assert.False(a.IhbarnameVar);
        Assert.False(a.SgkBildirimVar);

        var b = l.Satirlar[1];
        Assert.Equal("MUHSGK", b.Tur);
        Assert.Equal("22222222222", b.VknTckn);
        Assert.Equal(new Donem(2026, 7, 2026, 9), b.Donem);
        Assert.True(b.IhbarnameVar);
        Assert.True(b.SgkBildirimVar);
    }

    [Fact]
    public void SayfalamaYoksaSatirSayisi()
        => Assert.Equal(2, EbynAyristirici.BeyannameListesi($"<table>{Satir1}{Satir2}</table>").Toplam);

    [Fact]
    public void IhbarnamelerAyristirilir()
    {
        var html = """<table><tr><td>V.U.K 344 Vergi Ziyaı</td><td><img onclick="ihbarnameGoruntule('11mvyz06ox1fro', '0zmvw9p1z11g7u', '0zmvw9p1z11g85', false,false)"/></td></tr></table>""";
        var l = EbynAyristirici.Ihbarnameler(html);
        var i = Assert.Single(l);
        Assert.Equal(("11mvyz06ox1fro", "0zmvw9p1z11g7u", "0zmvw9p1z11g85"), (i.BeyannameOid, i.TahakkukOid, i.TarhOid));
        Assert.Contains("344", i.Aciklama);
    }

    [Fact]
    public void SgkBildirimleriAyristirilir()
    {
        var html = """
            <table id="tahakkukTable">
            <tr id="row1fmvjav1hc1o2j" class="blAG" title="SGK Bildirimleri Listesi">
              <td align="center">09/2026-09/2026 </td>
              <td align="left">Asıl, TÜM SİG.KOLLARI/YABNC UYR, 05510-Say.Kan.MYO, 09 / 2026, 01234567890123000</td>
              <td><table><tr><td><img onclick="sgkTahakkukGoruntule('1xmvig6vtc19ai','1fmvjav1hc1o2j',false,false)"/></td>
                <td><img onclick="sgkHizmetGoruntule('1xmvig6vtc19ai','1fmvjav1hc1o2j',false,false)"/></td></tr></table></td>
            </tr>
            <tr id="row1fmvglaf4q1iik" class="blKG" title="SGK Bildirimleri Listesi">
              <td align="center">09/2026-09/2026 </td>
              <td align="left">Asıl, TÜM SİG.KOLLARI, 05510-Say.Kan.MYO, 09 / 2026, 1</td>
              <td><table><tr><td id="durumDetay1fmvglaf4q1iik"><img src="images/err.gif"/> &nbsp;Hatalı</td></tr></table></td>
            </tr>
            </table>
            """;
        var l = EbynAyristirici.SgkBildirimleri(html, "1xmvig6vtc19ai");
        Assert.Equal(2, l.Count);
        Assert.True(l[0].PdfVar);
        Assert.False(l[1].PdfVar);
        Assert.StartsWith("Asıl, TÜM SİG.KOLLARI", l[0].Aciklama);
        Assert.Equal("09/2026-09/2026", l[0].Donem);
    }

    [Theory]
    [InlineData("cmd=BEYANNAMELISTESI&sorguTipiZ=1")]
    [InlineData("cmd=IHBARNAMELERIGETIR&beyannameOid=x")]
    [InlineData("cmd=THKESASBILGISGKMESAJLARI&beyannameOid=x")]
    [InlineData("cmd=IMAJ&subcmd=TAHAKKUKGORUNTULE&beyannameOid=x")]
    [InlineData("cmd=IMAJ&subcmd=SGKHIZMETGORUNTULE&beyannameOid=x")]
    public void IzinliKomutlar(string p) => EbynKomutlari.Dogrula(p);

    [Theory]
    [InlineData("cmd=BEYANNAMEONAY&o=x")]
    [InlineData("cmd=BEYANNAMEOZELONAY&oz=x")]
    [InlineData("cmd=BEYANNAMEIPTAL&i=x")]
    [InlineData("cmd=MUHSGKMANUELIPTALGONDER&beyannameOid=x")]
    [InlineData("cmd=MUHSGKMANUELONAYGONDER&beyannameOid=x")]
    [InlineData("cmd=PAKETYUKLEME")]
    [InlineData("cmd=IMAJ&subcmd=BASKABIRSEY")]
    [InlineData("sorguTipiZ=1")]
    public void GeriAlinamazKomutlarReddedilir(string p)
        => Assert.Throws<InvalidOperationException>(() => EbynKomutlari.Dogrula(p));

    [Fact]
    public void AylikDilimler()
    {
        var d = EbynKomutlari.AylikDilimler(new DateTime(2026, 10, 17), new DateTime(2026, 12, 5)).ToList();
        Assert.Equal([(new DateTime(2026, 10, 17), new DateTime(2026, 10, 31)),
                      (new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)),
                      (new DateTime(2026, 12, 1), new DateTime(2026, 12, 5))], d);
    }

    [Fact]
    public void MukellefSorgusuKimlikTurunuSecer()
    {
        var d = new Donem(2026, 1, 2026, 12);
        Assert.Contains("sorguTipiN=1&vergiNo=1111111111", EbynKomutlari.ListeMukellefTurDonem("1111111111", "KDV1", d, new(2026, 1, 1), new(2026, 12, 31)));
        Assert.Contains("sorguTipiT=1&tcKimlikNo=22222222222", EbynKomutlari.ListeMukellefTurDonem("22222222222", "KDV1", d, new(2026, 1, 1), new(2026, 12, 31)));
    }

    sealed class SahteTasiyici : IEbynTasiyici
    {
        public List<string> Postlar = [];
        public Queue<byte[]> Getler = new();
        public Func<string, string> Yanit = _ => "";
        public Task<string> PostAsync(string p, CancellationToken ct) { Postlar.Add(p); return Task.FromResult(Yanit(p)); }
        public Task<byte[]> GetAsync(string u, CancellationToken ct) => Task.FromResult(Getler.Dequeue());
    }

    [Fact]
    public async Task TumSayfalarCekilir()
    {
        var t = new SahteTasiyici { Yanit = p => $"<SERVICERESULT><TOKEN>t</TOKEN><SERVERERROR></SERVERERROR><HTMLCONTENT>{Liste("1 - 25 / 60")}</HTMLCONTENT></SERVICERESULT>" };
        var ist = new EbynIstemci(t) { IstekAraligi = TimeSpan.Zero };
        await ist.OnayliBeyannamelerAsync(new(2026, 10, 17), new(2026, 11, 16), null, default);
        // 2 aylık dilim × 3 sayfa
        Assert.Equal(6, t.Postlar.Count);
        Assert.Contains(t.Postlar, p => p.EndsWith("&pageNo=3"));
    }

    [Fact]
    public async Task PdfDegilseYenidenDenenir()
    {
        var t = new SahteTasiyici();
        t.Getler.Enqueue("<html>İki görüntüleme isteği arası en az 1 sn olabilir.</html>"u8.ToArray());
        t.Getler.Enqueue("%PDF-1.4 ..."u8.ToArray());
        var ist = new EbynIstemci(t) { IstekAraligi = TimeSpan.Zero };
        var b = await ist.PdfAsync(EbynKomutlari.BeyannamePdf("x"), default);
        Assert.True(EbynIstemci.PdfMi(b));
    }
}
