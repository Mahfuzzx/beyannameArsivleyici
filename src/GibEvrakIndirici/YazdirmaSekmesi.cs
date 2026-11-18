using System.Diagnostics;
using System.Drawing.Printing;
using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Arsiv;
using GibEvrakIndirici.Core.Indirme;

namespace GibEvrakIndirici;

/// <summary>
/// "Toplu yazdırma" sekmesi (§9.9): tür + dönem için filtredeki tüm müşterilerin arşivdeki beyanname / tahakkuk / ihbarname
/// PDF'lerini listeler; seçilenler tek yazdırma işi olarak yazdırılır (<see cref="Yazdir"/> olayı, işi AnaForm yürütür).
/// GİB'e hiç istek atılmaz; yalnız Y arşivi okunur.
/// </summary>
sealed class YazdirmaSekmesi : TabPage
{
    readonly Func<IReadOnlyList<Musteri>> _musteriler;
    readonly Func<Ayarlar> _ayarlar;
    readonly ComboBox _tur = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDown };
    readonly ComboBox _tip = new() { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly DateTimePicker _ay = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "MM / yyyy", ShowUpDown = true, Width = 95 };
    readonly CheckBox _byn = new() { Text = "Beyanname", AutoSize = true };
    readonly CheckBox _thk = new() { Text = "Tahakkuk", AutoSize = true, Checked = true };
    readonly CheckBox _ihb = new() { Text = "İhbarname", AutoSize = true };
    readonly CheckBox _sgkT = new() { Text = "SGK tahakkuk", AutoSize = true };
    readonly CheckBox _sgkH = new() { Text = "SGK hizmet listesi", AutoSize = true };
    readonly CheckBox _son =new() { Text = "Yalnız son sürüm", AutoSize = true, Checked = true };
    readonly ComboBox _sira = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly DataGridView _tablo = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    };
    readonly Label _ozet = new() { AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
    List<ArsivEvraki> _liste = [];
    readonly HashSet<ArsivEvraki> _secili = [];
    int _dosyasizMusteri;

    /// <summary>Seçilen dosyalar (yazdırma sırasıyla) ve seçilen yazıcı.</summary>
    public event Func<IReadOnlyList<string>, PrinterSettings, string, Task>? Yazdir;
    public List<Button> Dugmeler { get; } = [];

    public YazdirmaSekmesi(Func<IReadOnlyList<Musteri>> musteriler, Func<Ayarlar> ayarlar) : base("Toplu yazdırma")
    {
        _musteriler = musteriler;
        _ayarlar = ayarlar;
        foreach (var t in MusteriTaramasi.Bilinen) _tur.Items.Add(t);
        _tur.Text = "KDV1";
        _tip.Items.AddRange(["Aylık", "3 aylık", "Yıllık"]);
        _tip.SelectedIndex = 0;
        _tur.TextChanged += (_, _) => { _tip.SelectedIndex = (int)ArsivListesi.VarsayilanTip(_tur.Text); SgkKutulari(); };
        SgkKutulari();
        var gecenAy = DateTime.Today.AddMonths(-1);
        _ay.Value = new DateTime(gecenAy.Year, gecenAy.Month, 1);
        _sira.Items.AddRange(["Müşteriye göre", "Evrak türüne göre (önce hepsinin beyannamesi…)"]);
        _sira.SelectedIndex = 0;
        _sira.SelectedIndexChanged += (_, _) => Doldur();

        var ust = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = true };
        ust.Controls.Add(Etiket("Tür:"));
        ust.Controls.Add(_tur);
        ust.Controls.Add(Etiket("Dönem:"));
        ust.Controls.Add(_tip);
        ust.Controls.Add(_ay);
        ust.Controls.Add(Etiket("   Evrak:"));
        ust.Controls.Add(_byn);
        ust.Controls.Add(_thk);
        ust.Controls.Add(_ihb);
        ust.Controls.Add(_sgkT);
        ust.Controls.Add(_sgkH);
        ust.Controls.Add(_son);
        ust.Controls.Add(Dugme("Listele", ListeleAsync));

        _tablo.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", Width = 30 });
        foreach (var (baslik, gen) in new[] { ("Müşteri", 200), ("Evrak", 110), ("İşyeri", 110), ("Sürüm / bildirge", 150), ("Dosya", 520), ("Klasör", 300) })
            _tablo.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = baslik, Width = gen, ReadOnly = true });
        _tablo.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || _tablo.Rows[e.RowIndex].Tag is not ArsivEvraki a) return;
            if (!_secili.Remove(a)) _secili.Add(a);
            _tablo.Rows[e.RowIndex].Cells[0].Value = _secili.Contains(a);
            OzetiYaz();
        };
        _tablo.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex == 0 || _tablo.Rows[e.RowIndex].Tag is not ArsivEvraki a) return;
            try { Process.Start(new ProcessStartInfo(a.Yol) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        };

        var alt = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(4), WrapContents = false };
        alt.Controls.Add(Dugme("Tümünü seç", () => { _secili.UnionWith(_liste); Doldur(); return Task.CompletedTask; }, isDugmesi: false));
        alt.Controls.Add(Dugme("Seçimi kaldır", () => { _secili.Clear(); Doldur(); return Task.CompletedTask; }, isDugmesi: false));
        alt.Controls.Add(Etiket("Sıra:"));
        alt.Controls.Add(_sira);
        alt.Controls.Add(Dugme("Seçilenleri yazdır…", YazdirAsync));
        alt.Controls.Add(_ozet);

        Controls.Add(_tablo);
        Controls.Add(ust);
        Controls.Add(alt);
        Controls.Add(new Label
        {
            Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft,
            Text = "Çift tık: dosyayı aç. Yalnız program adlandırma kuralına uyan dosyalar bulunur; eski adlılar için önce Müşteri taraması › \"Adları düzelt…\".",
        });
    }

    static Label Etiket(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(6, 8, 2, 3) };

    Button Dugme(string metin, Func<Task> is_, bool isDugmesi = true)
    {
        var b = new Button { Text = metin, AutoSize = true };
        b.Click += async (_, _) => await is_();
        if (isDugmesi) Dugmeler.Add(b);
        return b;
    }

    /// <summary>SGK belgeleri yalnız muhtasar ve prim hizmet beyannamesine bağlıdır.</summary>
    void SgkKutulari()
    {
        bool muh = string.Equals(_tur.Text.Trim(), "MUHSGK", StringComparison.OrdinalIgnoreCase);
        _sgkT.Enabled = _sgkH.Enabled = muh;
    }

    async Task ListeleAsync()
    {
        var evraklar = new List<EvrakTuru>();
        if (_byn.Checked) evraklar.Add(EvrakTuru.Beyanname);
        if (_thk.Checked) evraklar.Add(EvrakTuru.Tahakkuk);
        if (_ihb.Checked) evraklar.Add(EvrakTuru.Ihbarname);
        if (_sgkT.Enabled && _sgkT.Checked) evraklar.Add(EvrakTuru.SgkTahakkuk);
        if (_sgkH.Enabled && _sgkH.Checked) evraklar.Add(EvrakTuru.SgkHizmetListesi);
        var tur = _tur.Text.Trim();
        if (tur.Length == 0 || evraklar.Count == 0) { MessageBox.Show(this, "Tür ve en az bir evrak türü seçin.", Text); return; }
        var ayar = _ayarlar();
        var musteriler = _musteriler().Where(ayar.MusteriDahil).ToList();
        var donem = ArsivListesi.DonemOlustur((DonemTipi)_tip.SelectedIndex, _ay.Value.Year, _ay.Value.Month);
        bool son = _son.Checked;
        Cursor = Cursors.WaitCursor;
        try
        {
            _liste = await Task.Run(() => ArsivListesi.Bul(musteriler, ayar.YArsivKok, tur, donem, evraklar, son, xKok: ayar.XArsivKok));
            _dosyasizMusteri = musteriler.Count - _liste.Select(x => x.Musteri.KisaAd).Distinct().Count();
        }
        catch (Exception ex) { MessageBox.Show(this, "Arşiv okunamadı: " + ex.Message, Text); return; }
        finally { Cursor = Cursors.Default; }
        _secili.Clear();
        _secili.UnionWith(_liste);
        Doldur();
    }

    IEnumerable<ArsivEvraki> Sirali() => _sira.SelectedIndex == 1
        ? _liste.Select((x, i) => (x, i)).OrderBy(p => p.x.Evrak).ThenBy(p => p.i).Select(p => p.x) // türe göre; tür içinde müşteri › işyeri › ay sırası korunur
        : _liste; // ArsivListesi zaten müşteri › evrak › sürüm sırasında

    static string EvrakMetni(EvrakTuru e) => e switch
    {
        EvrakTuru.Beyanname => "Beyanname", EvrakTuru.Tahakkuk => "Tahakkuk", EvrakTuru.Ihbarname => "İhbarname",
        EvrakTuru.SgkTahakkuk => "SGK tahakkuk", EvrakTuru.SgkHizmetListesi => "SGK hizmet l.", _ => e.ToString()
    };

    void Doldur()
    {
        var ayar = _ayarlar();
        _tablo.SuspendLayout();
        _tablo.Rows.Clear();
        foreach (var a in Sirali())
        {
            var kok = a.Sgk ? ayar.XArsivKok : ayar.YArsivKok;
            var klasor = Path.GetDirectoryName(a.Yol) ?? "";
            if (klasor.StartsWith(kok, StringComparison.OrdinalIgnoreCase)) klasor = klasor[kok.Length..].TrimStart('\\');
            int i = _tablo.Rows.Add(_secili.Contains(a), a.Musteri.KisaAd, EvrakMetni(a.Evrak), a.Isyeri,
                a.Sgk ? a.SgkBilgi : a.Surum == 0 ? "asıl" : $"{a.Surum}. düzeltme", a.Ad, klasor);
            _tablo.Rows[i].Tag = a;
        }
        _tablo.ResumeLayout();
        OzetiYaz();
    }

    void OzetiYaz()
    {
        var m = _liste.Select(x => x.Musteri.KisaAd).Distinct().Count();
        _ozet.Text = $"{_liste.Count} dosya, {m} müşteri — seçili: {_secili.Count}" +
                     (_liste.Count > 0 || _dosyasizMusteri > 0 ? $" — bu dönem dosyası olmayan müşteri: {_dosyasizMusteri}" : "");
    }

    async Task YazdirAsync()
    {
        var secilen = Sirali().Where(_secili.Contains).Select(a => a.Yol).ToList();
        if (secilen.Count == 0) { MessageBox.Show(this, "Seçili dosya yok.", Text); return; }
        using var d = new PrintDialog { UseEXDialog = true, AllowSomePages = false, AllowSelection = false };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var isAdi = $"{_tur.Text.Trim()} {_ay.Value:MM.yyyy} ({secilen.Count} dosya)";
        if (Yazdir is { } y) await y(secilen, d.PrinterSettings, isAdi);
    }
}
