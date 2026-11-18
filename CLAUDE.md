# CLAUDE.md — GİB Evrak İndirici (beyannameArsivleyici)

Mali müşavirlik ofisi için: GİB e-Beyanname'den tüm müşterilerin **onaylı** beyanname, tahakkuk, vergi ihbarnamesi,
SGK tahakkuk ve SGK hizmet listelerini indirip mevcut ofis arşivine, mevcut adlandırma kurallarıyla yerleştiren uygulama.
Kullanıcıyla Türkçe konuşulur. Ayrıntılı keşif notları ve tüm kararlar: [GIB_AKIS_NOTLARI.md](GIB_AKIS_NOTLARI.md) (bölüm numaraları aşağıda §).

## Yapı
- `src/GibEvrakIndirici.Core` (net10.0) — iş mantığı, UI'sız, test edilebilir. ClosedXML (müşteri Excel'i), PdfPig (PDF metni).
  - `Ebyn/` e-Beyanname: `EbynKomutlari` (beyaz liste + URL/parametre üretimi), `EbynIstemci` (hız sınırı, PDF doğrulama, yeniden deneme,
    kısıtlama/oturum hataları), `EbynAyristirici` (SERVICERESULT ve liste HTML'i), `BeyannamePdfOkuyucu` (onay zamanı).
  - `Indirme/Indirici` eksik evrakları indirir; `Arsiv/ArsivDenetci` "zaten var mı"; `Adlandirma/` klasör ve dosya adları;
    `Musteriler/` Luca müşteri Excel'i → JSON; `Sgk/` sicil okuma + sicil→işyeri tablosu; `Luca/` Luca'dan toplu işyeri aktarımı; `Ayarlar`.
- `src/GibEvrakIndirici` (net10.0-windows, WinForms + WebView2) — `AnaForm`, `GibOturumu` (DVD girişi → e-Beyanname SSO, istekleri sayfa
  içinde `fetch` ile atar, TOKEN sayfada), `LucaOturumu`, `WebViewYardimci` (`Runtime.evaluate`).
- `tests/GibEvrakIndirici.Tests` (xUnit). "Gerçek…" adlı testler bu makinedeki gerçek dosyaları okur, yoksa sessizce geçer.
- Komutlar: `dotnet test tests\GibEvrakIndirici.Tests` · `dotnet build GibEvrakIndirici.sln`.
  Uygulama açıksa exe kilitlidir → derlemeden önce kullanıcı kapatmalı. Çalıştırma: `src\GibEvrakIndirici\bin\Debug\net10.0-windows\GibEvrakIndirici.exe`.
- Kullanıcı verisi repo dışında: `%APPDATA%\GibEvrakIndirici\` (`ayarlar.json`, `musteriler.json`, `isyerleri.json`, `log\yyyy-MM-dd.log`).

## Değişmez kurallar (güvenlik)
- GİB/Luca **şifresi ve doğrulama kodunu kullanıcı kendisi girer**. Uygulama şifre saklamaz, captcha çözmez.
- e-Beyanname'de yalnızca beyaz listedeki komutlar: `BEYANNAMELISTESI`, `IHBARNAMELERIGETIR`, `THKESASBILGISGKMESAJLARI`, `IMAJ` (görüntüleme).
  **Asla**: onay/iptal/özel onay/MUHSGK manuel gönderim/paket yükleme (§7); e-Beyan'da onayla/düzeltme/silme (§13).
- Arşivdeki dosyanın **üzerine yazılmaz, silinmez** (`.part` → `File.Move(overwrite:false)`).
- Luca e-Bildirge sayfasındaki kullanıcı adı/işyeri kodu/şifre alanları okunmaz, saklanmaz.
- Eski programın ayar dosyasında (`C:\LucaEntegrasyon\LucaBeyanname.xml`) düz metin şifreler ve API anahtarı var → içeriği kopyalanmaz, yazılmaz, gösterilmez.
- **Repo herkese açık:** müşteri adı, VKN/TCKN, sicil no, sunucu adı, log içeriği commit'e girmez. Testlerde uydurma değer kullan
  (`1111111111`, `ÖRNEK …`). Commit öncesi `musteriler.json`/`isyerleri.json` değerleriyle repo taranmalı (bugün öyle yapıldı, 0 eşleşme).

## Arşiv kuralları (özet, ayrıntı §8–§9, §13.1)
- Beyanname/tahakkuk/ihbarname → `Y:\{YKlasör}\{dönem yılı}\{TÜR}\{etiket}_{TÜR}_{BEYANNAME|TAHAKKUK|IHBARNAME[n]}_{dd.MM.yyyy_HH_mm_ss}[_DÜZ|_DÜZ2…].pdf`
  - Etiket: `09_EYLUL`, çeyrek `03_DONEM`, yıllık `2025`. Düzeltme sırası: aynı VKN+tür+dönemde yükleme zamanına göre 2. = `_DÜZ`, 3. = `_DÜZ2`.
  - Zaman: KDV1/KDV2/KDV4'te beyanname PDF'inin 1. sayfasındaki **Onay Zamanı** (`Ayarlar.OnayZamaniTurleri`), diğerlerinde liste yükleme zamanı.
- SGK → `X:\{yıl}\{XKlasör}\{işyeri}\{AA}_{AY}_{BT}_{KANUN}_{ASIL|EKn|İPTALn}_{TAHAKKUK|HIZMET_LISTESI}_{yyyyMMddHHmmss}.pdf`
  - İşyeri klasörü: PDF'teki sicil (anahtar = 1. hane + 6–26. haneler, §11.1) → `isyerleri.json` (X arşivinden ve Luca'dan öğrenilir).
- Klasör adı Luca "Kısa Ad"dan: X = boşluk→`_`; Y = ayrıca Ş→S Ğ→G Ü→U Ö→O Ç→C, İ→`_`.
- Eski adlar (damgasız / `yyyyMMddHHmmss`) önek sayımıyla "var" sayılır; `_MAHSUP`, `_ÖD`, `T ÖD`, `B.xls` gibi elle eklenenler sayılmaz.

## Kullanıcı kararları
Tam otomatik (giriş sonrası listele → eksikleri indir) · açılışta son 31 günün eksikleri · yalnız Onaylandı · yeni müşteriye klasör aynı kuralla ·
tek bilgisayar · SGK belgeleri e-Beyanname'den (e-Bildirge değil) · Luca'dan işyeri alma özelliği var.

## Durum — 2026-11-16 sonu
Bugün yapılanlar:
- GİB akışı keşfedildi ve belgelendi (DVD girişi, e-Beyanname SSO, liste/alt liste/PDF komutları, uzun aralık istisnası, arşiv kuralları,
  eski Luca Entegrasyon programının analizi, e-Beyan sistemi ve resmi API incelemesi).
- Uygulama yazıldı: müşteri Excel içe aktarma, X arşivinden işyeri öğrenme, Luca'dan işyeri aktarma, listeleme, eksik indirme, arşiv taraması.
- 1. deneme (test klasörleri `D:\dev\y`, `D:\dev\x`; ayarlarda YArsivKok/XArsivKok bunlara çevrili): 797 PDF, 0 hata.
  Y arşiviyle karşılaştırma → KDV saat farkı bulundu → onay zamanı kuralı eklendi → 113 karşılaştırılabilir dosyanın 107'si birebir.
- 2. ve 3. denemede **GİB kısıtlaması** yaşandı (aşağıda). Kısıtlama algılama + bekleme + durdurma ve oturum zaman aşımı algılama eklendi.
  Bu sırada çıkan hata düzeltildi: normal yanıtlarda boş `<EYEKSERROR></EYEKSERROR>` var; yalnız içi doluysa oturum düşmüş sayılır.
- Repo anonimleştirildi, ilk commit GitHub'a gönderildi. 78/78 test geçiyor.

### Son yaşanan durum: GİB görüntüleme kotası (§12.1–12.2)
- Boş yanıt (0 bayt) = kısıtlama. Bugün toplam 1187 PDF indirildi; ilk engel ~846 indirmede geldi.
  19:58'de son 60 dk'da neredeyse hiç istek yokken yine engel → **saatlik/dakikalık değil, günlük kota** gibi (~850/gün tahmini, doğrulanmadı).
  Kota aşılınca GİB arada bir 60–120 PDF'e izin veriyor, sonra yine kesiyor.
- 3. tur 20:36'da "Yarıda kesildi (GİB kısıtlaması)" ile durdu (199 indirildi). Kalanlar "Eksikleri indir" ile alınabilir; var olanlar atlanır.
- Mevcut davranış: 2 boş yanıt → `KisitlamaBeklemeleriDk` (2,5,10 dk) bekle, aynı satırla devam; tükenince dur.
  Sorun: kota dolduysa bu beklemeler boşa (17 dk) ve tam otomatik akış aksıyor.

### Yarın — önce yapılacak
1. Program açılıp girişte **kotanın sıfırlanıp sıfırlanmadığını** gör (sıfırlandıysa günlük kota tahmini doğrulanır).
2. Kullanıcıya önerilen, **henüz onaylanmamış/uygulanmamış** plan (kullanıcı "şimdilik uygulama" dedi):
   - Günlük görüntüleme bütçesi (sayaç kalıcı, Ayarlar'da, öneri ~700); dolunca temiz dur, "yarın devam" de.
   - Öncelik: en yeni evraklar önce (şimdi yükleme zamanına göre eskiden yeniye gidiyor).
   - Arşiv taramasını günlere yay (her gün kalan bütçeyle ilerlesin).
   - Engelde uzun bekleme yerine tek kısa deneme sonrası dur.
   - Sonra değerlendirilebilir: KDV için e-Beyan (ayrı kota olabilir, bilinmiyor), resmi e-Beyan API'si (başvuru gerekir, PDF listesi doğrulanmadı).
3. Ertelenen: istek aralığını 1500 → 1100 ms düşürme (kota bulgusu nedeniyle anlamsız).

## Durum — 2026-11-18
- 4. tur: 471 indirildi, 0 hata, kısıtlama yok → **kota günlük sıfırlanıyor** (§12.3). Gerçek arşivle 228 dosyanın 214'ü birebir.
- Düzeltildi: pencere dışı asıl beyannamesi olan düzeltmeler artık `_DÜZ` olarak indiriliyor (`Indirici.EtkinSira`, §9.2) —
  önceden "zaten var" sanılıp kaçırılıyordu. SGK listesindeki "Vergi" satırı uyarısı kaldırıldı. 1 sn kuralı POST'ta da var (§12.3).
- Günlük bütçe / öncelik planı hâlâ **uygulanmadı** (kullanıcı onayı bekliyor).
- `GercekDuzeltmeRaporu` testi: D:\dev\y + Y:\ varken yeni kuralla düzeltme sayılanları listeler (çıktı müşteri klasör adı içermez; dosya adı içerir — paylaşma).

### Açık işler
- X (SGK) arşiviyle karşılaştırma yapılmadı (Ekim SGK'ları arşivde yoktu; ağ sürücüsü de bağlantısızdı) — Eylül ile denenmeli.
- SGK belge türü eşlemesi (01/02) gerçek verilerle doğrulanmadı.
- Luca'dan işyeri aktarımı uygulamada canlı denenmedi.
- Test indirmesinde bir müşterinin işyeri klasörü müşteri adıyla aynı çıktı (`{yıl}\{müşteri}\{müşteri}`) — kaynağı (X arşivinden öğrenme?) incelenmeli.
- Gerçek arşive (Y:\, X:\) geçmeden önce ayarlardaki test klasörleri geri alınmalı.
