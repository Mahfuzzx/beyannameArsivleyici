using System.Text;

namespace GibEvrakIndirici.Core.Adlandirma;

/// <summary>
/// Luca "Kısa Ad"ından arşiv klasör adı üretir (bkz. GIB_AKIS_NOTLARI.md §8.3).
/// X:\ (SGK): boşluk → '_' (Türkçe karakter korunur)      "ÖR TEMİZLİ" → "ÖR_TEMİZLİ"
/// Y:\ (Beyanname): boşluk → '_', Ş→S Ğ→G Ü→U Ö→O Ç→C, İ→'_'  "ÖR TEMİZLİ" → "OR_TEM_ZL_"
/// </summary>
public static class KlasorAdi
{
    public static string XIcin(string kisaAd) => kisaAd.Trim().Replace(' ', '_');

    public static string YIcin(string kisaAd)
    {
        var sb = new StringBuilder(kisaAd.Length);
        foreach (var c in kisaAd.Trim())
        {
            sb.Append(c switch
            {
                ' ' => '_',
                'Ş' => 'S', 'ş' => 's',
                'Ğ' => 'G', 'ğ' => 'g',
                'Ü' => 'U', 'ü' => 'u',
                'Ö' => 'O', 'ö' => 'o',
                'Ç' => 'C', 'ç' => 'c',
                'İ' => '_',
                'ı' => 'i',
                _ => c
            });
        }
        return sb.ToString();
    }
}
