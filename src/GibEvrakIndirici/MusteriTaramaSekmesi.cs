using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Indirme;

namespace GibEvrakIndirici;

/// <summary>
/// "Müşteri taraması" sekmesi: seçili müşteriler × türler × vergilendirme dönemi aralığı (§9.5).
/// Seçimleri toplar; listeleme/indirme işini AnaForm yapar (<see cref="Tara"/> olayı).
/// </summary>
sealed class MusteriTaramaSekmesi : TabPage
{
    readonly Func<IReadOnlyList<Musteri>> _musteriler;
    readonly Func<Ayarlar> _ayarlar;
    readonly HashSet<string> _secili = new(StringComparer.Ordinal); // KisaAd
    readonly TextBox _ara = new() { Dock = DockStyle.Top, PlaceholderText = "Müşteri ara (kısa ad, unvan, VKN/TCKN)…" };
    readonly CheckedListBox _liste = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    readonly Label _seciliEtiket = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
    readonly DateTimePicker _bas = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "MM / yyyy", ShowUpDown = true, Width = 100 };
    readonly DateTimePicker _bit = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "MM / yyyy", ShowUpDown = true, Width = 100 };
    readonly CheckedListBox _turler = new() { CheckOnClick = true, Width = 200, Height = 300, IntegralHeight = false };
    bool _dolduruluyor;

    /// <summary>indir = true ise listeleme ardından eksikler de indirilir.</summary>
    public event Func<MusteriTaramasiIstegi, bool, Task>? Tara;
    /// <summary>Ad düzeltme önerileri (bool: önekli eski adlar da önerilsin mi).</summary>
    public event Func<MusteriTaramasiIstegi, bool, Task>? AdDuzelt;
    public event Func<Task>? GeriAl;

    readonly CheckBox _onekliler = new()
    {
        AutoSize = true, Text = "Önekli ama zamansız adları da öner (ör. \"…_BEYANNAME_…AYLIK_.XML_-\"; program bunları zaten tanır)",
    };

    public List<Button> Dugmeler { get; } = [];

    public MusteriTaramaSekmesi(Func<IReadOnlyList<Musteri>> musteriler, Func<Ayarlar> ayarlar) : base("Müşteri taraması")
    {
        _musteriler = musteriler;
        _ayarlar = ayarlar;
        var bugun = DateTime.Today;
        _bas.Value = new DateTime(bugun.Year - 1, 1, 1);
        _bit.Value = new DateTime(bugun.Year, bugun.Month, 1);

        // Sol: müşteri listesi
        var sol = new Panel { Dock = DockStyle.Left, Width = 520, Padding = new Padding(6) };
        var solAlt = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
        solAlt.Controls.Add(Dugme("Görünenleri seç", () => { foreach (var m in Gorunenler()) _secili.Add(m.KisaAd); Doldur(); TurleriOner(); }));
        solAlt.Controls.Add(Dugme("Seçimi temizle", () => { _secili.Clear(); Doldur(); }));
        sol.Controls.Add(_liste);
        sol.Controls.Add(_ara);
        sol.Controls.Add(_seciliEtiket);
        sol.Controls.Add(solAlt);
        _ara.TextChanged += (_, _) => Doldur();
        _liste.ItemCheck += (_, e) =>
        {
            if (_dolduruluyor || _liste.Items[e.Index] is not Oge o) return;
            if (e.NewValue == CheckState.Checked) _secili.Add(o.M.KisaAd); else _secili.Remove(o.M.KisaAd);
            BeginInvoke(() => { SeciliyiYaz(); if (e.NewValue == CheckState.Checked) TurleriOner(); });
        };

        // Sağ: dönem, türler, düğmeler
        var sag = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10), WrapContents = false };
        sag.Controls.Add(new Label { Text = "Vergilendirme dönemi (ay/yıl):", AutoSize = true });
        var donem = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        donem.Controls.Add(_bas);
        donem.Controls.Add(new Label { Text = "–", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        donem.Controls.Add(_bit);
        sag.Controls.Add(donem);
        sag.Controls.Add(new Label { Text = "Beyanname türleri (her tür ayrı GİB sorgusu):", AutoSize = true, Margin = new Padding(3, 12, 3, 3) });
        sag.Controls.Add(_turler);
        var turDugme = new FlowLayoutPanel { AutoSize = true };
        turDugme.Controls.Add(Dugme("Önerilenleri işaretle", () => { TurleriOner(sifirla: true); }));
        sag.Controls.Add(turDugme);
        sag.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 12, 3, 3),
            Text = "Önerilen: KDV1, MUHSGK, geçici vergi + seçili müşterilerin arşivde klasörü olan türler.\n" +
                   "Liste sorguları müşteri × tür başına ~1,5 sn sürer. PDF yalnız arşivde eksikse indirilir (günlük kotadan yalnız onlar düşer).",
        });
        var calistir = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(3, 12, 3, 3) };
        calistir.Controls.Add(Dugme("Listele", () => CalistirAsync(indir: false), isDugmesi: true));
        calistir.Controls.Add(Dugme("Listele ve eksikleri indir", () => CalistirAsync(indir: true), isDugmesi: true));
        sag.Controls.Add(calistir);

        sag.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 18, 3, 3),
            Text = "Arşiv adlarını düzelt: seçili müşterilerin bu dönem ve türlerdeki eski adlı beyanname/tahakkuk PDF'lerine, " +
                   "PDF içeriği ve GİB listesine göre doğru ad önerilir. Önce öneri listesi gösterilir; onaylamadan hiçbir dosyaya dokunulmaz.",
        });
        sag.Controls.Add(_onekliler);
        var duzelt = new FlowLayoutPanel { AutoSize = true };
        duzelt.Controls.Add(Dugme("Adları düzelt…", AdDuzeltAsync, isDugmesi: true));
        duzelt.Controls.Add(Dugme("Düzeltmeyi geri al…", () => GeriAl?.Invoke() ?? Task.CompletedTask, isDugmesi: true));
        sag.Controls.Add(duzelt);

        Controls.Add(sag);
        Controls.Add(sol);

        foreach (var t in MusteriTaramasi.Bilinen) _turler.Items.Add(t);
        _bas.ValueChanged += (_, _) => TurleriOner();
        _bit.ValueChanged += (_, _) => TurleriOner();
        Enter += (_, _) => Doldur(); // müşteri listesi / birim filtresi değişmiş olabilir
        TurleriOner(sifirla: true);
    }

    sealed record Oge(Musteri M)
    {
        public override string ToString()
        {
            var kimlik = !string.IsNullOrWhiteSpace(M.Tckn) ? M.Tckn : M.VergiNo;
            return $"{M.KisaAd}   —   {M.UzunAd}   ({kimlik})";
        }
    }

    Button Dugme(string metin, Func<Task> is_, bool isDugmesi = false)
    {
        var b = new Button { Text = metin, AutoSize = true };
        b.Click += async (_, _) => await is_();
        if (isDugmesi) Dugmeler.Add(b);
        return b;
    }

    Button Dugme(string metin, Action a) => Dugme(metin, () => { a(); return Task.CompletedTask; });

    IEnumerable<Musteri> Dahil()
    {
        var a = _ayarlar();
        return _musteriler().Where(a.MusteriDahil).OrderBy(m => m.KisaAd, StringComparer.CurrentCulture);
    }

    IEnumerable<Musteri> Gorunenler()
    {
        var q = _ara.Text.Trim();
        return q.Length == 0 ? Dahil() : Dahil().Where(m =>
            m.KisaAd.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
            m.UzunAd.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
            m.VergiNo.Contains(q, StringComparison.Ordinal) || m.Tckn.Contains(q, StringComparison.Ordinal));
    }

    void Doldur()
    {
        _dolduruluyor = true;
        _liste.BeginUpdate();
        _liste.Items.Clear();
        foreach (var m in Gorunenler()) _liste.Items.Add(new Oge(m), _secili.Contains(m.KisaAd));
        _liste.EndUpdate();
        _dolduruluyor = false;
        SeciliyiYaz();
    }

    List<Musteri> Secililer() => Dahil().Where(m => _secili.Contains(m.KisaAd)).ToList();

    void SeciliyiYaz()
    {
        var n = Secililer().Count;
        _seciliEtiket.Text = $"{n} müşteri seçili" + (n > 0 ? $" — {string.Join(", ", Secililer().Take(6).Select(m => m.KisaAd))}{(n > 6 ? "…" : "")}" : "");
    }

    Donem Aralik()
    {
        DateTime b = _bas.Value, s = _bit.Value;
        if (s < b) (b, s) = (s, b);
        return new Donem(b.Year, b.Month, s.Year, s.Month);
    }

    /// <summary>Önerilen türleri işaretler (sifirla değilse kullanıcının işaretlerini kaldırmaz).</summary>
    void TurleriOner(bool sifirla = false)
    {
        List<string> oneri;
        try { oneri = MusteriTaramasi.VarsayilanTurler(Secililer(), _ayarlar(), Aralik()); }
        catch (Exception) { oneri = [.. MusteriTaramasi.Ortak]; } // ağ sürücüsü erişilemezse
        foreach (var t in oneri)
            if (!_turler.Items.Contains(t)) _turler.Items.Add(t);
        for (int i = 0; i < _turler.Items.Count; i++)
        {
            bool onerilen = oneri.Contains((string)_turler.Items[i]);
            if (sifirla) _turler.SetItemChecked(i, onerilen);
            else if (onerilen) _turler.SetItemChecked(i, true);
        }
    }

    async Task CalistirAsync(bool indir)
    {
        var musteriler = Secililer();
        var turler = _turler.CheckedItems.Cast<string>().ToList();
        if (musteriler.Count == 0) { MessageBox.Show(this, "En az bir müşteri seçin.", Text); return; }
        if (turler.Count == 0) { MessageBox.Show(this, "En az bir beyanname türü seçin.", Text); return; }
        if (Tara is { } t) await t(new MusteriTaramasiIstegi(musteriler, Aralik(), turler), indir);
    }

    async Task AdDuzeltAsync()
    {
        var musteriler = Secililer();
        var turler = _turler.CheckedItems.Cast<string>().ToList();
        if (musteriler.Count == 0) { MessageBox.Show(this, "En az bir müşteri seçin.", Text); return; }
        if (turler.Count == 0) { MessageBox.Show(this, "En az bir beyanname türü seçin.", Text); return; }
        if (AdDuzelt is { } a) await a(new MusteriTaramasiIstegi(musteriler, Aralik(), turler), _onekliler.Checked);
    }
}
