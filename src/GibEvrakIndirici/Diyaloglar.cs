using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Indirme;

namespace GibEvrakIndirici;

static class Diyaloglar
{
    /// <summary>Bilinmeyen SGK sicili için işyeri klasörü sorar (bir kez; cevap hatırlanır).</summary>
    public static string? IsyeriSor(IWin32Window sahip, IsyeriSorusu s)
    {
        using var f = new Form
        {
            Text = "SGK işyeri klasörü", Width = 520, Height = 220, FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
        };
        var etiket = new Label
        {
            Dock = DockStyle.Top, Height = 80, Padding = new Padding(10),
            Text = $"{s.Musteri.KisaAd} için yeni bir SGK işyeri sicili bulundu:\n{s.Sicil}\n\nBu işyerinin X:\\ klasör adını seçin veya yazın (Luca'daki işyeri kısa adı):",
        };
        var kutu = new ComboBox { Left = 10, Top = 90, Width = 480, DropDownStyle = ComboBoxStyle.DropDown };
        kutu.Items.AddRange(s.BilinenIsyerleri.ToArray<object>());
        var tamam = new Button { Text = "Kaydet", DialogResult = DialogResult.OK, Left = 300, Top = 130, Width = 90 };
        var iptal = new Button { Text = "Atla", DialogResult = DialogResult.Cancel, Left = 400, Top = 130, Width = 90 };
        f.Controls.AddRange([kutu, tamam, iptal, etiket]);
        f.AcceptButton = tamam; f.CancelButton = iptal;
        return f.ShowDialog(sahip) == DialogResult.OK && !string.IsNullOrWhiteSpace(kutu.Text)
            ? kutu.Text.Trim().Replace(' ', '_') : null;
    }

    public static bool AyarlariDuzenle(IWin32Window sahip, Ayarlar a)
    {
        using var f = new Form { Text = "Ayarlar", Width = 560, Height = 420, StartPosition = FormStartPosition.CenterParent };
        var pg = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = a, HelpVisible = true, ToolbarVisible = false };
        var alt = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
        var tamam = new Button { Text = "Kaydet", DialogResult = DialogResult.OK, Width = 90 };
        var iptal = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Width = 90 };
        alt.Controls.AddRange([iptal, tamam]);
        f.Controls.AddRange([pg, alt]);
        f.AcceptButton = tamam; f.CancelButton = iptal;
        return f.ShowDialog(sahip) == DialogResult.OK;
    }
}
