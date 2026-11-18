namespace GibEvrakIndirici.Core.Indirme;

/// <summary>Seçili müşteriler × türler × vergilendirme dönemi aralığı için arşiv taraması (§9.5).</summary>
public sealed record MusteriTaramasiIstegi(IReadOnlyList<Musteri> Musteriler, Donem Aralik, IReadOnlyList<string> Turler);

public static class MusteriTaramasi
{
    /// <summary>Her zaman önerilen türler.</summary>
    public static readonly string[] Ortak = ["KDV1", "MUHSGK", "GGECICI", "KGECICI"];

    /// <summary>Seçim listesinde gösterilen bilinen e-Beyanname türleri (§8.1).</summary>
    public static readonly string[] Bilinen =
        ["KDV1", "KDV2", "KDV4", "MUHSGK", "GGECICI", "KGECICI", "GELIR", "KURUMLAR", "POSET", "KONAKLAMA", "FORMBA", "FORMBS", "MTV", "DAMGAKK"];

    /// <summary>Varsayılan türler: ortak türler + müşterilerin Y arşivinde aralıktaki yıllarda klasörü olan türler.</summary>
    public static List<string> VarsayilanTurler(IEnumerable<Musteri> musteriler, Ayarlar ayarlar, Donem aralik)
    {
        var turler = new HashSet<string>(Ortak, StringComparer.OrdinalIgnoreCase);
        foreach (var m in musteriler)
            for (int yil = aralik.BasYil; yil <= aralik.BitYil; yil++)
            {
                var k = Path.Combine(ayarlar.YArsivKok, m.YKlasor, yil.ToString());
                if (!Directory.Exists(k)) continue;
                foreach (var d in Directory.EnumerateDirectories(k))
                {
                    var ad = Path.GetFileName(d).Trim().ToUpperInvariant();
                    if (ad.Length is >= 3 and <= 12 && ad.All(char.IsAsciiLetterOrDigit)) turler.Add(ad);
                }
            }
        return Bilinen.Where(turler.Contains)
            .Concat(turler.Where(t => !Bilinen.Contains(t, StringComparer.OrdinalIgnoreCase)).Order())
            .ToList();
    }

    /// <summary>e-Beyanname'de şahıslar TCKN ile görünür (§8.3) → TCKN varsa o, yoksa VKN.</summary>
    public static string? Kimlik(Musteri m) =>
        !string.IsNullOrWhiteSpace(m.Tckn) ? m.Tckn.Trim() : !string.IsNullOrWhiteSpace(m.VergiNo) ? m.VergiNo.Trim() : null;

    public static string Aciklama(MusteriTaramasiIstegi i) =>
        $"{i.Musteriler.Count} müşteri, {i.Aralik.BasAy:00}/{i.Aralik.BasYil}–{i.Aralik.BitAy:00}/{i.Aralik.BitYil}, türler: {string.Join(", ", i.Turler)}";
}
