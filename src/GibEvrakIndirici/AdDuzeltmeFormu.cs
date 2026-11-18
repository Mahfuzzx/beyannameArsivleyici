using GibEvrakIndirici.Core.Arsiv;

namespace GibEvrakIndirici;

/// <summary>
/// Ad düzeltme önerilerini gösterir (§9.8). Yalnız "Önerilen" satırlar seçilebilir; varsayılan olarak hepsi seçili.
/// "Seçilenleri yeniden adlandır" onaylanınca <see cref="Secilenler"/> ile DialogResult.OK döner; uygulamayı AnaForm yapar.
/// </summary>
sealed class AdDuzeltmeFormu : Form
{
    readonly List<AdOnerisi> _oneriler;
    readonly string _kok;
    readonly DataGridView _tablo = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
    };
    readonly ComboBox _filtre = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly Label _ozet = new() { AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
    readonly HashSet<AdOnerisi> _secili = [];

    public IReadOnlyList<AdOnerisi> Secilenler => _oneriler.Where(_secili.Contains).ToList();

    static string DurumMetni(OneriDurumu d) => d switch
    {
        OneriDurumu.Onerilen => "Önerilen",
        OneriDurumu.Kopya => "Kopya olabilir",
        OneriDurumu.Belirsiz => "Belirsiz",
        OneriDurumu.Eslesmedi => "Eşleşmedi",
        OneriDurumu.Okunamadi => "Okunamadı",
        OneriDurumu.YanlisKlasor => "Yanlış klasör",
        _ => d.ToString()
    };

    public AdDuzeltmeFormu(List<AdOnerisi> oneriler, string yKok)
    {
        _oneriler = oneriler;
        _kok = yKok;
        Text = "Arşiv adlarını düzelt — öneriler";
        Width = 1400; Height = 800;
        StartPosition = FormStartPosition.CenterParent;
        foreach (var o in oneriler.Where(o => o.Durum == OneriDurumu.Onerilen)) _secili.Add(o);

        _tablo.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Sec", HeaderText = "", Width = 30 });
        foreach (var (ad, baslik, gen) in new[] { ("Durum", "Durum", 110), ("Klasor", "Klasör", 230), ("Eski", "Mevcut ad", 380), ("Yeni", "Önerilen ad", 380), ("Aciklama", "Dayanak", 600) })
            _tablo.Columns.Add(new DataGridViewTextBoxColumn { Name = ad, HeaderText = baslik, Width = gen, ReadOnly = true });
        _tablo.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || _tablo.Rows[e.RowIndex].Tag is not AdOnerisi o || o.Durum != OneriDurumu.Onerilen) return;
            if (!_secili.Remove(o)) _secili.Add(o);
            _tablo.Rows[e.RowIndex].Cells[0].Value = _secili.Contains(o);
            OzetiYaz();
        };

        _filtre.Items.AddRange(["Hepsi", "Önerilenler", "Önerilmeyenler"]);
        _filtre.SelectedIndex = 0;
        _filtre.SelectedIndexChanged += (_, _) => Doldur();

        var ust = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(4) };
        ust.Controls.Add(new Label { Text = "Göster:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        ust.Controls.Add(_filtre);
        ust.Controls.Add(Dugme("Önerilenlerin hepsini seç", () => { foreach (var o in _oneriler.Where(o => o.Durum == OneriDurumu.Onerilen)) _secili.Add(o); Doldur(); }));
        ust.Controls.Add(Dugme("Seçimi kaldır", () => { _secili.Clear(); Doldur(); }));
        ust.Controls.Add(_ozet);

        var alt = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        alt.Controls.Add(Dugme("Kapat", () => { DialogResult = DialogResult.Cancel; Close(); }));
        alt.Controls.Add(Dugme("Seçilenleri yeniden adlandır", Uygula));
        alt.Controls.Add(new Label
        {
            AutoSize = true, Margin = new Padding(3, 10, 20, 3),
            Text = "Hiçbir dosya silinmez, üzerine yazılmaz. Her uygulama geri alınabilir (Müşteri taraması › \"Düzeltmeyi geri al…\").",
        });

        Controls.Add(_tablo);
        Controls.Add(ust);
        Controls.Add(alt);
        Doldur();
    }

    static Button Dugme(string metin, Action a)
    {
        var b = new Button { Text = metin, AutoSize = true };
        b.Click += (_, _) => a();
        return b;
    }

    string Goreli(string klasor) => klasor.StartsWith(_kok, StringComparison.OrdinalIgnoreCase) ? klasor[_kok.Length..].TrimStart('\\') : klasor;

    void Doldur()
    {
        _tablo.SuspendLayout();
        _tablo.Rows.Clear();
        var liste = _filtre.SelectedIndex switch
        {
            1 => _oneriler.Where(o => o.Durum == OneriDurumu.Onerilen),
            2 => _oneriler.Where(o => o.Durum != OneriDurumu.Onerilen),
            _ => _oneriler
        };
        foreach (var o in liste)
        {
            int i = _tablo.Rows.Add(_secili.Contains(o), DurumMetni(o.Durum), Goreli(o.Klasor), o.EskiAd, o.YeniAd ?? "", o.Aciklama);
            var r = _tablo.Rows[i];
            r.Tag = o;
            if (o.Durum != OneriDurumu.Onerilen)
            {
                r.Cells[0].ReadOnly = true;
                r.DefaultCellStyle.ForeColor = o.Durum == OneriDurumu.Kopya ? Color.DarkOrange : Color.Gray;
            }
        }
        _tablo.ResumeLayout();
        OzetiYaz();
    }

    void OzetiYaz()
    {
        var gruplar = _oneriler.GroupBy(o => o.Durum).OrderBy(g => g.Key).Select(g => $"{DurumMetni(g.Key)}: {g.Count()}");
        _ozet.Text = $"{_oneriler.Count} dosya — {string.Join(", ", gruplar)} — seçili: {_secili.Count}";
    }

    void Uygula()
    {
        if (_secili.Count == 0) { MessageBox.Show(this, "Seçili dosya yok.", Text); return; }
        if (MessageBox.Show(this, $"{_secili.Count} dosyanın adı değiştirilecek. Hiçbir dosya silinmez; işlem sonradan geri alınabilir.\n\nDevam edilsin mi?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        DialogResult = DialogResult.OK;
        Close();
    }
}
