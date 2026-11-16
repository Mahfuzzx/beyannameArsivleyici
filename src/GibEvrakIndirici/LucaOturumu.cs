using GibEvrakIndirici.Core.Luca;
using Microsoft.Web.WebView2.WinForms;

namespace GibEvrakIndirici;

/// <summary>
/// Luca ortak giriş → "Luca Mali Müşavir Paketi" (yeni pencere yerine aynı görünümde) → işyeri okuma (§11).
/// Giriş bilgilerini kullanıcı kendisi girer.
/// </summary>
sealed class LucaOturumu(WebView2 web) : ILucaTasiyici
{
    public const string GirisAdresi = "https://agiris.luca.com.tr/LUCASSO/main.erp";
    const string PaketKoku = "https://auygs.luca.com.tr/Luca/";

    // Ortak giriş sayfası paketi yeni pencerede açar (window.open + form target) → aynı pencereye zorla
    const string AyniPencereBetigi = """
        if (location.host === 'agiris.luca.com.tr') {
          window.open = function () { return { closed: false, focus() {}, location: {} }; };
          const gonder = HTMLFormElement.prototype.submit;
          HTMLFormElement.prototype.submit = function () { this.target = '_self'; return gonder.call(this); };
        }
        """;

    public bool PaketAcik => (web.CoreWebView2?.Source ?? "").StartsWith(PaketKoku, StringComparison.OrdinalIgnoreCase);

    public async Task BaslatAsync()
    {
        await web.EnsureCoreWebView2Async();
        await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(AyniPencereBetigi);
        web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; web.CoreWebView2.Navigate(e.Uri); };
        web.CoreWebView2.Navigate(GirisAdresi);
    }

    public async Task<string> GetAsync(string yol, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!PaketAcik) throw new InvalidOperationException("Luca Mali Müşavir Paketi açık değil.");
        var js = $"(async()=>{{const r=await fetch('/Luca/'+{WebViewYardimci.JsDize(yol)},{{credentials:'include'}});return new TextDecoder('utf-8').decode(await r.arrayBuffer());}})()";
        return await WebViewYardimci.DegerlendirAsync(web.CoreWebView2, js);
    }

    public async Task<(string SirketId, string DonemId, string DonemTxt)?> AktifFirmaAsync(CancellationToken ct)
    {
        var js = """
            (() => { try {
              const u = new URL(document.getElementById('frm4').contentWindow.location.href);
              return [u.searchParams.get('SIRKET_ID')||'', u.searchParams.get('DONEM_ID')||'', u.searchParams.get('DONEM_TXT')||''].join('|');
            } catch (e) { return ''; } })()
            """;
        var s = await WebViewYardimci.DegerlendirAsync(web.CoreWebView2, js);
        var p = s.Split('|');
        return p.Length == 3 && p[0].Length > 0 ? (p[0], p[1], p[2]) : null;
    }
}
