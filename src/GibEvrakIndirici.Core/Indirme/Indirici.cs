using GibEvrakIndirici.Core.Adlandirma;
using GibEvrakIndirici.Core.Arsiv;
using GibEvrakIndirici.Core.Ebyn;
using GibEvrakIndirici.Core.Musteriler;
using GibEvrakIndirici.Core.Sgk;

namespace GibEvrakIndirici.Core.Indirme;

public enum Seviye { Bilgi, Indirildi, Atlandi, Uyari, Hata }

public sealed record IndirmeOlayi(Seviye Seviye, string Mesaj, string? Dosya = null);

public sealed class IndirmeOzeti
{
    public int Indirilen, Atlanan, Uyari, Hata;
    /// <summary>Birim filtresi dışında kaldığı için hiç işlenmeyen beyanname satırı.</summary>
    public int FiltreDisi;
    /// <summary>Oturum düştüğü veya GİB kısıtlaması sürdüğü için yarıda kesildi.</summary>
    public bool Durduruldu;
}

/// <summary>Bilinmeyen sicil için kullanıcıya sorulan soru (birden fazla işyeri olan müşteride).</summary>
public sealed record IsyeriSorusu(Musteri Musteri, string Sicil, IReadOnlyList<string> BilinenIsyerleri);

/// <summary>Bir satırın arşivdeki durumu (tablo gösterimi için; ihbarname/SGK ek sorgu gerektirdiği için "bilinmiyor" olabilir).</summary>
public sealed record SatirDurumu(Musteri? Musteri, bool? Beyanname, bool? Tahakkuk, int DuzeltmeSira);

/// <summary>
/// Onaylı beyanname satırlarından eksik evrakları indirir ve arşiv kurallarına göre yerleştirir.
/// Var olan dosyanın üzerine asla yazmaz.
/// </summary>
public sealed class Indirici(
    EbynIstemci istemci,
    MusteriDeposu musteriler,
    IsyeriDeposu isyerleri,
    Ayarlar ayarlar,
    Func<IsyeriSorusu, Task<string?>> isyeriSor,
    Action? isyerleriDegisti = null,
    Func<TimeSpan, CancellationToken, Task>? bekle = null,
    TamamlananDeposu? tamamlanan = null,
    ArsivDizini? dizin = null)
{
    readonly Func<TimeSpan, CancellationToken, Task> _bekle = bekle ?? Task.Delay;
    readonly TamamlananDeposu _tamamlanan = tamamlanan ?? new();
    readonly ArsivDizini _dizin = dizin ?? new();

    /// <summary>
    /// Pencere dışındaki eski asıl beyannameyi de hesaba katan düzeltme sırası: listedeki sıra ile arşivde bu satırdan
    /// önce yüklenmiş (adındaki damga en az 10 dk eski) beyanname dosyası sayısının büyüğü (§9.2).
    /// </summary>
    public int EtkinSira(BeyannameSatiri b, Musteri m, int listeSira)
    {
        var k = ArsivDenetci.BeyannameKlasoru(ayarlar.YArsivKok, m, b);
        var arsivde = ArsivDenetci.OncekiYuklemeSayisi(k, DosyaAdlari.BeyannameOnek(b, EvrakTuru.Beyanname), b.YuklemeZamani.AddMinutes(-10));
        return Math.Max(listeSira, arsivde);
    }

    public SatirDurumu Durum(BeyannameSatiri b, int duzeltmeSira)
    {
        var m = musteriler.Bul(b.VknTckn);
        if (m is null) return new SatirDurumu(null, null, null, duzeltmeSira);
        duzeltmeSira = EtkinSira(b, m, duzeltmeSira);
        var k = ArsivDenetci.BeyannameKlasoru(ayarlar.YArsivKok, m, b);
        bool Var(EvrakTuru e) => ArsivDenetci.VarMi(k, DosyaAdlari.BeyannameOnek(b, e), DosyaAdlari.BeyannameDosyasi(b, e, duzeltmeSira), duzeltmeSira);
        return new SatirDurumu(m, Var(EvrakTuru.Beyanname), b.TahakkukOid is null ? null : Var(EvrakTuru.Tahakkuk), duzeltmeSira);
    }

    /// <param name="oran">(işlenen satır, toplam satır) — ilerleme çubuğu için.</param>
    public async Task<IndirmeOzeti> CalistirAsync(IReadOnlyList<BeyannameSatiri> satirlar, IProgress<IndirmeOlayi> ilerleme, CancellationToken ct,
        IProgress<(int Biten, int Toplam)>? oran = null)
    {
        int biten = 0;
        oran?.Report((0, satirlar.Count));
        var ozet = new IndirmeOzeti();
        var siralar = DuzeltmeSiralayici.Sirala(satirlar);
        var sonrakiler = DuzeltmeSiralayici.SonrakiYuklemeler(satirlar);

        void Bildir(Seviye s, string msj, string? dosya = null)
        {
            switch (s)
            {
                case Seviye.Indirildi: ozet.Indirilen++; break;
                case Seviye.Atlandi: ozet.Atlanan++; break;
                case Seviye.Uyari: ozet.Uyari++; break;
                case Seviye.Hata: ozet.Hata++; break;
            }
            ilerleme.Report(new IndirmeOlayi(s, msj, dosya));
        }

        foreach (var b in satirlar.OrderBy(x => x.YuklemeZamani))
        {
            ct.ThrowIfCancellationRequested();
            oran?.Report((biten++, satirlar.Count));
            var m = musteriler.Bul(b.VknTckn);
            if (m is null)
            {
                Bildir(Seviye.Uyari, $"{b.VknTckn} {b.Unvan}: müşteri listesinde yok, atlandı");
                continue;
            }
            if (!ayarlar.MusteriDahil(m)) { ozet.FiltreDisi++; continue; } // diğer birimin müşterisi: GİB'e istek yok
            var satirAnahtari = TamamlananDeposu.Anahtar(ayarlar, b.Oid, "SATIR");
            if (_tamamlanan.Var(satirAnahtari)) { ozet.Atlanan++; continue; }
            int sira = EtkinSira(b, m, siralar.GetValueOrDefault(b.Oid));
            for (int kisitlama = 0; ; kisitlama++)
            {
                try
                {
                    // Var olan dosyalar atlandığı için aynı satırı yeniden denemek güvenlidir.
                    var surum = await BeyannameEvraklariAsync(b, m, sira, sonrakiler.GetValueOrDefault(b.Oid), Bildir, ct);
                    if (b.IhbarnameVar) await IhbarnamelerAsync(b, m, sira, surum, Bildir, ct);
                    if (b.SgkBildirimVar) await SgkAsync(b, m, Bildir, ct);
                    // Satırın tüm evrakı arşivde → sonraki çalıştırmalarda hiç GİB isteği yapılmaz (PDF içerik karşılaştırması tekrarlanmaz)
                    if ((!b.IhbarnameVar || _tamamlanan.Var(TamamlananDeposu.Anahtar(ayarlar, b.Oid, "IHB")))
                        && (!b.SgkBildirimVar || _tamamlanan.Var(TamamlananDeposu.Anahtar(ayarlar, b.Oid, "SGK"))))
                        _tamamlanan.Ekle(satirAnahtari);
                    break;
                }
                catch (OperationCanceledException) { throw; }
                catch (EbynOturumHatasi) { throw; } // arayüz yeniden giriş ister
                catch (EbynKisitlamaHatasi ex)
                {
                    var beklemeler = ayarlar.KisitlamaBeklemeleri();
                    if (kisitlama >= beklemeler.Count)
                    {
                        Bildir(Seviye.Hata, $"{m.KisaAd} {b.Tur} {b.Donem}: {ex.Message}. GİB kısıtlaması sürüyor, indirme durduruldu. " +
                                            "Bir süre (ör. 1 saat) sonra \"Eksikleri indir\" ile kalan evraklar alınır.");
                        ozet.Durduruldu = true;
                        return ozet;
                    }
                    var sure = beklemeler[kisitlama];
                    Bildir(Seviye.Uyari, $"{m.KisaAd} {b.Tur} {b.Donem}: {ex.Message}. GİB kısıtlıyor olabilir; {sure.TotalMinutes:0} dk beklenip aynı evrakla devam edilecek.");
                    await _bekle(sure, ct);
                }
                catch (Exception ex)
                {
                    Bildir(Seviye.Hata, $"{m.KisaAd} {b.Tur} {b.Donem}: {ex.Message}");
                    break;
                }
            }
        }
        oran?.Report((satirlar.Count, satirlar.Count));
        return ozet;
    }

    /// <summary>
    /// Bir beyanname sürümünün arşivdeki durumu (§9.6).
    /// <para><see cref="Eslesen"/>: arşivde aynı onay zamanlı beyanname PDF'i (adı ne olursa olsun) → bu sürüm var.</para>
    /// <para><see cref="IcerikleYok"/>: arşivdeki aynı önekli tüm beyanname PDF'leri okundu, hiçbiri bu sürüm değil → kesin eksik.</para>
    /// İkisi de değilse karar eski önek sayımıyla verilir (PDF okunamadı).
    /// </summary>
    sealed record SurumDurumu(DateTime Zaman, string? Eslesen, bool IcerikleYok);

    /// <summary>Arşivdeki bir beyanname PDF'inin onay zamanı (kalıcı dizinden; okunamazsa null).</summary>
    DateTime? ArsivOnay(string yol) => _dizin.BeyannameOnay(yol);

    /// <summary>Aynı sürüm grubunda iki yükleme bundan daha yakınsa onay zamanı aralığı güvenilmez → GİB PDF'iyle kesin karşılaştırma.</summary>
    public static readonly TimeSpan YakinSurumPayi = TimeSpan.FromHours(2);
    /// <summary>Onay, yüklemeden en çok bu kadar sonra gelir sayılır (sonraki sürüm listede yoksa aralığın üst sınırı).</summary>
    public static readonly TimeSpan EnUzunOnaySuresi = TimeSpan.FromDays(1);

    /// <summary>
    /// GİB'e gitmeden eşleştirme (§9.7): bir sürümün onayı kendi yüklemesinden sonra, bir sonraki sürümün yüklemesinden önce olur.
    /// Arşivdeki dosyalardan onay zamanı [yükleme, min(sonraki yükleme, yükleme + 1 gün)) aralığına düşen = bu sürüm.
    /// Belirsiz: bir aday okunamadı ya da sonraki sürüm 2 saatten yakın yüklendi → çağıran GİB PDF'iyle kesin karşılaştırır.
    /// </summary>
    public static (string? Eslesen, bool Belirsiz) AraliktaEslesen(IReadOnlyList<(string Ad, DateTime? Onay)> adaylar, DateTime yukleme, DateTime? sonraki)
    {
        if (adaylar.Any(a => a.Onay is null)) return (null, true);
        if (sonraki is { } s && s - yukleme < YakinSurumPayi) return (null, true);
        var ust = sonraki is { } s2 && s2 < yukleme + EnUzunOnaySuresi ? s2 : yukleme + EnUzunOnaySuresi;
        // GİB listesindeki yükleme dakikası ile PDF'teki onay saniyesi arasında küçük saat farkı olabilir → 1 dk alt pay
        var es = adaylar.Where(a => a.Onay >= yukleme.AddMinutes(-1) && a.Onay < ust).OrderBy(a => a.Onay).FirstOrDefault();
        return (es.Ad, false);
    }

    /// <summary>"..._BEYANNAME_X" → "..._TAHAKKUK_X" (arşivdeki eşin tahakkuk/ihbarname karşılığı).</summary>
    static string Karsilik(string beyannameAdi, string onekBeyanname, string onekHedef) =>
        onekHedef + beyannameAdi[onekBeyanname.Length..];

    /// <summary>
    /// Beyanname + tahakkuk. KDV türlerinde (Ayarlar.OnayZamaniTurleri) dosya adındaki zaman,
    /// beyanname PDF'inin 1. sayfasındaki "Onay Zamanı"dır (okunamazsa yükleme zamanı).
    /// Arşivde aynı önekli dosya varsa sürüm PDF içeriğiyle (onay zamanı) tanınır: ofisin elle verdiği adlar
    /// (_DÜZ, _DÜZ2, damgasız, XML adlı) GİB'deki sıra numarasıyla örtüşmeyebilir (2026-11-18'de 8 kopya buradan çıktı).
    /// </summary>
    async Task<SurumDurumu> BeyannameEvraklariAsync(BeyannameSatiri b, Musteri m, int sira, DateTime? sonrakiYukleme,
        Action<Seviye, string, string?> bildir, CancellationToken ct)
    {
        var klasor = ArsivDenetci.BeyannameKlasoru(ayarlar.YArsivKok, m, b);
        var bOnek = DosyaAdlari.BeyannameOnek(b, EvrakTuru.Beyanname);
        var tOnek = DosyaAdlari.BeyannameOnek(b, EvrakTuru.Tahakkuk);
        bool onayli = ayarlar.OnayZamaniKullanilir(b.Tur);
        bool tamAd = File.Exists(Path.Combine(klasor, DosyaAdlari.BeyannameDosyasi(b, EvrakTuru.Beyanname, sira)));
        var adaylar = ArsivDenetci.Dosyalar(klasor, bOnek).ToList();

        DateTime zaman = b.YuklemeZamani;
        byte[]? bynPdf = null;
        string? eslesen = null;
        bool icerikleYok = false;
        bool onayOkundu = false;

        // 1) GİB'e gitmeden: arşivdeki PDF'lerin onay zamanı bu sürümün aralığına düşüyor mu? (§9.7)
        bool gibGerekli = !tamAd && (adaylar.Count > 0 || onayli);
        if (!tamAd && adaylar.Count > 0)
        {
            var okunan = adaylar.Select(a => (Ad: a, Onay: ArsivOnay(Path.Combine(klasor, a + ".pdf")))).ToList();
            var (es, belirsiz) = AraliktaEslesen(okunan, b.YuklemeZamani, sonrakiYukleme);
            if (!belirsiz && es is not null)
            {
                eslesen = es;
                onayOkundu = true;
                if (onayli && okunan.First(x => x.Ad == es).Onay is { } esOnay) zaman = esOnay;
                gibGerekli = false;                                  // sürüm var; GİB'den PDF istenmez
            }
            else if (!belirsiz)
            {
                icerikleYok = true;                                  // sürüm eksik; PDF zaten indirilecek
                gibGerekli = true;
            }
        }

        // 2) Belirsizse ya da indirilecekse GİB'deki beyanname PDF'i alınır (onay zamanı + kesin karşılaştırma)
        if (gibGerekli)
        {
            bynPdf = await istemci.PdfAsync(EbynKomutlari.BeyannamePdf(b.Oid), ct);
            if (BeyannamePdfOkuyucu.OnayZamani(bynPdf) is { } oz)
            {
                onayOkundu = true;
                if (onayli) zaman = oz;
                var okunan = adaylar.Select(a => (Ad: a, Onay: ArsivOnay(Path.Combine(klasor, a + ".pdf")))).ToList();
                eslesen = okunan.FirstOrDefault(x => x.Onay == oz).Ad;
                icerikleYok = eslesen is null && okunan.All(x => x.Onay is not null);
            }
            else if (onayli)
                bildir(Seviye.Uyari, $"{m.KisaAd} {b.Tur} {b.Donem}: beyanname PDF'inde onay zamanı okunamadı, yükleme zamanı kullanıldı", null);
        }

        var bAd = DosyaAdlari.BeyannameDosyasi(b, EvrakTuru.Beyanname, sira, zaman: zaman);
        var tAd = DosyaAdlari.BeyannameDosyasi(b, EvrakTuru.Tahakkuk, sira, zaman: zaman);
        bool? bVar, tVar;
        if (tamAd) { bVar = true; tVar = null; }                       // eski davranış (tahakkuk da kendi tam adıyla/sayımla)
        else if (eslesen is not null)
        {
            bVar = true;
            // Eşin tahakkuku: aynı adın TAHAKKUK karşılığı; yoksa ve tahakkuk sayısı beyanname sayısına yetiyorsa var say
            var tKarsilik = Karsilik(eslesen, bOnek, tOnek);
            tVar = File.Exists(Path.Combine(klasor, tKarsilik + ".pdf")) || File.Exists(Path.Combine(klasor, tAd))
                   || ArsivDenetci.EskiDosyaSayisi(klasor, tOnek) >= adaylar.Count;
            if (tVar == false) tAd = tKarsilik + ".pdf";             // eksik tahakkuk, arşivdeki beyannamesiyle aynı adla
            bildir(Seviye.Atlandi, $"zaten var (içerikten): {eslesen}.pdf", Path.Combine(klasor, eslesen + ".pdf"));
        }
        else if (icerikleYok || (onayOkundu && adaylar.Count == 0))
        {
            bVar = File.Exists(Path.Combine(klasor, bAd));
            tVar = File.Exists(Path.Combine(klasor, tAd));
        }
        else { bVar = null; tVar = null; }                            // içerik okunamadı → önek sayımı

        if (bVar != true || eslesen is null)
            await TekEvrakAsync(klasor, bOnek, bAd, sira,
                () => bynPdf is not null ? Task.FromResult(bynPdf) : istemci.PdfAsync(EbynKomutlari.BeyannamePdf(b.Oid), ct), bildir, bVar);

        if (b.TahakkukOid is { } t)
            await TekEvrakAsync(klasor, tOnek, tAd, sira, () => istemci.PdfAsync(EbynKomutlari.TahakkukPdf(b.Oid, t), ct), bildir, tVar);
        return new SurumDurumu(zaman, eslesen, icerikleYok);
    }

    async Task IhbarnamelerAsync(BeyannameSatiri b, Musteri m, int sira, SurumDurumu surum, Action<Seviye, string, string?> bildir, CancellationToken ct)
    {
        var klasor = ArsivDenetci.BeyannameKlasoru(ayarlar.YArsivKok, m, b);
        var anahtar = TamamlananDeposu.Anahtar(ayarlar, b.Oid, "IHB");
        if (_tamamlanan.Var(anahtar)) return;
        var onek1 = DosyaAdlari.BeyannameOnek(b, EvrakTuru.Ihbarname, 1);
        var ad1 = DosyaAdlari.BeyannameDosyasi(b, EvrakTuru.Ihbarname, sira, 1, surum.Zaman);
        bool? ilkVar = null;
        if (surum.Eslesen is not null)
        {
            // Sürüm arşivde başka adla var → ihbarnamesi de o adın karşılığıyla aranır; yoksa sayıma bakılır
            var karsilik = Karsilik(surum.Eslesen, DosyaAdlari.BeyannameOnek(b, EvrakTuru.Beyanname), onek1);
            ilkVar = File.Exists(Path.Combine(klasor, karsilik + ".pdf")) || File.Exists(Path.Combine(klasor, ad1))
                     || ArsivDenetci.EskiDosyaSayisi(klasor, onek1) > 0;
        }
        else if (surum.IcerikleYok) ilkVar = File.Exists(Path.Combine(klasor, ad1));
        // İlk ihbarname zaten varsa listeyi sorgulamaya gerek yok
        if (ilkVar ?? ArsivDenetci.VarMi(klasor, onek1, ad1, sira)) { _tamamlanan.Ekle(anahtar); return; }

        var liste = await istemci.IhbarnamelerAsync(b.Oid, ct);
        for (int i = 0; i < liste.Count; i++)
        {
            var ih = liste[i];
            var ad = DosyaAdlari.BeyannameDosyasi(b, EvrakTuru.Ihbarname, sira, i + 1, surum.Zaman);
            await TekEvrakAsync(klasor, DosyaAdlari.BeyannameOnek(b, EvrakTuru.Ihbarname, i + 1), ad, sira,
                () => istemci.PdfAsync(EbynKomutlari.IhbarnamePdf(ih), ct), bildir,
                surum.IcerikleYok || surum.Eslesen is not null ? File.Exists(Path.Combine(klasor, ad)) : null);
        }
        // Buraya hatasız gelindiyse tüm ihbarnameler arşivde (hata olsaydı istisna yukarı çıkardı)
        _tamamlanan.Ekle(anahtar);
    }

    async Task SgkAsync(BeyannameSatiri b, Musteri m, Action<Seviye, string, string?> bildir, CancellationToken ct)
    {
        var anahtar = TamamlananDeposu.Anahtar(ayarlar, b.Oid, "SGK");
        if (_tamamlanan.Var(anahtar)) return;

        var bildirimler = await istemci.SgkBildirimleriAsync(b.Oid, ct);
        var sayac = new Dictionary<string, int>();
        bool tam = true; // PDF'siz (işlemde) bildirim ya da belirlenemeyen işyeri varsa sonraki çalıştırmada yeniden sorgulanır
        foreach (var s in bildirimler)
        {
            if (!s.PdfVar)
            {
                // MUHSGK'nın vergi kısmı da bu listede "Vergi" satırı olarak gelir; SGK PDF'i hiç olmaz → uyarı gereksiz.
                if (s.Aciklama.Trim().StartsWith("Vergi", StringComparison.OrdinalIgnoreCase)) continue;
                tam = false;
                bildir(Seviye.Uyari, $"{m.KisaAd} {b.Donem}: SGK bildirimi PDF'siz (hatalı/işlemde) — {s.Aciklama}", null);
                continue;
            }
            var (bt0, kn0, mh0) = DosyaAdlari.SgkAdParcalari(s.Aciklama);
            var grup = $"{bt0}_{kn0}_{mh0[..2]}";
            sayac[grup] = sayac.GetValueOrDefault(grup) + 1;
            var (bt, kn, mh) = DosyaAdlari.SgkAdParcalari(s.Aciklama, sayac[grup]);

            // Bilinen işyerlerinden birinde zaten varsa indirme
            var tOnek = DosyaAdlari.SgkOnek(b.Donem, bt, kn, mh, EvrakTuru.SgkTahakkuk);
            var hOnek = DosyaAdlari.SgkOnek(b.Donem, bt, kn, mh, EvrakTuru.SgkHizmetListesi);
            var bilinen = isyerleri.MusteriIsyerleri(m.XKlasor).Select(k => k.IsyeriKlasor).Append("MERKEZ").Distinct().ToList();
            bool HerYerdeVar(string onek) => bilinen.Any(i => ArsivDenetci.EskiDosyaSayisi(ArsivDenetci.SgkKlasoru(ayarlar.XArsivKok, m, b.Donem, i), onek) > 0);
            if (HerYerdeVar(tOnek) && HerYerdeVar(hOnek))
            {
                bildir(Seviye.Atlandi, $"{m.KisaAd} {b.Donem} SGK {bt}/{kn}: zaten var", null);
                continue;
            }

            var thkPdf = await istemci.PdfAsync(EbynKomutlari.SgkTahakkukPdf(s), ct);
            var (sicil, pdfBt) = SgkPdfOkuyucu.Oku(thkPdf);
            if (pdfBt != bt)
            {
                // Belge türü açıklamadan güvenilir çıkmıyor (SGDP = 02); tahakkuk PDF'i esas (§9.4). Açıklama ileride kural için loglanır.
                bildir(Seviye.Bilgi, $"{m.KisaAd} {b.Donem} SGK: belge türü PDF'ten {pdfBt} (açıklamadan {bt}) — {s.Aciklama}", null);
                bt = pdfBt;
                tOnek = DosyaAdlari.SgkOnek(b.Donem, bt, kn, mh, EvrakTuru.SgkTahakkuk);
                hOnek = DosyaAdlari.SgkOnek(b.Donem, bt, kn, mh, EvrakTuru.SgkHizmetListesi);
                if (HerYerdeVar(tOnek) && HerYerdeVar(hOnek))
                {
                    bildir(Seviye.Atlandi, $"{m.KisaAd} {b.Donem} SGK {bt}/{kn}: zaten var", null);
                    continue;
                }
            }
            var isyeri = sicil is null ? null : await IsyeriBulAsync(m, sicil);
            if (isyeri is null)
            {
                tam = false;
                bildir(Seviye.Uyari, $"{m.KisaAd} {b.Donem}: SGK işyeri belirlenemedi (sicil {sicil ?? "okunamadı"}), atlandı", null);
                continue;
            }

            var klasor = ArsivDenetci.SgkKlasoru(ayarlar.XArsivKok, m, b.Donem, isyeri);
            await TekEvrakAsync(klasor, tOnek, DosyaAdlari.SgkDosyasi(b.Donem, bt, kn, mh, EvrakTuru.SgkTahakkuk, b.YuklemeZamani), 0,
                () => Task.FromResult(thkPdf), bildir);
            await TekEvrakAsync(klasor, hOnek, DosyaAdlari.SgkDosyasi(b.Donem, bt, kn, mh, EvrakTuru.SgkHizmetListesi, b.YuklemeZamani), 0,
                () => istemci.PdfAsync(EbynKomutlari.SgkHizmetPdf(s), ct), bildir);
        }
        if (tam) _tamamlanan.Ekle(anahtar);
    }

    async Task<string?> IsyeriBulAsync(Musteri m, string sicil)
    {
        if (isyerleri.Bul(sicil) is { } k) return k.IsyeriKlasor;

        var bilinen = isyerleri.MusteriIsyerleri(m.XKlasor).Select(x => x.IsyeriKlasor).Distinct().ToList();
        string? secim = bilinen.Count == 0 ? "MERKEZ" : await isyeriSor(new IsyeriSorusu(m, sicil, bilinen));
        if (string.IsNullOrWhiteSpace(secim)) return null;

        isyerleri.Ekle(new IsyeriDeposu.Kayit(m.XKlasor, secim, sicil, bilinen.Count == 0 ? "varsayılan" : "kullanıcı"));
        isyerleriDegisti?.Invoke();
        return secim;
    }

    /// <param name="varMi">Verilirse "arşivde var mı" kararı (içerikten); verilmezse tam ad / önek sayımı (ArsivDenetci.VarMi).</param>
    static async Task TekEvrakAsync(string klasor, string onek, string ad, int sira, Func<Task<byte[]>> indir, Action<Seviye, string, string?> bildir,
        bool? varMi = null)
    {
        var hedef = Path.Combine(klasor, ad);
        if (varMi ?? ArsivDenetci.VarMi(klasor, onek, ad, sira))
        {
            // KDV'de onay zamanı PDF indirilmeden bilinmez → ad yükleme zamanlı olabilir; o zaman yalnız öneki göster
            bildir(Seviye.Atlandi, File.Exists(hedef) ? $"zaten var: {ad}" : $"zaten var: {onek}_… (arşivdeki adla)", hedef);
            return;
        }
        var veri = await indir();
        Directory.CreateDirectory(klasor);
        var gecici = hedef + ".part";
        await File.WriteAllBytesAsync(gecici, veri);
        try
        {
            File.Move(gecici, hedef, overwrite: false);
            bildir(Seviye.Indirildi, ad, hedef);
        }
        catch (IOException) when (File.Exists(hedef))
        {
            File.Delete(gecici);
            bildir(Seviye.Atlandi, $"zaten var: {ad}", hedef);
        }
    }
}
