using System.Diagnostics;
using GibEvrakIndirici.Core.Ebyn;

namespace GibEvrakIndirici.Tests;

/// <summary>GİB'in "iki istek arası en az 1 sn" kuralı (§12.3): aralık yanıtın bitişinden ve tüm istemcilerde ortak sayılır.</summary>
public class IstekAraligiTestleri
{
    const string Basarili = "<SERVICERESULT><TOKEN>t</TOKEN><SERVERERROR></SERVERERROR><EYEKSERROR></EYEKSERROR><HTMLCONTENT>x</HTMLCONTENT></SERVICERESULT>";
    const string AralikHatasi = "<SERVICERESULT><TOKEN>t</TOKEN><SERVERERROR>İki istek arası en az 1 sn olabilir.</SERVERERROR><EYEKSERROR></EYEKSERROR><HTMLCONTENT></HTMLCONTENT></SERVICERESULT>";

    sealed class Tasiyici(TimeSpan sure, params string[] yanitlar) : IEbynTasiyici
    {
        readonly Stopwatch _sw = Stopwatch.StartNew();
        int _i;
        public List<(TimeSpan Bas, TimeSpan Bit)> Istekler { get; } = [];
        public async Task<string> PostAsync(string p, CancellationToken ct)
        {
            var bas = _sw.Elapsed;
            await Task.Delay(sure, ct);
            Istekler.Add((bas, _sw.Elapsed));
            return yanitlar[Math.Min(_i++, yanitlar.Length - 1)];
        }
        public Task<byte[]> GetAsync(string u, CancellationToken ct) => Task.FromResult("%PDF-1.4 x"u8.ToArray());
    }

    static readonly string Liste = EbynKomutlari.ListeYuklemeTarihi(new DateTime(2026, 11, 1), new DateTime(2026, 11, 18), 1);

    [Fact]
    public async Task AralikYanitBitisindenSayilir()
    {
        var t = new Tasiyici(TimeSpan.FromMilliseconds(300), Basarili);
        var i = new EbynIstemci(t) { IstekAraligi = TimeSpan.FromMilliseconds(200) };
        await i.PostAsync(Liste, default);
        await i.PostAsync(Liste, default);
        Assert.True(t.Istekler[1].Bas - t.Istekler[0].Bit >= TimeSpan.FromMilliseconds(180), $"{t.Istekler[1].Bas - t.Istekler[0].Bit}");
    }

    [Fact]
    public async Task YeniIstemciAyniOturumdaBekler()
    {
        var t = new Tasiyici(TimeSpan.Zero, Basarili);
        await new EbynIstemci(t) { IstekAraligi = TimeSpan.FromMilliseconds(200) }.PostAsync(Liste, default);
        await new EbynIstemci(t) { IstekAraligi = TimeSpan.FromMilliseconds(200) }.PostAsync(Liste, default);
        Assert.True(t.Istekler[1].Bas - t.Istekler[0].Bit >= TimeSpan.FromMilliseconds(180), $"{t.Istekler[1].Bas - t.Istekler[0].Bit}");
    }

    [Fact]
    public async Task AralikHatasindaBekleyipYenidenDener()
    {
        var t = new Tasiyici(TimeSpan.Zero, AralikHatasi, AralikHatasi, Basarili);
        var y = await new EbynIstemci(t) { IstekAraligi = TimeSpan.FromMilliseconds(10) }.PostAsync(Liste, default);
        Assert.True(y.Basarili);
        Assert.Equal(3, t.Istekler.Count);
    }

    [Fact]
    public async Task AralikHatasiSurerseHataDoner()
    {
        var t = new Tasiyici(TimeSpan.Zero, AralikHatasi);
        var y = await new EbynIstemci(t) { IstekAraligi = TimeSpan.FromMilliseconds(10), AralikHatasiDenemesi = 2 }.PostAsync(Liste, default);
        Assert.False(y.Basarili);
        Assert.Equal(3, t.Istekler.Count);
    }
}
