namespace GibEvrakIndirici.Core;

/// <summary>Vergilendirme dönemi (ör. 07/2026-09/2026).</summary>
public readonly record struct Donem(int BasYil, int BasAy, int BitYil, int BitAy)
{
    public int AySayisi => (BitYil - BasYil) * 12 + (BitAy - BasAy) + 1;

    /// <summary>"09/2026-09/2026" biçimini ayrıştırır.</summary>
    public static Donem Ayristir(string metin)
    {
        var p = metin.Trim().Split('-', StringSplitOptions.TrimEntries);
        var (by, ba) = AyYil(p[0]);
        var (ty, ta) = p.Length > 1 ? AyYil(p[1]) : (by, ba);
        return new Donem(by, ba, ty, ta);

        static (int yil, int ay) AyYil(string s)
        {
            var q = s.Split('/');
            return (int.Parse(q[1]), int.Parse(q[0]));
        }
    }

    public override string ToString() => $"{BasAy:00}/{BasYil}-{BitAy:00}/{BitYil}";
}

/// <summary>e-Beyanname "Beyanname Ara" listesindeki bir satır.</summary>
public sealed record BeyannameSatiri
{
    public required string Oid { get; init; }              // URL-encode edilmiş haliyle (aynen geri gönderilir)
    public required string Tur { get; init; }              // KDV1, MUHSGK, GGECICI ...
    public required string VknTckn { get; init; }
    public required string Unvan { get; init; }
    public string VergiDairesi { get; init; } = "";
    public required Donem Donem { get; init; }
    public string Sube { get; init; } = "";
    public required DateTime YuklemeZamani { get; init; }
    public string Durum { get; init; } = "";
    public string? TahakkukOid { get; init; }
    public bool IhbarnameVar { get; init; }
    public bool SgkBildirimVar { get; init; }
}

/// <summary>Bir beyannameye bağlı vergi ihbarnamesi.</summary>
public sealed record IhbarnameBilgisi(string BeyannameOid, string TahakkukOid, string TarhOid, string Aciklama);

/// <summary>MUHSGK beyannamesinin SGK bildirimlerinden biri.</summary>
public sealed record SgkBildirimi
{
    public required string BeyannameOid { get; init; }
    public required string SgkTahakkukOid { get; init; }
    public required string Aciklama { get; init; }   // "Asıl, TÜM SİG.KOLLARI/..., 05510-Say.Kan..., 09 / 2026, ..."
    public string Donem { get; init; } = "";
    public bool PdfVar { get; init; }                // hatalı bildirimlerde PDF yok
}

/// <summary>Luca müşteri listesinden bir müşteri.</summary>
public sealed record Musteri
{
    public required string KisaAd { get; init; }
    public string UzunAd { get; init; } = "";
    public string VergiDairesi { get; init; } = "";
    public string VergiNo { get; init; } = "";
    public string Tckn { get; init; } = "";
    /// <summary>Elle verilmiş X:\ klasör adı (boşsa kuraldan türetilir).</summary>
    public string? XKlasorOzel { get; init; }
    /// <summary>Elle verilmiş Y:\ klasör adı (boşsa kuraldan türetilir).</summary>
    public string? YKlasorOzel { get; init; }

    /// <summary>
    /// Klasör adı türetilirken kısa addan atılacak birim öneki (ör. "0 "; diğer birimin kurulumunda). Kaydedilmez,
    /// <see cref="Musteriler.MusteriDeposu.KlasorOnekiniAt"/> ile verilir.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string AtilanOnek { get; init; } = "";

    /// <summary>Klasör adının türetildiği kısa ad (birim öneki atılmış).</summary>
    public string KlasorKisaAdi => AtilanOnek.Length > 0 && KisaAd.StartsWith(AtilanOnek, StringComparison.Ordinal) && KisaAd.Length > AtilanOnek.Length
        ? KisaAd[AtilanOnek.Length..].Trim()
        : KisaAd;

    public string XKlasor => string.IsNullOrWhiteSpace(XKlasorOzel) ? Adlandirma.KlasorAdi.XIcin(KlasorKisaAdi) : XKlasorOzel!;
    public string YKlasor => string.IsNullOrWhiteSpace(YKlasorOzel) ? Adlandirma.KlasorAdi.YIcin(KlasorKisaAdi) : YKlasorOzel!;
}

/// <summary>Luca'daki bir SGK işyeri.</summary>
public sealed record Isyeri
{
    public required string FirmaKisaAd { get; init; }
    public required string KisaAd { get; init; }      // X:\ işyeri klasörü (MERKEZ, KARTAL ...)
    public string UzunAd { get; init; } = "";
    public required string Sicil { get; init; }       // 26 hane
    public DateTime? Baslama { get; init; }
    public DateTime? Bitis { get; init; }
}

public enum EvrakTuru { Beyanname, Tahakkuk, Ihbarname, SgkTahakkuk, SgkHizmetListesi }
