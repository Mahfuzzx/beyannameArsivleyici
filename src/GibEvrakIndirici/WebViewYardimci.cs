using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace GibEvrakIndirici;

/// <summary>Sayfa içinde Promise döndüren JS çalıştırır (DevTools Runtime.evaluate, awaitPromise).</summary>
static class WebViewYardimci
{
    public static async Task<string> DegerlendirAsync(CoreWebView2 cw, string ifade)
    {
        var parametre = JsonSerializer.Serialize(new { expression = ifade, awaitPromise = true, returnByValue = true });
        var json = await cw.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parametre);
        using var doc = JsonDocument.Parse(json);
        var kok = doc.RootElement;
        if (kok.TryGetProperty("exceptionDetails", out var ex))
            throw new InvalidOperationException("Sayfa betiği hatası: " + (ex.TryGetProperty("exception", out var e) && e.TryGetProperty("description", out var d) ? d.GetString() : ex.ToString()));
        var sonuc = kok.GetProperty("result");
        // Dize dışındaki değerler JSON metni olarak döner (true → "true"; JsonElement.ToString() "True" verirdi)
        return sonuc.TryGetProperty("value", out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText()) : "";
    }

    public static string JsDize(string s) => JsonSerializer.Serialize(s);
}
