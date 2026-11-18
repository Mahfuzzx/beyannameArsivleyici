namespace GibEvrakIndirici.Core.Ebyn;

/// <summary>
/// e-Beyanname oturumuna istek taşıyan katman. TOKEN sayfada (getTOKEN/setTOKEN) tutulduğu için
/// gerçek uygulama bunu WebView2 içinde çalıştırır; testler sahte bir taşıyıcı kullanır.
/// </summary>
public interface IEbynTasiyici
{
    /// <summary>POST /dispatch — gövdeye TOKEN eklenir, yanıttaki yeni TOKEN oturuma yazılır. Ham yanıtı döner.</summary>
    Task<string> PostAsync(string parametreler, CancellationToken ct);

    /// <summary>GET {goreliUrl}&amp;TOKEN=... — ham baytları döner.</summary>
    Task<byte[]> GetAsync(string goreliUrl, CancellationToken ct);
}

public class EbynHatasi(string mesaj) : Exception(mesaj);

/// <summary>e-Beyanname oturumu düştü (EYEKSERROR "Oturum zamanaşımına uğradı"). İndirme durur; yeniden giriş gerekir.</summary>
public sealed class EbynOturumHatasi(string mesaj) : EbynHatasi(mesaj);

/// <summary>
/// GİB görüntüleme isteğine boş/PDF olmayan yanıt veriyor (kısa sürede çok istek sonrası görülen kısıtlama, §12.1).
/// Bekleyip aynı evrakla devam edilir; sürerse indirme durdurulur.
/// </summary>
public sealed class EbynKisitlamaHatasi(string mesaj) : EbynHatasi(mesaj);

/// <summary>
/// Beyaz liste denetimi, hız sınırı (görüntülemeler arası ≥1 sn, §6), PDF doğrulama ve yeniden deneme.
/// </summary>
public sealed class EbynIstemci(IEbynTasiyici tasiyici, TimeProvider? zaman = null)
{
    readonly TimeProvider _zaman = zaman ?? TimeProvider.System;

    // Son isteğin bitiş zamanı aynı oturumu (taşıyıcıyı) kullanan tüm istemcilerde ortak: her iş yeni bir istemci kurar,
    // GİB ise oturum başına sayar. Süre yanıtın gelişinden ölçülür: uzun süren liste sorgusundan hemen sonra gönderilen istek
    // "İki istek arası en az 1 sn olabilir" hatası alıyordu (§12.3).
    sealed class Saat { public DateTimeOffset Son = DateTimeOffset.MinValue; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IEbynTasiyici, Saat> Saatler = new();
    readonly Saat _saat = Saatler.GetValue(tasiyici, _ => new Saat());
    DateTimeOffset _sonIstek { get => _saat.Son; set => _saat.Son = value; }

    /// <summary>"İki istek arası en az 1 sn" yanıtında kaç kez beklenip yeniden denenir.</summary>
    public int AralikHatasiDenemesi { get; init; } = 3;

    static bool AralikHatasi(string metin) => metin.Contains("en az 1 sn", StringComparison.OrdinalIgnoreCase);

    public TimeSpan IstekAraligi { get; init; } = TimeSpan.FromMilliseconds(1500);
    public int DenemeSayisi { get; init; } = 3;
    /// <summary>İlk boş yanıttan sonra ikinci deneme öncesi bekleme.</summary>
    public TimeSpan BosYanitBeklemesi { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Her PDF isteğini kalıcı olarak sayar (kota ölçümü, §12.5).</summary>
    public IstekSayaci? Sayac { get; init; }

    async Task BekleAsync(CancellationToken ct)
    {
        var gecen = _zaman.GetUtcNow() - _sonIstek;
        if (gecen < IstekAraligi) await Task.Delay(IstekAraligi - gecen, _zaman, ct);
        _sonIstek = _zaman.GetUtcNow();
    }

    public async Task<EbynYanit> PostAsync(string parametreler, CancellationToken ct)
    {
        EbynKomutlari.Dogrula(parametreler);
        for (int deneme = 0; ; deneme++)
        {
            await BekleAsync(ct);
            string ham;
            try { ham = await tasiyici.PostAsync(parametreler, ct); }
            finally { _sonIstek = _zaman.GetUtcNow(); }
            var y = EbynYanit.Ayristir(ham);
            if (y.OturumDustu) throw new EbynOturumHatasi("e-Beyanname oturumu zaman aşımına uğradı: " + y.Hata);
            if (!y.Basarili && AralikHatasi(y.Hata) && deneme < AralikHatasiDenemesi)
            {
                await Task.Delay(IstekAraligi, _zaman, ct);
                continue;
            }
            return y;
        }
    }

    public async Task<byte[]> PdfAsync(string goreliUrl, CancellationToken ct)
    {
        EbynKomutlari.Dogrula(goreliUrl[(goreliUrl.IndexOf('?') + 1)..]);
        string sonHata = "";
        int bosYanit = 0;
        for (int deneme = 1; deneme <= DenemeSayisi; deneme++)
        {
            await BekleAsync(ct);
            byte[] b;
            try { b = await tasiyici.GetAsync(goreliUrl, ct); }
            finally { _sonIstek = _zaman.GetUtcNow(); }
            if (PdfMi(b)) { Sayac?.Kaydet(IstekSonucu.Pdf); return b; }

            var metin = System.Text.Encoding.UTF8.GetString(b);
            var y = EbynYanit.Ayristir(metin);
            if (y.OturumDustu) throw new EbynOturumHatasi("e-Beyanname oturumu zaman aşımına uğradı: " + y.Hata);
            var aciklama = y.Hata.Length > 0 ? y.Hata : EbynAyristirici.Metin(y.Html);
            Sayac?.Kaydet(aciklama.Length == 0 ? IstekSonucu.Bos : IstekSonucu.Hata);
            if (aciklama.Length == 0)
            {
                // Boş yanıt: kısıtlama belirtisi. Sunucuyu yormamak için ikinci boş yanıtta vazgeç.
                sonHata = $"GİB boş yanıt verdi ({b.Length} bayt)";
                if (++bosYanit >= 2)
                    throw new EbynKisitlamaHatasi("PDF alınamadı: " + sonHata + (Sayac is null ? "" : $" [{Sayac.Metin()}]"));
                await Task.Delay(BosYanitBeklemesi, _zaman, ct);
                continue;
            }
            sonHata = aciklama.Length > 200 ? aciklama[..200] + "…" : aciklama;
            await Task.Delay(TimeSpan.FromSeconds(deneme), _zaman, ct);
        }
        throw new EbynHatasi($"PDF alınamadı: {sonHata}");
    }

    public static bool PdfMi(byte[] b) => b.Length > 5 && b[0] == '%' && b[1] == 'P' && b[2] == 'D' && b[3] == 'F' && b[4] == '-';

    /// <summary>Yükleme tarihi aralığındaki tüm onaylı beyannameler (aylık dilim + tüm sayfalar).</summary>
    /// <param name="oran">(biten, toplam) aylık dilim — ilerleme çubuğu için.</param>
    public async Task<List<BeyannameSatiri>> OnayliBeyannamelerAsync(DateTime bas, DateTime bit, IProgress<string>? ilerleme, CancellationToken ct,
        IProgress<(int Biten, int Toplam)>? oran = null)
    {
        var sonuc = new List<BeyannameSatiri>();
        var dilimler = EbynKomutlari.AylikDilimler(bas, bit).ToList();
        int bitenDilim = 0;
        oran?.Report((0, dilimler.Count));
        foreach (var (b, s) in dilimler)
        {
            int sayfa = 1, sayfaSayisi = 1;
            do
            {
                ilerleme?.Report($"Listeleniyor: {b:dd.MM.yyyy}–{s:dd.MM.yyyy}, sayfa {sayfa}/{sayfaSayisi}");
                var y = await PostAsync(EbynKomutlari.ListeYuklemeTarihi(b, s, sayfa), ct);
                if (!y.Basarili)
                {
                    if (y.Hata.Contains("bulunamam", StringComparison.OrdinalIgnoreCase)) break;
                    throw new EbynHatasi(y.Hata);
                }
                var l = EbynAyristirici.BeyannameListesi(y.Html);
                sonuc.AddRange(l.Satirlar);
                sayfaSayisi = l.SayfaSayisi;
                sayfa++;
            } while (sayfa <= sayfaSayisi);
            oran?.Report((++bitenDilim, dilimler.Count));
        }
        // Aynı OID iki dilimde gelmesin
        return sonuc.GroupBy(x => x.Oid).Select(g => g.First()).ToList();
    }

    /// <summary>
    /// Tek mükellefin seçili türlerdeki onaylı beyannameleri, vergilendirme dönemi aralığına göre (§9.1 istisnası:
    /// VKN/TCKN + tür + dönem verilince uzun yükleme aralığı kabul edilir). Yükleme aralığı = dönem başı → bugün.
    /// Sonuç, başlangıç ayı aralık içinde kalan dönemlerle sınırlanır.
    /// </summary>
    public async Task<List<BeyannameSatiri>> MukellefBeyannameleriAsync(string vknTckn, IEnumerable<string> turler, Donem aralik,
        DateTime bugun, IProgress<string>? ilerleme, CancellationToken ct, Action? turBitti = null)
    {
        var sonuc = new List<BeyannameSatiri>();
        var yukBas = new DateTime(aralik.BasYil, aralik.BasAy, 1);
        int bas = aralik.BasYil * 12 + aralik.BasAy, bit = aralik.BitYil * 12 + aralik.BitAy;
        foreach (var tur in turler)
        {
            int sayfa = 1, sayfaSayisi = 1;
            do
            {
                ilerleme?.Report($"{vknTckn} {tur}: sayfa {sayfa}/{sayfaSayisi}");
                var y = await PostAsync(EbynKomutlari.ListeMukellefTurDonem(vknTckn, tur, aralik, yukBas, bugun, sayfa), ct);
                if (!y.Basarili)
                {
                    if (y.Hata.Contains("bulunamam", StringComparison.OrdinalIgnoreCase)) break;
                    throw new EbynHatasi($"{tur}: {y.Hata}");
                }
                var l = EbynAyristirici.BeyannameListesi(y.Html);
                sonuc.AddRange(l.Satirlar.Where(s => s.Donem.BasYil * 12 + s.Donem.BasAy is var a && a >= bas && a <= bit));
                sayfaSayisi = l.SayfaSayisi;
                sayfa++;
            } while (sayfa <= sayfaSayisi);
            turBitti?.Invoke();
        }
        return sonuc.GroupBy(x => x.Oid).Select(g => g.First()).ToList();
    }

    public async Task<IReadOnlyList<IhbarnameBilgisi>> IhbarnamelerAsync(string beyannameOid, CancellationToken ct)
    {
        var y = await PostAsync(EbynKomutlari.Ihbarnameler(beyannameOid), ct);
        if (!y.Basarili) throw new EbynHatasi(y.Hata);
        return EbynAyristirici.Ihbarnameler(y.Html);
    }

    public async Task<IReadOnlyList<SgkBildirimi>> SgkBildirimleriAsync(string beyannameOid, CancellationToken ct)
    {
        var y = await PostAsync(EbynKomutlari.SgkBildirimleri(beyannameOid), ct);
        if (!y.Basarili) throw new EbynHatasi(y.Hata);
        return EbynAyristirici.SgkBildirimleri(y.Html, beyannameOid);
    }
}
