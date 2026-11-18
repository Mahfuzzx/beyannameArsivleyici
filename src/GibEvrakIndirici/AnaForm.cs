using System.ComponentModel;
using GibEvrakIndirici.Core;
using GibEvrakIndirici.Core.Adlandirma;
using GibEvrakIndirici.Core.Arsiv;
using GibEvrakIndirici.Core.Ebyn;
using GibEvrakIndirici.Core.Indirme;
using GibEvrakIndirici.Core.Luca;
using GibEvrakIndirici.Core.Musteriler;
using GibEvrakIndirici.Core.Sgk;
using Microsoft.Web.WebView2.WinForms;

namespace GibEvrakIndirici;

/// <summary>
/// Ana pencere. Akış (§9): GİB girişi (elle) → e-Beyanname'ye otomatik geçiş →
/// son N günün onaylı beyannameleri listelenir → eksikler otomatik indirilir → özet.
/// </summary>
sealed class AnaForm : Form
{
    // ---- durum ----
    Ayarlar _ayarlar = Ayarlar.Ac();
    MusteriDeposu _musteriler = MusteriDeposu.Ac(Ayarlar.MusteriDosyasi);
    IsyeriDeposu _isyerleri = IsyeriDeposu.Ac(Ayarlar.IsyeriDosyasi);
    readonly TamamlananDeposu _tamamlanan = TamamlananDeposu.Ac(Ayarlar.TamamlananDosyasi);
    List<BeyannameSatiri> _satirlar = [];
    readonly BindingList<SatirGorunumu> _gorunum = [];
    CancellationTokenSource? _iptal;

    // ---- denetimler ----
    readonly TabControl _sekmeler = new() { Dock = DockStyle.Fill };
    readonly TabPage _evrakSekme = new("Evraklar"), _gibSekme = new("GİB girişi"), _lucaSekme = new("Luca");
    readonly WebView2 _gibWeb = new() { Dock = DockStyle.Fill };
    readonly WebView2 _lucaWeb = new() { Dock = DockStyle.Fill };
    readonly DataGridView _tablo = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        RowHeadersVisible = false,
    };
    readonly ListBox _log = new() { Dock = DockStyle.Bottom, Height = 160, HorizontalScrollbar = true, IntegralHeight = false };
    readonly ToolStripStatusLabel _durum = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel _oturum = new() { Text = "GİB: kapalı", ForeColor = Color.Firebrick };
    readonly ToolStripProgressBar _ilerleme = new() { Size = new Size(260, 16), Visible = false };
    readonly ToolStripStatusLabel _ilerlemeYazi = new() { Visible = false };
    readonly DateTimePicker _bas = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    readonly DateTimePicker _bit = new() { Format = DateTimePickerFormat.Short, Width = 100 };
    readonly CheckBox _sadeceEksik = new() { Text = "Sadece eksikler", Checked = true, AutoSize = true };
    readonly List<Button> _isDugmeleri = [];
    Button _durdur = null!;

    readonly MusteriTaramaSekmesi _taramaSekme;
    GibOturumu _gib = null!;
    LucaOturumu _luca = null!;
    bool _lucaBasladi;

    public AnaForm()
    {
        Text = "GİB Evrak İndirici";
        Width = 1250; Height = 820;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        _bas.Value = DateTime.Today.AddDays(-_ayarlar.AcilisGunSayisi);
        _bit.Value = DateTime.Today;

        // Evraklar sekmesi
        var ust = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4), WrapContents = false };
        ust.Controls.Add(new Label { Text = "Yükleme:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        ust.Controls.Add(_bas);
        ust.Controls.Add(new Label { Text = "–", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        ust.Controls.Add(_bit);
        ust.Controls.Add(Dugme("Listele", async () => await ListeleAsync(_bas.Value, _bit.Value), isDugmesi: true));
        ust.Controls.Add(Dugme("Eksikleri indir", async () => await IndirAsync(), isDugmesi: true));
        ust.Controls.Add(Dugme("Arşiv taraması", async () => await ArsivTaramasiAsync(), isDugmesi: true));
        _durdur = Dugme("Durdur", () => { _iptal?.Cancel(); return Task.CompletedTask; });
        _durdur.Enabled = false;
        ust.Controls.Add(_durdur);
        _sadeceEksik.Margin = new Padding(12, 8, 3, 3);
        _sadeceEksik.CheckedChanged += (_, _) => TabloyuDoldur();
        ust.Controls.Add(_sadeceEksik);
        ust.Controls.Add(new Label { Width = 20 });
        ust.Controls.Add(Dugme("Müşteri Excel'i al", async () => await MusteriExceliAlAsync(), isDugmesi: true));
        ust.Controls.Add(Dugme("X arşivinden işyeri öğren", async () => await XtenOgrenAsync(), isDugmesi: true));
        ust.Controls.Add(Dugme("Ayarlar", () => { AyarlariAc(); return Task.CompletedTask; }));

        _tablo.DataSource = _gorunum;
        _tablo.CellFormatting += TabloBicimle;
        _evrakSekme.Controls.Add(_tablo);
        _evrakSekme.Controls.Add(_log);
        _evrakSekme.Controls.Add(ust);

        // GİB sekmesi
        var gibAlt = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(4) };
        gibAlt.Controls.Add(new Label { Text = "Bilgilerinizi ve doğrulama kodunu kendiniz girin. Program şifre saklamaz.", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        gibAlt.Controls.Add(Dugme("Yeniden giriş", () => { _gib.YenidenGiris(); return Task.CompletedTask; }));
        _gibSekme.Controls.Add(_gibWeb);
        _gibSekme.Controls.Add(gibAlt);

        // Luca sekmesi
        var lucaUst = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(4) };
        lucaUst.Controls.Add(new Label { Text = "Luca'ya girip \"Luca Mali Müşavir Paketi\"ni açın, sonra:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        lucaUst.Controls.Add(Dugme("İşyerlerini Luca'dan al", async () => await LucadanIsyeriAlAsync(), isDugmesi: true));
        _lucaSekme.Controls.Add(_lucaWeb);
        _lucaSekme.Controls.Add(lucaUst);

        // Müşteri taraması sekmesi
        _taramaSekme = new MusteriTaramaSekmesi(() => _musteriler.Liste, () => _ayarlar);
        _taramaSekme.Tara += MusteriTaramasiAsync;
        _isDugmeleri.AddRange(_taramaSekme.Dugmeler);

        _sekmeler.TabPages.AddRange([_evrakSekme, _taramaSekme, _gibSekme, _lucaSekme]);
        _sekmeler.SelectedIndexChanged += async (_, _) =>
        {
            if (_sekmeler.SelectedTab == _lucaSekme && !_lucaBasladi) { _lucaBasladi = true; await _luca.BaslatAsync(); }
        };

        var durumCubugu = new StatusStrip();
        durumCubugu.Items.AddRange([_oturum, _durum, _ilerlemeYazi, _ilerleme]);

        Controls.Add(_sekmeler);
        Controls.Add(durumCubugu);

        Shown += async (_, _) => await AcilisAsync();
    }

    Button Dugme(string metin, Func<Task> is_, bool isDugmesi = false)
    {
        var b = new Button { Text = metin, AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
        b.Click += async (_, _) =>
        {
            try { await is_(); }
            catch (Exception ex) { Log(Seviye.Hata, ex.Message); }
        };
        if (isDugmesi) _isDugmeleri.Add(b);
        return b;
    }

    // ---------------- açılış ----------------

    async Task AcilisAsync()
    {
        _gib = new GibOturumu(_gibWeb);
        _luca = new LucaOturumu(_lucaWeb);
        _gib.DurumDegisti += (_, m) => Durum(m);
        _gib.EbeyannameHazir += async (_, _) =>
        {
            _oturum.Text = "GİB: e-Beyanname açık"; _oturum.ForeColor = Color.ForestGreen;
            _sekmeler.SelectedTab = _evrakSekme;
            await OtomatikAkisAsync();
        };

        _musteriler.KlasorOnekiniAt(_ayarlar.AtilacakKlasorOneki);
        if (_musteriler.Liste.Count == 0)
            Log(Seviye.Uyari, "Müşteri listesi boş. \"Müşteri Excel'i al\" ile Luca müşteri listesini içe aktarın.");
        else
            Log(Seviye.Bilgi, $"{_musteriler.Liste.Count} müşteri, {_isyerleri.Kayitlar.Count} SGK işyeri kaydı yüklendi.");
        FiltreyiBildir();

        _sekmeler.SelectedTab = _gibSekme;
        try { await _gib.BaslatAsync(); }
        catch (Exception ex) { Log(Seviye.Hata, "WebView2 başlatılamadı: " + ex.Message); }
    }

    /// <summary>Giriş sonrası tam otomatik: son N gün → listele → eksikleri indir.</summary>
    async Task OtomatikAkisAsync()
    {
        try
        {
            _bas.Value = DateTime.Today.AddDays(-_ayarlar.AcilisGunSayisi);
            _bit.Value = DateTime.Today;
            await ListeleAsync(_bas.Value, _bit.Value);
            await IndirAsync();
        }
        catch (Exception ex) { Log(Seviye.Hata, ex.Message); }
    }

    // ---------------- işlemler ----------------

    EbynIstemci Istemci() => new(_gib) { IstekAraligi = TimeSpan.FromMilliseconds(Math.Max(1000, _ayarlar.IstekAraligiMs)) };

    Indirici Indirici() => new(Istemci(), _musteriler, _isyerleri, _ayarlar,
        s => Task.FromResult(Diyaloglar.IsyeriSor(this, s)),
        () => _isyerleri.Kaydet(Ayarlar.IsyeriDosyasi),
        tamamlanan: _tamamlanan);

    /// <summary>İndirme bitince/kesilince tamamlanan listesini diske yazar.</summary>
    void TamamlananKaydet()
    {
        try { _tamamlanan.Kaydet(Ayarlar.TamamlananDosyasi); }
        catch (Exception ex) { Log(Seviye.Uyari, "Tamamlanan listesi kaydedilemedi: " + ex.Message); }
    }

    async Task IsCalistirAsync(string ad, Func<CancellationToken, Task> is_)
    {
        if (_iptal != null) { Log(Seviye.Uyari, "Önce süren işlemin bitmesini bekleyin."); return; }
        _iptal = new CancellationTokenSource();
        foreach (var b in _isDugmeleri) b.Enabled = false;
        _durdur.Enabled = true;
        try { await is_(_iptal.Token); }
        catch (OperationCanceledException) { Log(Seviye.Uyari, $"{ad} durduruldu."); }
        catch (EbynOturumHatasi ex)
        {
            Log(Seviye.Hata, $"{ad} durduruldu — {ex.Message} Yeniden giriş yapın; \"Eksikleri indir\" kaldığı yerden devam eder.");
            _gib.YenidenGiris();
            _oturum.Text = "GİB: kapalı"; _oturum.ForeColor = Color.Firebrick;
            _sekmeler.SelectedTab = _gibSekme;
        }
        catch (EbynHatasi ex) when (!_gib.Hazir)
        {
            Log(Seviye.Hata, ex.Message);
            _oturum.Text = "GİB: kapalı"; _oturum.ForeColor = Color.Firebrick;
            _sekmeler.SelectedTab = _gibSekme;
        }
        catch (Exception ex) { Log(Seviye.Hata, $"{ad}: {ex.Message}"); }
        finally
        {
            TamamlananKaydet();
            IlerlemeyiGizle();
            _iptal.Dispose(); _iptal = null;
            foreach (var b in _isDugmeleri) b.Enabled = true;
            _durdur.Enabled = false;
        }
    }

    /// <summary>
    /// İlerleme çubuğu: (biten, toplam) bildirimi alır; çubuğun yanında aşama, sayı, yüzde ve kalan süre tahmini yazar.
    /// Her çağrı yeni bir aşama başlatır (süre tahmini o aşamanın başından hesaplanır).
    /// </summary>
    IProgress<(int Biten, int Toplam)> Oran(string asama)
    {
        var basla = DateTime.Now;
        return new Progress<(int Biten, int Toplam)>(o =>
        {
            _ilerleme.Visible = _ilerlemeYazi.Visible = true;
            if (o.Toplam <= 0)
            {
                _ilerleme.Style = ProgressBarStyle.Marquee;
                _ilerlemeYazi.Text = asama;
                return;
            }
            _ilerleme.Style = ProgressBarStyle.Continuous;
            _ilerleme.Maximum = o.Toplam;
            _ilerleme.Value = Math.Clamp(o.Biten, 0, o.Toplam);
            var yazi = $"{asama}: {o.Biten}/{o.Toplam} (%{o.Biten * 100 / o.Toplam})";
            var gecen = DateTime.Now - basla;
            if (o.Biten > 0 && o.Biten < o.Toplam && gecen > TimeSpan.FromSeconds(5))
            {
                var kalan = TimeSpan.FromSeconds(gecen.TotalSeconds / o.Biten * (o.Toplam - o.Biten));
                yazi += kalan.TotalMinutes >= 1 ? $" — ~{Math.Ceiling(kalan.TotalMinutes)} dk kaldı" : $" — ~{Math.Ceiling(kalan.TotalSeconds)} sn kaldı";
            }
            _ilerlemeYazi.Text = yazi;
        });
    }

    void IlerlemeyiGizle()
    {
        _ilerleme.Visible = _ilerlemeYazi.Visible = false;
        _ilerleme.Value = 0;
    }

    Task ListeleAsync(DateTime bas, DateTime bit) => IsCalistirAsync("Listeleme", async ct =>
    {
        var ilerleme = new Progress<string>(Durum);
        _satirlar = await Istemci().OnayliBeyannamelerAsync(bas, bit, ilerleme, ct, Oran("Listeleniyor (ay)"));
        Log(Seviye.Bilgi, $"{bas:dd.MM.yyyy}–{bit:dd.MM.yyyy}: {_satirlar.Count} onaylı beyanname listelendi.");
        TabloyuDoldur();
    });

    Task IndirAsync() => IsCalistirAsync("İndirme", async ct =>
    {
        if (_satirlar.Count == 0) { Log(Seviye.Bilgi, "İndirilecek satır yok."); return; }
        var ilerleme = new Progress<IndirmeOlayi>(o => { Log(o.Seviye, o.Mesaj); Durum(o.Mesaj); });
        var ozet = await Indirici().CalistirAsync(_satirlar, ilerleme, ct, Oran("İndiriliyor (beyanname)"));
        TabloyuDoldur();
        var metin = $"{(ozet.Durduruldu ? "Yarıda kesildi (GİB kısıtlaması)" : "Bitti")} — indirilen: {ozet.Indirilen}, zaten var: {ozet.Atlanan}, uyarı: {ozet.Uyari}, hata: {ozet.Hata}" + (ozet.FiltreDisi > 0 ? $", filtre dışı: {ozet.FiltreDisi}" : "");
        Log(Seviye.Bilgi, metin); Durum(metin);
    });

    /// <summary>Son TaramaYilSayisi yılın tüm onaylı beyannameleri (aylık dilimler) → eksikleri indir.</summary>
    Task ArsivTaramasiAsync() => IsCalistirAsync("Arşiv taraması", async ct =>
    {
        var bas = new DateTime(DateTime.Today.Year - Math.Max(1, _ayarlar.TaramaYilSayisi) + 1, 1, 1);
        Log(Seviye.Bilgi, $"Arşiv taraması: {bas:dd.MM.yyyy}–{DateTime.Today:dd.MM.yyyy}");
        _satirlar = await Istemci().OnayliBeyannamelerAsync(bas, DateTime.Today, new Progress<string>(Durum), ct, Oran("Listeleniyor (ay)"));
        TabloyuDoldur();
        var ozet = await Indirici().CalistirAsync(_satirlar, new Progress<IndirmeOlayi>(o => { Log(o.Seviye, o.Mesaj); Durum(o.Mesaj); }), ct, Oran("İndiriliyor (beyanname)"));
        TabloyuDoldur();
        Log(Seviye.Bilgi, $"Arşiv taraması {(ozet.Durduruldu ? "yarıda kesildi (GİB kısıtlaması)" : "bitti")} — indirilen: {ozet.Indirilen}, zaten var: {ozet.Atlanan}, uyarı: {ozet.Uyari}, hata: {ozet.Hata}" + (ozet.FiltreDisi > 0 ? $", filtre dışı: {ozet.FiltreDisi}" : ""));
    });

    /// <summary>Seçili müşteriler × türler × dönem aralığı (§9.5). Sonuç ana tabloya gelir; istenirse eksikler indirilir.</summary>
    Task MusteriTaramasiAsync(MusteriTaramasiIstegi istek, bool indir) => IsCalistirAsync("Müşteri taraması", async ct =>
    {
        _sekmeler.SelectedTab = _evrakSekme;
        Log(Seviye.Bilgi, "Müşteri taraması: " + MusteriTaramasi.Aciklama(istek));
        var istemci = Istemci();
        var ilerleme = new Progress<string>(Durum);
        var satirlar = new List<BeyannameSatiri>();
        var oran = Oran("Listeleniyor (müşteri × tür)");
        int toplamSorgu = istek.Musteriler.Count * istek.Turler.Count, bitenSorgu = 0;
        oran.Report((0, toplamSorgu));
        foreach (var m in istek.Musteriler)
        {
            ct.ThrowIfCancellationRequested();
            if (MusteriTaramasi.Kimlik(m) is not { } kimlik)
            {
                Log(Seviye.Uyari, $"{m.KisaAd}: VKN/TCKN yok, atlandı");
                bitenSorgu += istek.Turler.Count; oran.Report((bitenSorgu, toplamSorgu));
                continue;
            }
            var l = await istemci.MukellefBeyannameleriAsync(kimlik, istek.Turler, istek.Aralik, DateTime.Today, ilerleme, ct,
                turBitti: () => oran.Report((++bitenSorgu, toplamSorgu)));
            Log(Seviye.Bilgi, $"{m.KisaAd}: {l.Count} onaylı beyanname");
            satirlar.AddRange(l);
        }
        _satirlar = satirlar.GroupBy(x => x.Oid).Select(g => g.First()).ToList();
        TabloyuDoldur();
        Log(Seviye.Bilgi, $"Müşteri taraması listelendi: {_satirlar.Count} beyanname." + (indir ? "" : " İndirmek için \"Eksikleri indir\"."));
        if (!indir) return;
        var ozet = await Indirici().CalistirAsync(_satirlar, new Progress<IndirmeOlayi>(o => { Log(o.Seviye, o.Mesaj); Durum(o.Mesaj); }), ct, Oran("İndiriliyor (beyanname)"));
        TabloyuDoldur();
        Log(Seviye.Bilgi, $"Müşteri taraması {(ozet.Durduruldu ? "yarıda kesildi (GİB kısıtlaması)" : "bitti")} — indirilen: {ozet.Indirilen}, zaten var: {ozet.Atlanan}, uyarı: {ozet.Uyari}, hata: {ozet.Hata}");
    });

    async Task MusteriExceliAlAsync()
    {
        using var d = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", Title = "Luca müşteri listesi" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var liste = await Task.Run(() => MusteriExcelOkuyucu.Oku(d.FileName));
        _musteriler.Birlestir(liste);
        _musteriler.Kaydet(Ayarlar.MusteriDosyasi);
        var kimliksiz = liste.Where(m => string.IsNullOrWhiteSpace(m.VergiNo) && string.IsNullOrWhiteSpace(m.Tckn)).Select(m => m.KisaAd).ToList();
        Log(Seviye.Bilgi, $"{liste.Count} müşteri içe aktarıldı.");
        if (kimliksiz.Count > 0) Log(Seviye.Uyari, "VKN/TCKN'si boş müşteriler eşleşmeyecek: " + string.Join(", ", kimliksiz));
        TabloyuDoldur();
    }

    Task XtenOgrenAsync() => IsCalistirAsync("İşyeri öğrenme", async ct =>
    {
        var ilerleme = new Progress<string>(Durum);
        var n = await Task.Run(() => _isyerleri.XArsivindenOgren(_ayarlar.XArsivKok, ilerleme, ct), ct);
        _isyerleri.Kaydet(Ayarlar.IsyeriDosyasi);
        Log(Seviye.Bilgi, $"X arşivinden {n} yeni işyeri sicili öğrenildi (toplam {_isyerleri.Kayitlar.Count}).");
    });

    Task LucadanIsyeriAlAsync() => IsCalistirAsync("Luca işyeri aktarımı", async ct =>
    {
        if (!_luca.PaketAcik) { Log(Seviye.Uyari, "Önce Luca'ya girip \"Luca Mali Müşavir Paketi\"ni açın."); return; }
        if (string.IsNullOrWhiteSpace(_ayarlar.LucaUyeNo))
        {
            Log(Seviye.Uyari, "Ayarlar'da Luca üye no (LucaUyeNo) girilmeli."); return;
        }
        if (MessageBox.Show(this, "Program Luca'da firmaları sırayla seçip işyeri listelerini okuyacak, sonunda seçili firmanıza geri dönecek.\n" +
                                  "Bu sırada Luca'da başka işlem yapmayın. Devam edilsin mi?", "Luca işyeri aktarımı",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

        var liste = await new LucaIsyeriAktarici(_luca).AktarAsync(_ayarlar.LucaUyeNo, new Progress<string>(Durum), ct,
            firmaDahil: _ayarlar.MusteriDahil);
        var klasorler = _musteriler.Liste.GroupBy(m => m.KisaAd).ToDictionary(g => g.Key, g => g.First().XKlasor);
        var n = LucaIsyeriAktarici.DepoyaEkle(_isyerleri, liste, f => klasorler.GetValueOrDefault(f) ?? KlasorAdi.XIcin(f));
        _isyerleri.Kaydet(Ayarlar.IsyeriDosyasi);
        Log(Seviye.Bilgi, $"Luca'dan {n} işyeri alındı ({liste.Select(i => i.FirmaKisaAd).Distinct().Count()} firma).");
    });

    void AyarlariAc()
    {
        if (!Diyaloglar.AyarlariDuzenle(this, _ayarlar)) { _ayarlar = Ayarlar.Ac(); return; }
        _ayarlar.Kaydet();
        _musteriler.KlasorOnekiniAt(_ayarlar.AtilacakKlasorOneki);
        Log(Seviye.Bilgi, "Ayarlar kaydedildi.");
        FiltreyiBildir();
        TabloyuDoldur();
    }

    void FiltreyiBildir()
    {
        if (_ayarlar.Filtre == MusteriFiltresi.Hepsi) return;
        int dahil = _musteriler.Liste.Count(_ayarlar.MusteriDahil);
        Log(Seviye.Bilgi, $"Müşteri filtresi: {_ayarlar.FiltreAciklamasi} — {dahil}/{_musteriler.Liste.Count} müşteri işlenecek. " +
                          $"Arşiv: {_ayarlar.YArsivKok} / {_ayarlar.XArsivKok}");
    }

    // ---------------- tablo ----------------

    void TabloyuDoldur()
    {
        var siralar = DuzeltmeSiralayici.Sirala(_satirlar);
        var ind = Indirici();
        _gorunum.RaiseListChangedEvents = false;
        _gorunum.Clear();
        int filtreDisi = 0;
        foreach (var b in _satirlar.OrderBy(x => x.Unvan).ThenBy(x => x.Tur).ThenBy(x => x.YuklemeZamani))
        {
            if (_musteriler.Bul(b.VknTckn) is { } fm && !_ayarlar.MusteriDahil(fm)) { filtreDisi++; continue; }
            var d = ind.Durum(b, siralar.GetValueOrDefault(b.Oid));
            var g = SatirGorunumu.Olustur(b, d);
            if (_sadeceEksik.Checked && !g.EksikVar) continue;
            _gorunum.Add(g);
        }
        _gorunum.RaiseListChangedEvents = true;
        _gorunum.ResetBindings();
        Durum($"{_satirlar.Count} kayıt, {_gorunum.Count} gösteriliyor" + (filtreDisi > 0 ? $", {filtreDisi} filtre dışı" : ""));
    }

    void TabloBicimle(object? s, DataGridViewCellFormattingEventArgs e)
    {
        if (e.Value is not string v) return;
        if (v == "eksik") e.CellStyle!.ForeColor = Color.DarkOrange;
        else if (v == "✓") e.CellStyle!.ForeColor = Color.ForestGreen;
        else if (v.StartsWith("Listede yok")) e.CellStyle!.ForeColor = Color.Firebrick;
    }

    // ---------------- log ----------------

    void Durum(string m) => _durum.Text = m;

    void Log(Seviye s, string mesaj)
    {
        var satir = $"{DateTime.Now:HH:mm:ss} {s,-9} {mesaj}";
        _log.Items.Add(satir);
        _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
        try
        {
            Directory.CreateDirectory(Ayarlar.LogKlasoru);
            File.AppendAllText(Path.Combine(Ayarlar.LogKlasoru, $"{DateTime.Today:yyyy-MM-dd}.log"), satir + Environment.NewLine);
        }
        catch { /* log yazılamazsa uygulama durmasın */ }
    }
}

sealed class SatirGorunumu
{
    [DisplayName("Müşteri")] public string Musteri { get; init; } = "";
    [DisplayName("Tür")] public string Tur { get; init; } = "";
    [DisplayName("VKN/TCKN")] public string Kimlik { get; init; } = "";
    [DisplayName("Unvan")] public string Unvan { get; init; } = "";
    [DisplayName("Dönem")] public string Donem { get; init; } = "";
    [DisplayName("Yükleme")] public string Yukleme { get; init; } = "";
    [DisplayName("B")] public string B { get; init; } = "";
    [DisplayName("T")] public string T { get; init; } = "";
    [DisplayName("İhb.")] public string I { get; init; } = "";
    [DisplayName("SGK")] public string Sgk { get; init; } = "";
    [DisplayName("Not")] public string Not { get; init; } = "";
    [Browsable(false)] public bool EksikVar { get; init; }

    static string D(bool? v) => v switch { true => "✓", false => "eksik", null => "–" };

    public static SatirGorunumu Olustur(BeyannameSatiri b, SatirDurumu d) => new()
    {
        Musteri = d.Musteri?.KisaAd ?? "",
        Tur = b.Tur,
        Kimlik = b.VknTckn,
        Unvan = b.Unvan,
        Donem = b.Donem.ToString(),
        Yukleme = b.YuklemeZamani.ToString("dd.MM.yyyy HH:mm"),
        B = D(d.Beyanname),
        T = D(d.Tahakkuk),
        I = b.IhbarnameVar ? "var" : "–",
        Sgk = b.SgkBildirimVar ? "var" : "–",
        Not = d.Musteri is null ? "Listede yok (müşteri Excel'i)" : d.DuzeltmeSira > 0 ? Adlandirma.DosyaAdlariDuz(d.DuzeltmeSira) : "",
        // İhbarname/SGK için kesin durum ek sorgu gerektirir → "var" olan satırlar da kontrol edilsin
        EksikVar = d.Musteri is null || d.Beyanname == false || d.Tahakkuk == false || b.IhbarnameVar || b.SgkBildirimVar,
    };
}

static class Adlandirma
{
    public static string DosyaAdlariDuz(int sira) => Core.Adlandirma.DosyaAdlari.DuzeltmeEki(sira).TrimStart('_');
}
