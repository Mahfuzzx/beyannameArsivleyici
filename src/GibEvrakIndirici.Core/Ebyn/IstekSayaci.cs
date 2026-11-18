using System.Globalization;

namespace GibEvrakIndirici.Core.Ebyn;

public enum IstekSonucu { Pdf, Bos, Hata }

/// <summary>
/// GİB'e giden her PDF (IMAJ) isteğini kalıcı olarak sayar: indirmeler, sürüm karşılaştırması için alınan PDF'ler ve
/// boş yanıtlar dahil (§12.5). Kota türünü (günlük / kayan 24 saat / saatlik) ölçmek için.
/// Her gün bir dosya: {klasor}\yyyy-MM-dd.txt, satır başına "HH:mm:ss P|B|H". Dosyaya eklenir; program kapansa da sayım sürer.
/// Yalnız bu bilgisayarın isteklerini görür (diğer birim aynı kotayı kullanır).
/// </summary>
public sealed class IstekSayaci
{
    readonly string _klasor;
    readonly Func<DateTime> _simdi;
    readonly object _kilit = new();
    readonly List<(DateTime Zaman, IstekSonucu Sonuc)> _son = []; // dün + bugün (kayan pencereler için)

    public IstekSayaci(string klasor, Func<DateTime>? simdi = null)
    {
        _klasor = klasor;
        _simdi = simdi ?? (() => DateTime.Now);
        var bugun = _simdi().Date;
        foreach (var gun in new[] { bugun.AddDays(-1), bugun })
            _son.AddRange(Oku(gun));
    }

    string Dosya(DateTime gun) => Path.Combine(_klasor, gun.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".txt");

    IEnumerable<(DateTime, IstekSonucu)> Oku(DateTime gun)
    {
        var yol = Dosya(gun);
        if (!File.Exists(yol)) yield break;
        foreach (var satir in File.ReadLines(yol))
        {
            var p = satir.Split(' ');
            if (p.Length < 2 || !TimeSpan.TryParseExact(p[0], @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var s)) continue;
            yield return (gun + s, p[1] switch { "P" => IstekSonucu.Pdf, "B" => IstekSonucu.Bos, _ => IstekSonucu.Hata });
        }
    }

    /// <summary>Bu program açıldığından beri sayılan istek.</summary>
    public int ProgramToplami { get; private set; }

    public void Kaydet(IstekSonucu sonuc)
    {
        lock (_kilit)
        {
            ProgramToplami++;
            var t = _simdi();
            _son.Add((t, sonuc));
            _son.RemoveAll(x => x.Zaman < t.Date.AddDays(-1));
            try
            {
                Directory.CreateDirectory(_klasor);
                File.AppendAllText(Dosya(t), $"{t:HH\\:mm\\:ss} {(sonuc == IstekSonucu.Pdf ? "P" : sonuc == IstekSonucu.Bos ? "B" : "H")}{Environment.NewLine}");
            }
            catch (IOException) { } // sayım indirmeyi durdurmasın
            catch (UnauthorizedAccessException) { }
        }
    }

    public sealed record Ozet(int Bugun, int BugunBos, int Son60Dk, int Son24Saat);

    public Ozet Al()
    {
        lock (_kilit)
        {
            var t = _simdi();
            return new Ozet(
                _son.Count(x => x.Zaman.Date == t.Date),
                _son.Count(x => x.Zaman.Date == t.Date && x.Sonuc == IstekSonucu.Bos),
                _son.Count(x => x.Zaman > t.AddHours(-1) && x.Zaman <= t),
                _son.Count(x => x.Zaman > t.AddDays(-1) && x.Zaman <= t));
        }
    }

    public string Metin()
    {
        var o = Al();
        return $"GİB PDF isteği — bugün {o.Bugun} (boş yanıt {o.BugunBos}), son 60 dk {o.Son60Dk}, son 24 saat {o.Son24Saat}";
    }
}
