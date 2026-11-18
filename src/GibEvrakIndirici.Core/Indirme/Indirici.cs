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
    TamamlananDeposu? tamamlanan = null)
{
    readonly Func<TimeSpan, CancellationToken, Task> _bekle = bekle ?? Task.Delay;
    readonly TamamlananDeposu _tamamlanan = tamamlanan ?? new();

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

    public async Task<IndirmeOzeti> CalistirAsync(IReadOnlyList<BeyannameSatiri> satirlar, IProgress<IndirmeOlayi> ilerleme, CancellationToken ct)
    {
        var ozet = new IndirmeOzeti();
        var siralar = DuzeltmeSiralayici.Sirala(satirlar);

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
            var m = musteriler.Bul(b.VknTckn);
            if (m is null)
            {
                Bildir(Seviye.Uyari, $"{b.VknTckn} {b.Unvan}: müşteri listesinde yok, atlandı");
                continue;
            }
            if (!ayarlar.MusteriDahil(m)) { ozet.FiltreDisi++; continue; } // diğer birimin müşterisi: GİB'e istek yok
            int sira = EtkinSira(b, m, siralar.GetValueOrDefault(b.Oid));
            for (int kisitlama = 0; ; kisitlama++)
            {
                try
                {
                    // Var olan dosyalar atlandığı için aynı satırı yeniden denemek güvenlidir.
                    var zaman = await BeyannameEvraklariAsync(b, m, sira, Bildir, ct);
                    if (b.IhbarnameVar) await IhbarnamelerAsync(b, m, sira, zaman, Bildir, ct);
                    if (b.SgkBildirimVar) await SgkAsync(b, m, Bildir, ct);
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
        return ozet;
    }

    /// <summary>
    /// Beyanname + tahakkuk. KDV türlerinde (Ayarlar.OnayZamaniTurleri) dosya adındaki zaman,
    /// beyanname PDF'inin 1. sayfasındaki "Onay Zamanı"dır (okunamazsa yükleme zamanı). Dönen değer = kullanılan zaman.
    /// </summary>
    async Task<DateTime> BeyannameEvraklariAsync(BeyannameSatiri b, Musteri m, int sira, Action<Seviye, string, string?> bildir, CancellationToken ct)
    {
        var klasor = ArsivDenetci.BeyannameKlasoru(ayarlar.YArsivKok, m, b);
        var bOnek = DosyaAdlari.BeyannameOnek(b, EvrakTuru.Beyanname);
        var tOnek = DosyaAdlari.BeyannameOnek(b, EvrakTuru.Tahakkuk);
        bool bVar = ArsivDenetci.VarMi(klasor, bOnek, DosyaAdlari.BeyannameDosyasi(b, EvrakTuru.Beyanname, sira), sira);
        bool tVar = b.TahakkukOid is null || ArsivDenetci.VarMi(klasor, tOnek, DosyaAdlari.BeyannameDosyasi(b, EvrakTuru.Tahakkuk, sira), sira);

        DateTime zaman = b.YuklemeZamani;
        byte[]? bynPdf = null;
        bool onayli = ayarlar.OnayZamaniKullanilir(b.Tur);

        // Onay zamanı gerekiyorsa ve bir şey indirilecekse beyanname PDF'i önce alınır (tahakkuk adı da ona bağlı)
        if (onayli && !(bVar && tVar))
        {
            bynPdf = await istemci.PdfAsync(EbynKomutlari.BeyannamePdf(b.Oid), ct);
            if (BeyannamePdfOkuyucu.OnayZamani(bynPdf) is { } oz) zaman = oz;
            else bildir(Seviye.Uyari, $"{m.KisaAd} {b.Tur} {b.Donem}: beyanname PDF'inde onay zamanı okunamadı, yükleme zamanı kullanıldı", null);
        }

        await TekEvrakAsync(klasor, bOnek, DosyaAdlari.BeyannameDosyasi(b, EvrakTuru.Beyanname, sira, zaman: zaman), sira,
            () => bynPdf is not null ? Task.FromResult(bynPdf) : istemci.PdfAsync(EbynKomutlari.BeyannamePdf(b.Oid), ct), bildir);

        if (b.TahakkukOid is { } t)
            await TekEvrakAsync(klasor, tOnek, DosyaAdlari.BeyannameDosyasi(b, EvrakTuru.Tahakkuk, sira, zaman: zaman), sira,
                () => istemci.PdfAsync(EbynKomutlari.TahakkukPdf(b.Oid, t), ct), bildir);
        return zaman;
    }

    async Task IhbarnamelerAsync(BeyannameSatiri b, Musteri m, int sira, DateTime zaman, Action<Seviye, string, string?> bildir, CancellationToken ct)
    {
        var klasor = ArsivDenetci.BeyannameKlasoru(ayarlar.YArsivKok, m, b);
        var anahtar = TamamlananDeposu.Anahtar(ayarlar, b.Oid, "IHB");
        if (_tamamlanan.Var(anahtar)) return;
        // İlk ihbarname zaten varsa listeyi sorgulamaya gerek yok
        if (ArsivDenetci.VarMi(klasor, DosyaAdlari.BeyannameOnek(b, EvrakTuru.Ihbarname, 1),
                DosyaAdlari.BeyannameDosyasi(b, EvrakTuru.Ihbarname, sira, 1, zaman), sira))
            return;

        var liste = await istemci.IhbarnamelerAsync(b.Oid, ct);
        for (int i = 0; i < liste.Count; i++)
        {
            var ih = liste[i];
            await TekEvrakAsync(klasor, DosyaAdlari.BeyannameOnek(b, EvrakTuru.Ihbarname, i + 1),
                DosyaAdlari.BeyannameDosyasi(b, EvrakTuru.Ihbarname, sira, i + 1, zaman), sira,
                () => istemci.PdfAsync(EbynKomutlari.IhbarnamePdf(ih), ct), bildir);
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

    static async Task TekEvrakAsync(string klasor, string onek, string ad, int sira, Func<Task<byte[]>> indir, Action<Seviye, string, string?> bildir)
    {
        var hedef = Path.Combine(klasor, ad);
        if (ArsivDenetci.VarMi(klasor, onek, ad, sira))
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
