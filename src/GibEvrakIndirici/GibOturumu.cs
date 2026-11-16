using GibEvrakIndirici.Core.Ebyn;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GibEvrakIndirici;

/// <summary>
/// Dijital Vergi Dairesi girişi (kullanıcı elle: şifre + doğrulama kodu) → e-Beyanname'ye otomatik geçiş (§1–§3).
/// Uygulama şifre saklamaz, doğrulama kodunu çözmeye çalışmaz.
/// </summary>
sealed class GibOturumu : IEbynTasiyici
{
    public const string GirisAdresi = "https://dijital.gib.gov.tr/portal/login";

    readonly WebView2 _web;
    readonly System.Windows.Forms.Timer _izle = new() { Interval = 1000 };
    DateTime _sonGecisDenemesi = DateTime.MinValue;
    bool _hazir;

    public event EventHandler? EbeyannameHazir;
    public event EventHandler<string>? DurumDegisti;
    public bool Hazir => _hazir;

    public GibOturumu(WebView2 web)
    {
        _web = web;
        _izle.Tick += async (_, _) => await IzleAsync();
    }

    public async Task BaslatAsync()
    {
        await _web.EnsureCoreWebView2Async();
        var cw = _web.CoreWebView2;
        // e-Beyanname yeni pencerede açılmak ister → aynı görünümde aç
        cw.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Host.EndsWith("gib.gov.tr", StringComparison.OrdinalIgnoreCase))
                cw.Navigate(e.Uri);
        };
        cw.Navigate(GirisAdresi);
        _izle.Start();
        DurumDegisti?.Invoke(this, "GİB girişi bekleniyor — bilgilerinizi ve doğrulama kodunu girin");
    }

    public void YenidenGiris()
    {
        _hazir = false;
        _web.CoreWebView2?.Navigate(GirisAdresi);
        _izle.Start();
        DurumDegisti?.Invoke(this, "GİB girişi bekleniyor");
    }

    async Task IzleAsync()
    {
        var cw = _web.CoreWebView2;
        if (cw is null) return;
        var adres = cw.Source ?? "";
        try
        {
            if (adres.StartsWith("https://ebeyanname.gib.gov.tr", StringComparison.OrdinalIgnoreCase))
            {
                var tamam = await WebViewYardimci.DegerlendirAsync(cw, "typeof getTOKEN==='function' && !!getTOKEN()");
                if (tamam == "true" && !_hazir)
                {
                    _hazir = true;
                    _izle.Stop();
                    DurumDegisti?.Invoke(this, "e-Beyanname oturumu açık");
                    EbeyannameHazir?.Invoke(this, EventArgs.Empty);
                }
                return;
            }

            // DVD paneline girildiyse (login sayfası değil) e-Beyanname geçişini tetikle
            if (adres.StartsWith("https://dijital.gib.gov.tr/portal", StringComparison.OrdinalIgnoreCase)
                && !adres.Contains("/login", StringComparison.OrdinalIgnoreCase)
                && DateTime.Now - _sonGecisDenemesi > TimeSpan.FromSeconds(8))
            {
                _sonGecisDenemesi = DateTime.Now;
                DurumDegisti?.Invoke(this, "e-Beyanname'ye geçiliyor…");
                await WebViewYardimci.DegerlendirAsync(cw, EbeyannameGecisBetigi);
            }
        }
        catch (Exception ex)
        {
            DurumDegisti?.Invoke(this, "Geçiş denemesi: " + ex.Message);
        }
    }

    // "Geçiş Yapılabilecek Uygulamalar" → e-Beyanname → onay penceresi "ONAYLA"
    const string EbeyannameGecisBetigi = """
        (async () => {
          const bekle = ms => new Promise(r => setTimeout(r, ms));
          for (let i = 0; i < 10; i++) {
            const el = [...document.querySelectorAll('li.react-multi-carousel-item--active p, p')]
                         .find(p => p.textContent.trim() === 'e-Beyanname');
            if (el) { (el.parentElement || el).click(); break; }
            await bekle(500);
          }
          for (let i = 0; i < 10; i++) {
            const b = [...document.querySelectorAll('button')].find(x => x.textContent.trim().toUpperCase() === 'ONAYLA');
            if (b) { b.click(); return 'onaylandi'; }
            await bekle(500);
          }
          return 'onay-yok';
        })()
        """;

    // ---------------- IEbynTasiyici ----------------

    public async Task<string> PostAsync(string parametreler, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        OturumKontrol();
        var js = $$"""
            (async () => {
              const r = await fetch('/dispatch', { method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: {{WebViewYardimci.JsDize(parametreler)}} + '&' + getParameterStringFromObjID('TOKEN') });
              const t = await r.text();
              const m = t.match(/<TOKEN>([^<]*)<\/TOKEN>/);
              if (m && m[1]) setTOKEN(m[1]);
              return t;
            })()
            """;
        return await WebViewYardimci.DegerlendirAsync(_web.CoreWebView2, js);
    }

    public async Task<byte[]> GetAsync(string goreliUrl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        OturumKontrol();
        var js = $$"""
            (async () => {
              const r = await fetch({{WebViewYardimci.JsDize(goreliUrl)}} + '&TOKEN=' + getTOKEN());
              const b = new Uint8Array(await r.arrayBuffer());
              let s = ''; const n = 0x8000;
              for (let i = 0; i < b.length; i += n) s += String.fromCharCode.apply(null, b.subarray(i, i + n));
              return btoa(s);
            })()
            """;
        return Convert.FromBase64String(await WebViewYardimci.DegerlendirAsync(_web.CoreWebView2, js));
    }

    void OturumKontrol()
    {
        var adres = _web.CoreWebView2?.Source ?? "";
        if (!_hazir || !adres.StartsWith("https://ebeyanname.gib.gov.tr", StringComparison.OrdinalIgnoreCase))
            throw new EbynHatasi("e-Beyanname oturumu kapalı. Yeniden giriş yapın.");
    }
}
