using System.Drawing.Printing;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;

namespace GibEvrakIndirici;

/// <summary>
/// PDF'leri tek bir yazdırma işi olarak yazdırır (§9.9). Sayfalar Windows'un yerleşik PDF bileşeniyle (Windows.Data.Pdf)
/// resme çizilir; bilgisayardaki varsayılan PDF okuyucusuna bağlı değildir. Sayfalar sırayla, gerektiğinde çizilir (bellek şişmez).
/// Yatay sayfalar kâğıda sığması için döndürülür.
/// </summary>
static class PdfYazdirici
{
    const int Dpi = 200;

    /// <param name="ilerleme">(yazdırılan dosya sırası, toplam dosya, dosya adı)</param>
    /// <param name="hata">Açılamayan dosya (atlanır, iş sürer).</param>
    public static int Yazdir(IReadOnlyList<string> dosyalar, PrinterSettings yazici, string isAdi,
        Action<int, int, string>? ilerleme, Action<string, Exception>? hata, CancellationToken ct)
    {
        int dosyaNo = -1, sayfaNo = 0, basilanSayfa = 0;
        WinPdf.PdfDocument? acik = null;

        bool SonrakiDosyayiAc()
        {
            while (++dosyaNo < dosyalar.Count)
            {
                if (ct.IsCancellationRequested) return false;
                ilerleme?.Invoke(dosyaNo, dosyalar.Count, Path.GetFileName(dosyalar[dosyaNo]));
                try
                {
                    acik = Ac(dosyalar[dosyaNo]);
                    sayfaNo = 0;
                    if (acik.PageCount > 0) return true;
                }
                catch (Exception ex) { hata?.Invoke(dosyalar[dosyaNo], ex); }
            }
            return false;
        }

        using var belge = new PrintDocument
        {
            PrinterSettings = yazici,
            DocumentName = isAdi,
            PrintController = new StandardPrintController(), // arka planda, "yazdırılıyor" penceresi olmadan
            OriginAtMargins = false,
        };
        belge.DefaultPageSettings.Margins = new Margins(20, 20, 20, 20);
        bool ilk = true;
        belge.PrintPage += (_, e) =>
        {
            if (ilk) { ilk = false; if (!SonrakiDosyayiAc()) { e.Cancel = true; return; } }
            using (var resim = Ciz(acik!, sayfaNo))
                SayfayaSigdir(e, resim);
            basilanSayfa++;
            sayfaNo++;
            e.HasMorePages = !ct.IsCancellationRequested && (sayfaNo < acik!.PageCount || SonrakiDosyayiAc());
        };
        belge.Print();
        ilerleme?.Invoke(dosyalar.Count, dosyalar.Count, "");
        return basilanSayfa;
    }

    static WinPdf.PdfDocument Ac(string yol)
    {
        // StorageFile yerine bellekten: ağ sürücüsündeki (Y:\) dosyalarda yetki/yol sorunu çıkmasın
        var bayt = File.ReadAllBytes(yol);
        var akis = new InMemoryRandomAccessStream();
        using (var w = new DataWriter(akis.GetOutputStreamAt(0)))
        {
            w.WriteBytes(bayt);
            w.StoreAsync().AsTask().GetAwaiter().GetResult();
            w.FlushAsync().AsTask().GetAwaiter().GetResult();
            w.DetachStream();
        }
        return WinPdf.PdfDocument.LoadFromStreamAsync(akis).AsTask().GetAwaiter().GetResult();
    }

    static Bitmap Ciz(WinPdf.PdfDocument pdf, int sayfa)
    {
        using var s = pdf.GetPage((uint)sayfa);
        var secenek = new WinPdf.PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, s.Size.Width * Dpi / 96.0),
            DestinationHeight = (uint)Math.Max(1, s.Size.Height * Dpi / 96.0),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
        };
        using var akis = new InMemoryRandomAccessStream();
        s.RenderToStreamAsync(akis, secenek).AsTask().GetAwaiter().GetResult();
        using var net = akis.AsStreamForRead();
        using var gecici = new Bitmap(net);
        return new Bitmap(gecici); // akıştan bağımsız kopya
    }

    static void SayfayaSigdir(PrintPageEventArgs e, Bitmap resim)
    {
        var alan = e.MarginBounds;
        bool alanDikey = alan.Height >= alan.Width;
        bool resimDikey = resim.Height >= resim.Width;
        if (alanDikey != resimDikey) resim.RotateFlip(RotateFlipType.Rotate90FlipNone);
        double olcek = Math.Min((double)alan.Width / resim.Width, (double)alan.Height / resim.Height);
        int w = (int)(resim.Width * olcek), h = (int)(resim.Height * olcek);
        var hedef = new Rectangle(alan.X + (alan.Width - w) / 2, alan.Y + (alan.Height - h) / 2, w, h);
        e.Graphics!.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(resim, hedef);
    }
}
