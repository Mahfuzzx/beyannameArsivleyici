# GİB Evrak İndirici – Keşif Notları

Proje klasörü: `C:\dev\GibEvrakIndirici` · Teknoloji: C# WinForms + WebView2

## 1. Dijital Vergi Dairesi (DVD) girişi
- Sayfa: `https://dijital.gib.gov.tr/portal/login`
- Alanlar: TCKN/VKN/Kullanıcı Kodu, Şifre, Doğrulama Kodu (captcha)
- `GET  /apigateway/captcha/getnewcaptcha` → captcha resmi
- `POST /apigateway/auth/tdvd/login` → giriş
- `GET  /apigateway/auth/tdvd/user-info`
- Oturum: `sessionStorage["token"]` (çerez yok)
- Giriş kullanıcı tarafından WebView2 içinde elle yapılır (şifre saklanmaz, captcha otomatik çözülmez)

## 2. DVD → e-Beyanname geçişi (SSO)
- Panel > "Geçiş Yapılabilecek Uygulamalar" > e-Beyanname → onay penceresi
  ("açık e-Beyanname oturumu varsa sonlandırılır") → ONAYLA
- `GET /apigateway/auth/tdvd/ebyn-login` → tek seferlik TOKEN
- Ardından `window.open("https://ebeyanname.gib.gov.tr/dispatch?cmd=LOGIN&TOKEN=...", "eBeyanname")`
- WebView2: `CoreWebView2.NewWindowRequested` yakalanır, `Handled=true`, aynı WebView'da `Navigate(e.Uri)`

## 3. e-Beyanname oturumu ve TOKEN
- Tüm istekler: `https://ebeyanname.gib.gov.tr/dispatch`
- Çerez yok; TOKEN sayfada JS ile tutulur: `getTOKEN()` / `setTOKEN(t)`
- **TOKEN her POST yanıtında yenilenir!** Yanıt XML: 
  ```xml
  <SERVICERESULT>
    <TOKEN>yeni_token</TOKEN>
    <SERVERERROR>hata metni (boşsa OK)</SERVERERROR>
    <EYEKSERROR></EYEKSERROR>
    <HTMLCONTENT> ...html... </HTMLCONTENT>
  </SERVICERESULT>
  ```
  → Her POST'tan sonra `<TOKEN>` okunup bir sonraki istekte kullanılmalı.
  → Uygulama istekleri WebView içinde (`ExecuteScriptAsync` + fetch) atarsa `setTOKEN()` ile sayfayı da güncel tutmalı.
- POST gövdesi: `application/x-www-form-urlencoded`, sonuna `&TOKEN=...`

## 4. Beyanname listesi (Beyanname Ara)
`POST cmd=BEYANNAMELISTESI` + filtreler (her filtre kendi `sorguTipiX=1` bayrağıyla açılır):

| Bayrak | Parametreler | Anlam |
|---|---|---|
| `sorguTipiN=1` | `vergiNo` | VKN |
| `sorguTipiT=1` | `tcKimlikNo` | TCKN |
| `sorguTipiB=1` | `beyannameTanim` (KDV1, MUHSGK, …) | Beyanname türü |
| `sorguTipiP=1` | `donemBasYil, donemBasAy, donemBitYil, donemBitAy` | Vergilendirme dönemi |
| `sorguTipiV=1` | `vdKodu` (ör. 034262) | Vergi dairesi |
| `sorguTipiZ=1` | `baslangicTarihi, bitisTarihi` (yyyyMMdd) | Yükleme tarihi — **en fazla 1 ay** |
| `sorguTipiD=1` | `durum` 0=Hatalı 1=Onay bekliyor 2=Onaylandı 3=İptal | Durum |

- Sayfalama: aynı parametreler + `&pageNo=N`, sayfa başı 25 kayıt. Toplam: `"1 - 25 / 348"`, son sayfa `digerSayfayaGecis(...,1,14,...)`
- Satır: `<tr id="row{OID}" title="TUBITAK  18.10.2026 - 16:24:25">` sütunlar:
  Tür | TCKN/VKN | Unvan (tam ad `td title`) | VD | Dönem `09/2026-09/2026` | Şube | Yükleme zamanı | Durum | SGK
- OID URL-encode edilmiş haliyle gelir (`9Ga%7Bzl...`) → aynen geri gönderilir.
- Satırdaki onclick'ler:
  - `beyannameGoruntule('{bynOid}',false,false)`
  - `tahakkukGoruntule('{bynOid}','{thkOid}',false,false)`
  - `ihbarnameleriGetirInDetay('{bynOid}',false)` → ihbarname var
  - `beyannameSGKBildirimleriGoster('{bynOid}', false)` → MUHSGK SGK bildirimleri var
  - `beyannameMesajGosterInDetay(...)` → mesaj/hata

## 5. Alt listeler
- İhbarnameler: `POST cmd=IHBARNAMELERIGETIR&beyannameOid=X`
  → `ihbarnameGoruntule('{bynOid}','{thkOid}','{tarhOid}',false,false)` + açıklama (ör. "V.U.K 344 Vergi Ziyaı")
- SGK bildirimleri: `POST cmd=THKESASBILGISGKMESAJLARI&beyannameOid=X`
  → her bildirim için `sgkTahakkukGoruntule('{bynOid}','{sgkThkOid}',..)` ve `sgkHizmetGoruntule(...)`
  → toplu: `sgkTopluTahakkukGoruntule('{bynOid}','oid1;oid2',..)`
  → Hatalı bildirimlerde PDF düğmesi yok.

## 6. PDF indirme (GET, TOKEN değişmez)
`GET dispatch?cmd=IMAJ&subcmd=...&TOKEN=...` (`&inline=true` yoksa attachment gelir)

| Evrak | subcmd + parametreler | Örnek dosya adı |
|---|---|---|
| Beyanname | `BEYANNAMEGORUNTULE&beyannameOid=` | |
| Tahakkuk | `TAHAKKUKGORUNTULE&beyannameOid=&tahakkukOid=` | `TUBITAK_THK.pdf` |
| İhbarname | `IHBARNAMEGORUNTULE&beyannameOid=&tahakkukOid=&tarhOid=` | `034262_<VKN>_KDV1_39_01052024-31052024.xml_IHB.pdf` |
| SGK tahakkuk | `SGKTAHAKKUKGORUNTULE&beyannameOid=&sgkTahakkukOid=` | `tahakkukfis.pdf` |
| SGK hizmet listesi | `SGKHIZMETGORUNTULE&beyannameOid=&sgkTahakkukOid=` | `hizmetfis.pdf` |
| SGK toplu tahakkuk | `PDF_SGK_TOPLU_TAHAKKUK_GORUNTULE&beyannameOid=&thkOidler=a;b` | |

- Arşivden görüntüleme için `&ARSIV=T`
- **Kural: iki görüntüleme isteği arasında en az 1 sn** — aksi halde HTML hata: "İki görüntüleme isteği arası en az 1 sn olabilir." → 1,5 sn bekleme + yanıtın `%PDF` ile başladığını kontrol et, değilse tekrar dene.

## 7. ASLA ÇAĞRILMAYACAK komutlar (geri alınamaz işlemler)
`BEYANNAMEONAY`, `BEYANNAMEOZELONAY`, `BEYANNAMEIPTAL`, `MUHSGKMANUELONAYGONDER`,
`MUHSGKMANUELIPTALGONDER`, `MUHSGKMANUELKONTROLGONDER`, `HATALITAHAKKUKSAYISI*`, paket gönder/iptal.
Uygulama yalnızca: `BEYANNAMELISTESI`, `IHBARNAMELERIGETIR`, `THKESASBILGISGKMESAJLARI`, `IMAJ` kullanır (beyaz liste).

## 8. Arşiv düzeni (mevcut ofis düzeni — aynen korunacak)

### 8.1 Beyanname + tahakkuk → `Y:\` (= `\\SUNUCU\BEYANNAMELER\LUCA`)
```
Y:\{MÜKELLEF_KLASÖRÜ}\{DÖNEM_YILI}\{TÜR}\{dosya}.pdf
```
- `{DÖNEM_YILI}` = vergilendirme döneminin yılı (yükleme yılı değil): 2025 Gelir → `2025\GELIR\..._10.03.2026_...`
- `{TÜR}` = e-Beyanname kodu: KDV1, KDV2, KDV4, MUHSGK, GGECICI, KGECICI, POSET, GELIR, KURUMLAR, FORMBA, FORMBS, KONAKLAMA, MTV …
- Dosya adı:
  - Aylık:    `{AA}_{AY}_{TÜR}_{BEYANNAME|TAHAKKUK}_{dd.MM.yyyy_HH_mm_ss}.pdf` → `09_EYLUL_KDV1_TAHAKKUK_27.10.2026_15_06_52.pdf`
  - 3 aylık:  `{NN}_DONEM_{TÜR}_{...}_{zaman}.pdf` → `03_DONEM_KGECICI_BEYANNAME_12.11.2026_15_48_49.pdf` (GGECICI, KGECICI, POSET, 3 aylık MUHSGK)
  - Yıllık:   `{YIL}_{TÜR}_{...}_{zaman}.pdf` → `2025_KURUMLAR_TAHAKKUK_22.04.2026_18_22_38.pdf`
  - `{AY}` ASCII büyük harf: OCAK SUBAT MART NISAN MAYIS HAZIRAN TEMMUZ AGUSTOS EYLUL EKIM KASIM ARALIK
  - `{zaman}` = e-Beyanname listesindeki **yükleme zamanı** ("27.10.2026 - 15:06:52") — beyanname ve tahakkuk aynı damgayı taşır. Aynı dönemin düzeltme beyannamesi farklı zamanla yan yana durur (üzerine yazmaz).
  - Eski dosyalarda `yyyyMMddHHmmss` damga veya damgasız ad da var → "zaten indirilmiş mi" kontrolünde üçü de tanınmalı.
- **Vergi ihbarnamesi**: aynı tür klasörüne, beyanname/tahakkuk ile aynı kalıp ve aynı yükleme zamanıyla:
  `{AA}_{AY}_{TÜR}_IHBARNAME_{zaman}.pdf` → `05_MAYIS_KDV1_IHBARNAME_18.10.2026_16_24_25.pdf`
  Bir beyannamede birden fazla ihbarname varsa (farklı tarhOid): `_IHBARNAME_`, `_IHBARNAME2_`, `_IHBARNAME3_` …
- Elle eklenen ekler (uygulama dokunmaz): `_DÜZ`, `_DÜZ2`, `_MAHSUP`, `_MAHSUP1`, `_ÖD`, `{AA} {YIL} T ÖD.pdf` (ödeme dekontu), eski `2024 B.pdf` vb.

### 8.2 SGK tahakkuk + hizmet listesi → `X:\` (= `\\SUNUCU\BEYANNAMELER\SGK`)
```
X:\{YIL}\{MÜKELLEF_KLASÖRÜ_TR}\{İŞYERİ}\{dosya}.pdf
```
- Yıl en üstte (Y:\'den farklı!), sonra mükellef, sonra işyeri/şube klasörü (MERKEZ, KARTAL, SUNGURLU, BEYKOZ, FABRİKA …)
- Dosya adı: `{AA}_{AY}_{BELGE_TÜRÜ 2 hane}_{KANUN 5 hane}_{ASIL|EK1|İPTAL1}_{TAHAKKUK|HIZMET_LISTESI}_{yyyyMMddHHmmss}.pdf`
  → `09_EYLUL_01_05510_ASIL_TAHAKKUK_20261011164618.pdf`
  - Belge türü: 01, 02 … · Kanun: 05510, 00000, 06111 … (SGK bildirim satırındaki bilgiden: "Asıl, TÜM SİG.KOLLARI/..., 05510-Say.Kan.MYO, 09 / 2026, {işyeri sicil}")
- Elle eklenenler: `{AA} {YIL} B.xls`, `{AA} {YIL} T ÖD.pdf`, `_MAHSUP`, `_SPEC_HARICI`

### 8.3 Mükellef listesi ve klasör adları
**Kaynak: Luca müşteri listesi Excel'i** (`musteri_listesi_filtre_raporu_*.xlsx`) — müşteriler uygulamaya bununla alınır.
- Satır 1: başlık "MÜŞTERİ LİSTESİ", satır 2: sütun adları, veri 3. satırdan itibaren (2026-11-16 itibarıyla 231 müşteri)
- Sütunlar: A **Kısa Ad** · B Uzun Ad · C Vergi Dairesi (ör. "KÜÇÜKYALI VERGİ DAİRESİ") · D **Vergi No** (metin, baştaki 0 korunur) · E **T.C. Kimlik No** · F Açıklama · G Kuruluş Tarihi (Excel seri tarih)
- Kısa Ad 6–10 karakter, Türkçe karakterli, boşluk içerebilir (`ÖRNEŞ GIDA`, `ÖRNEK AY`, `OTOÖRNEK`)

**Klasör adı = Kısa Ad'dan türetilir:**
- X:\ → boşluk → `_` : `ÖRNEŞ GIDA` → `ÖRNEŞ_GIDA`, `ÖR TEMİZLİ` → `ÖR_TEMİZLİ`
- Y:\ → boşluk → `_`, Ş→S Ğ→G Ü→U Ö→O Ç→C, **İ → `_`**: `ORNES_GIDA`, `OR_TEM_ZL_`, `AHMETKAC`, `G_YORNEK`
- Doğrulama: Y:\'deki 83 klasörden 80'i, X:\'teki 75 klasörden 71'i bu kuralla birebir tutuyor. Tutmayanlar:
  - Y:\ `GMSİ` (tür klasörü), `ESKIMUS1`, `ESKIMUS2` (listede yok — eski/pasif müşteri)
  - X:\ `DOĞRUL ÖRNEK`, `ÖRNEK KAY` (elle açılmış, `_` yerine boşluk), `ÖRNEK_GİYİM`, `ÖRN-ARI` (listede yok)
- Y:\ adlarında çakışma yok.

**e-Beyanname satırı ↔ müşteri eşleşmesi:** listedeki TCKN/VKN sütunu → Excel'de D (Vergi No) **veya** E (TCKN) ile eşleşir (şahıslarda e-Beyanname TCKN gösteriyor).
- VKN ve TCKN'si boş olanlar: `ÖRNEKYAPI`, `ÖRNEK MUH`, `Z ÖRNEK GÜV` → eşleşmez, uyarı verilir.
- Listede olmayan mükellef → "eşleme yok" uyarısı, indirilmez (yeni Excel içe aktarılınca çözülür).
- Elle düzeltme imkânı: eşleme tablosunda bir müşteriye farklı X/Y klasör adı yazılabilmeli (ör. `ÖRNEK KAY`).

- İşyeri klasörü (X:\) için **işyeri sicil no → klasör** eşlemesi gerekir (Excel'de yok).

### 8.4 Açık sorular
- e-Beyanname'deki SGK PDF'leri ile X:\'deki dosyalar aynı mı (X:\ adları e-Bildirge'den mi geliyor)?

## 9. Davranış kararları (2026-11-16)
1. Açılışta: son 1 ayın **eksikleri** listelenir. Ayrıca arşivde eksik taraması yapılır.
2. Tam otomatik: giriş sonrası listeleme + eksiklerin indirilmesi kullanıcı onayı beklemeden başlar.
3. Yeni mükellef: klasörler aynı kurallarla (Kısa Ad → X/Y adı) otomatik oluşturulur.
4. Sadece **Onaylandı** durumundaki beyannameler.
5. Tek bilgisayar (eşzamanlı yazma kilidi gerekmez).
6. Giriş (captcha) elle; giriş sonrası her şey tam otomatik (listele → indir → arşiv taraması → özet).

### 9.1 Uzun aralık sorgu istisnası (2026-11-16 canlı test edildi)
- `sorguTipiZ` (yükleme tarihi) **her zaman zorunlu** ("Yükleme Tarih Aralığı bilgisi girilmelidir").
- Normalde yükleme aralığı ≤ 1 ay. **İstisna:** `sorguTipiN` (VKN) **veya** `sorguTipiT` (TCKN) **+** `sorguTipiB` (tür) **+** `sorguTipiP` (dönem) birlikte verilirse uzun yükleme aralığı kabul ediliyor.
  | Deneme | Sonuç |
  |---|---|
  | B+P+Z 1 yıl (VKN yok) | ✗ 1 ay sınırı |
  | N+Z 1 yıl (tür/dönem yok) | ✗ |
  | N+P+Z 1 yıl (tür yok) | ✗ |
  | **N+B+P+Z 2026 tüm yıl** | ✓ 10 kayıt (01–10/2026) |
  | **T+B+P+Z 2026 tüm yıl** | ✓ 11 kayıt |
  | **N+B+P 2025-01..2026-12, Z 2025-01-01..2026-12-31** | ✓ 22 kayıt (yıl aşan da çalışıyor) |
  | N+B+P, Z yok | ✗ Z zorunlu |
- Formdaki "başlangıç/bitiş yılı aynı olmalı" kontrolü JS'te yorum satırına alınmış (artık uygulanmıyor).
- **Strateji:**
  - Günlük/son ay: `sorguTipiZ` son 1 ay + `durum=2`, tüm sayfalar (~14 sayfa).
  - Tüm ofis arşiv taraması: aylık Z dilimleri (yıl başına 12 sorgu × ~14 sayfa) — müşteri × tür sorgusundan daha az istek.
  - Tek müşteri derin tarama (müşteri ekranından): N/T + her tür + çok yıllık dönem → müşteri×tür başına 1 sorgu.

### 9.1b Arşiv beyanname araması
- `cmd=ARSIVBEYANNAMESORGU` formu → `cmd=ARSIVBEYANNAMELISTESI`, alanlar aynı (+ `grupSayi`). Eski yıllar için; görüntülemede `&ARSIV=T`. Henüz denenmedi.

### 9.2 Düzeltme beyannameleri
- Listede düzeltme ayrı işaretlenmiyor: aynı dönem iki satır, farklı yükleme zamanı (ör. ÖRNEK MÜKELLEF KDV1 01/2026: 17.02.2026 17:20:28 ve 18.02.2026 12:59:29). Satır `title` = gönderilen dosya adı + zaman ("2026_OCAK_KDV(AYLIK).xml 17.02.2026 - 17:20:28" veya "TUBITAK …").
- PDF dosya adı (content-disposition) da ikisinde aynı: `2026_OCAK_KDV(AYLIK).xml_BYN.pdf`.
- Aynı mükellef + tür + dönem için birden fazla onaylı beyanname → yükleme zamanına göre sırala:
  1. yükleme = normal ad, 2. = `_DÜZ`, 3. = `_DÜZ2`, 4. = `_DÜZ3` … (kullanıcı kararı)
  Örnek: `01_OCAK_KDV1_BEYANNAME_18.02.2026_12_59_29_DÜZ.pdf` — ek, zaman damgasından sonra, `.pdf`'ten önce.
  (Arşivdeki eski elle verilmiş adlarda damga yok: `01_OCAK_KDV1_BEYANNAME_DÜZ.pdf` → var-mı kontrolünde bu da tanınmalı.)
- **Pencere dışı asıl (2026-11-18 bulundu, düzeltildi):** Liste son 31 günü gösterdiği için eski bir dönemin bu ay verilen düzeltmesi tek satır
  görünür → sıra 0 sanılır → arşivdeki eski asıl dosya yüzünden "zaten var" denip **hiç indirilmezdi**.
  Çözüm `Indirici.EtkinSira`: sıra = max(liste sırası, arşivde adındaki damgası bu satırın yüklemesinden ≥10 dk eski beyanname dosyası sayısı)
  (`ArsivDenetci.OncekiYuklemeSayisi`; `dd.MM.yyyy_HH_mm_ss`, `yyyyMMddHHmmss`, yalnız tarih `dd.MM.yyyy` = gün olarak; damgasız sayılmaz).
  Gerçek arşivde denendi: 149 beyannameden 2'si düzeltme sayıldı, ikisi de gerçek (2024 KDV1 düzeltmesi + 2020 KURUMLAR); yanlış pozitif yok.
  2020 KURUMLAR'da ofis düzeltmeyi elle `_DÜZELTME.pdf` adıyla kaydetmiş → önek sayımıyla "var", yeniden indirilmez.

### 9.3 SGK işyeri klasörü — PDF'ten öğrenme
- İşyeri adları Luca'dan geliyor, dışa aktarılamıyor.
- X:\ PDF'leri metin içeriyor (Flate sıkıştırmalı, latin-1/Windows-1254); **"İşyeri Sicil No"** okunabiliyor
  (ör. ÖRNEŞ GIDA MERKEZ: `1234 2 5678 01 01000001 034 011` benzeri, ÖRNEK GRUP KARTAL: `2345 4 6789 01 01000002 034 022`).
- Çözüm: ilk kurulumda X:\{yıl}\{müşteri}\{işyeri}\ altındaki tahakkuk PDF'lerinden sicil no okunup **sicil → işyeri klasörü** tablosu otomatik kurulur.
- ⚠ e-Beyanname'den inen SGK PDF'inde sicil okuması tarayıcı içinde doğrulanamadı (test aracı sınırı) → uygulamada PdfPig ile ilk iş doğrulanacak. Okunamazsa yedek: SGK bildirim satırındaki sicil bilgisi ("…, 09 / 2026, 01234567890123000") ile eşleme.
- Yeni indirilen SGK PDF'i önce geçici klasöre iner, içinden sicil okunur, tabloya göre doğru işyeri klasörüne taşınır.
- Bilinmeyen sicil: müşterinin tek işyeri varsa / hiç yoksa `MERKEZ`; aksi halde bir kez sorulur ve hatırlanır.
- .NET tarafında PDF metni için kütüphane: UglyToad.PdfPig.

### 9.4 Gerçek arşive bağlanınca eski dosyalar tanınıyor mu? (2026-11-18 denetimi)
- **Müşteri klasörleri:** bu birimin 129 müşterisinden 80'inin Y:\ klasörü kuraldan bulunuyor; kalan 49'un arşivde hiç klasörü yok
  (program aynı kuralla açar). Y:\'de hiçbir müşteriye uymayan 3 klasör var (1 tür klasörü + 2 listede olmayan eski müşteri);
  hiçbir müşterinin klasörü başka adla durmuyor. X:\ son 3 yıl: uymayan klasör yok.
- **Beyanname/tahakkuk/ihbarname (Y):** test indirmesindeki bu birime ait dosyaların arşivde karşılığı olanların hepsi önek sayımıyla tanınıyor
  (KDV1 104, GGECICI 78, KGECICI 28, KDV2 14, KDV4 2, KURUMLAR 2). Tanınmayanlar gerçekten yeni: Ekim MUHSGK (72, eski program henüz indirmemiş)
  ve 2024 KDV1 düzeltmesinin ihbarnamesi.
- **SGK belge türü HATASI bulundu ve düzeltildi:** arşivde `02_00000_…` (2026'da 204, 2025'te 285 dosya) = **SGDP** (sosyal güvenlik destek primi,
  emekli çalışan). Program belge türünü e-Beyanname bildirim açıklamasından tahmin ediyordu ("DESTEK"/"SGDP" arıyordu) → bizim 13 `01_00000`
  dosyanın 10'u aslında 02 idi. Gerçek arşivde eski programın 02 dosyaları tanınmayacak, yanlış adla kopya inecekti.
  Çözüm: belge türü **tahakkuk PDF'inden** (`SgkPdfOkuyucu.Oku`): metinde boşluksuz "DESTEKPR"/"SGDP" → 02, yoksa 01.
  Arşivde doğrulandı: gerçek SGK tahakkuklarında 1135/1135 (2026: 453, 2025: 682) uyuşuyor; uyuşmayan 11 dosya elle konmuş `_MAHSUP`
  (KDV iade dilekçesi). Hizmet listesi PDF'inde bu ifade yok → karar tahakkuktan verilir, hizmet listesi aynı adı alır.
  Akış: açıklamaya göre "var mı" bakılır → yoksa tahakkuk indirilir → PDF'teki tür farklıysa adlar yeniden hesaplanır, öyle varsa yazılmaz.
  Fark olduğunda açıklama metni log'a yazılır (açıklamadan kural çıkarmak için). Test klasöründeki (D:\dev\x) yanlış adlı 10 dosya eski kuraldan kaldı.

### 9.5 Müşteri taraması sekmesi (2026-11-18)
- Seçili müşteri(ler) × seçili türler × **vergilendirme dönemi** aralığı. §9.1 istisnası kullanılır: VKN/TCKN + tür + dönem (P) +
  yükleme aralığı (Z = dönem başı → bugün). Her müşteri × tür ayrı sorgu (tüm sayfalar); sonuç başlangıç ayı aralıkta olan dönemlerle sınırlanır.
- Kimlik: TCKN varsa TCKN (şahıslar e-Beyanname'de TCKN ile görünür), yoksa VKN.
- Önerilen türler: KDV1, MUHSGK, GGECICI, KGECICI + müşterinin Y arşivinde aralıktaki yıllarda klasörü olan türler (`MusteriTaramasi.VarsayilanTurler`).
- Liste birim filtresine uyan müşterileri gösterir. Sonuç ana tabloya gelir; "Listele ve eksikleri indir" ya da sonra "Eksikleri indir".
- Uzun aralıkta düzeltme sırası daha doğru çıkar (asıl ve düzeltmeler aynı listede).
- **İlk canlı deneme (2026-11-18 10:55):** 1 şirket, 01/2025–11/2026, 5 tür → 5 sorgu 8 sn, 101 onaylı beyanname; indirme 262 PDF, 0 hata.
  (~23 aylık yükleme aralığı kabul edildi; 3+ yıllık aralık henüz denenmedi.)
  Gerçek arşivle: Y'de tüm evraklar tanınıyor (KDV1 98, KDV2 44, MUHSGK 42, KGECICI 14, KURUMLAR 2); yeni olanlar Ekim 2026 MUHSGK.
  **X SGK: 68 dosya tanınıyor (02 = SGDP dahil), başka işyeri klasörüne düşen yok**; 4 yeni = Ekim 2026. → SGK adlandırması doğrulandı.
  Müşterinin 2025–26'da 29 düzeltmesi var; arşivde bunların bir kısmı **eksik** (ör. bir dönemde GİB'de 5 sürüm, arşivde yalnız asıl) →
  gerçek arşive geçişte program bu eksik düzeltmeleri `_DÜZn` ile tamamlayacak. Arşivde elle damgasız `_DÜZ`/`_DÜZ2` ya da `_DÜZ`'süz ama
  farklı damgalı kaydedilmiş düzeltmeler önek sayımıyla tanınıyor.
- SGDP bildirim açıklaması: `Asıl, SOS.GÜV.DES PRİM BİLDİRGE, Kanun türü yoktur, 09 / 2025, {sicil}, Yasal süresinde verilme` →
  `SgkAdParcalari` artık bunu 02 tanıyor (ön kontrol); kesin karar yine PDF'ten.

### 9.6 Düzeltme sürümleri PDF içeriğiyle tanınır (2026-11-18, gerçek arşivde kopya bulunduktan sonra)
- **Olay:** gerçek arşivde 1 müşteri için 01/2023–11/2026 müşteri taraması (~4 yıllık yükleme aralığını GİB kabul etti, 277 beyanname,
  104 PDF; ~900. PDF civarında 5 kez kısa kısıtlama, 2 dk beklemelerle aşıldı). Ardından 11:10'daki turla birlikte inen dosyalar arşivdeki
  eşleriyle **beyanname PDF'indeki onay zamanı** üzerinden karşılaştırıldı: **8 beyanname (+ 8 tahakkuk) kopya**, 79 beyanname gerçekten yeni
  (2023'te arşivde hiç olmayan aylar, eksik düzeltmeler).
- **Neden:** ofis düzeltmelerin hepsini kaydetmemiş, kaydettiklerine elle `_DÜZ`, `_DÜZ2` (ya da XML dosya adı, damgasız ad) vermiş.
  Örn. GİB'de v1 v2 v3 v4, arşivde `…BEYANNAME_25.01.2024…` (v1), `_DÜZ` (v2), `_DÜZ2` (= v4!). Önek sayımı "3 dosya var → 4. sürüm eksik"
  diyerek v4'ü `_DÜZ3` adıyla bir daha indirdi. Tersi de mümkündü (eksik sürüm "var" sanılabilirdi).
- **Kopyalar:** silinmedi; kullanıcı onayıyla her biri kendi klasöründeki `_KOPYA` alt klasörüne taşındı (16 dosya; liste
  `%APPDATA%\GibEvrakIndirici\log\kopyalar_2026-11-18.txt`, repoda değil). Program alt klasörlere bakmaz.
- **Yeni kural (`Indirici.BeyannameEvraklariAsync`):** tam ad yoksa ve klasörde aynı önekli dosya varsa GİB'den beyanname PDF'i alınır,
  onay zamanı okunur, arşivdeki aynı önekli her beyanname PDF'inin onay zamanıyla (önbellekli) karşılaştırılır:
  - eşleşen var → sürüm var (adı ne olursa olsun). Tahakkuk: eşin `TAHAKKUK` karşılığı ya da tahakkuk sayısı ≥ beyanname sayısı ise var;
    yoksa eşin adıyla indirilir (çift düzgün kalır). İhbarname de eşin karşılığıyla aranır.
  - hepsi okundu, eşleşen yok → sürüm kesin eksik → yalnız tam adla bakılır, indirilir.
  - bir PDF okunamadıysa → eski önek sayımı (yedek).
- **Maliyet:** adı farklı her sürüm için bir kez 1 beyanname PDF isteği. Satırın tüm evrakı doğrulanınca `tamamlanan.json`'a "SATIR"
  yazılır; sonraki çalıştırmalarda o satır için GİB'e hiç istek gitmez.
- Tablodaki ✓/eksik gösterimi (`Indirici.Durum`) hâlâ önek sayımıyla (yalnız görüntü; indirme kararı yeni kuralla).

### 9.7 Arşiv dizini ve GİB'e gitmeden sürüm eşleştirme (2026-11-18)
- PDF içerikleri (gerçek dosyalarda bakıldı): beyanname PDF'i yalnız **onay zamanı**nı (saniyesiyle) taşır, yükleme zamanını taşımaz;
  tahakkuk PDF'i yalnız tarihleri (onay günü, vade) ve tutarları; SGK PDF'leri sicil, yıl/ay, belge kabul tarihi, fiş no, destek primi.
- **Dizin** (`ArsivDizini`, `%APPDATA%\GibEvrakIndirici\arsiv-dizini.json`): arşivdeki beyanname PDF'lerinin onay zamanı; her dosya bir kez okunur,
  boyut/yazma zamanı değişirse yeniden. Ayrı bir tarama yok: bir klasör ilk kez işlendiğinde dolar (ilk geçiş yavaş, sonrası önbellekten).
- **Aralık kuralı** (`Indirici.AraliktaEslesen`): bir sürümün onayı kendi yüklemesinden sonra, sonraki sürümün yüklemesinden önce olur →
  arşivdeki dosyanın onay zamanı [yükleme − 1 dk, min(sonraki yükleme, yükleme + 1 gün)) aralığındaysa o sürümdür; GİB'den PDF istenmez.
  Belirsiz (aday PDF okunamadı ya da sonraki sürüm < 2 saat sonra yüklendi) → GİB'den beyanname PDF'i alınıp onay zamanı birebir karşılaştırılır (§9.6).
  Aralıkta aday yoksa sürüm eksik sayılır; PDF zaten indirileceği için birebir karşılaştırma da yapılır.
- Sonraki sürümün yükleme zamanı aynı listedeki gruptan (`DuzeltmeSiralayici.SonrakiYuklemeler`) gelir; listede yoksa üst sınır yükleme + 1 gün.
- Sağlık raporu (arşivdeki kopyalar, hiçbir GİB sürümüne uymayan dosyalar, eksik sürümler) sonraki aşama.
- Canlıda doğrulandı (§9.8 sonrası tarama): 1 müşteri, 01/2021–11/2026 → 603 "zaten var", 84 gerçekten eksik (ör. 01.10.2026'da toplu verilmiş
  eski dönem KDV1 düzeltmeleri, arşivde hiç olmayan FORMBA/BS ayları), indirme sonrası klasörde aynı sürümden iki dosya yok.

### 9.8 Ad düzeltme — eski adlı dosyaları PDF içeriğiyle standart ada çevirme (2026-11-18)
Sorun: eski programların / elle verilmiş adlar (`04_NISAN_{KISAAD}_NISAN_KDV(AYLIK).xml_BYN`, `…_KDV1_37_01112023-30112023_BYN_4`,
`2025 02 B DÜZ`, `2024 T`, `…_BEYANNAME_{kısaltma}_{AY}_KDV_AYLIK_.XML_-`, damgasız ya da `yyyyMMddHHmmss` damgalı önekli adlar)
"var" sayılamıyor → evrak eksik görünüyor ve yeniden indiriliyordu.
- Yer: Müşteri taraması sekmesi › "Adları düzelt…" (seçili müşteri × tür × dönem aralığı; önce GİB listesi alınır, PDF kotası harcanmaz).
  "Önekli adları da kat" seçilmezse yalnız tamamen eski biçimler önerilir.
- `EvrakIcerigi` (PdfPig, 1. sayfa): beyanname = "Onay Zamanı" + VKN/TCKN; tahakkuk = "TAHAKKUK FİŞİ" ya da Ba/Bs "BİLDİRİM ALINDISI",
  dönem `AA/YYYY-AA/YYYY` ve sonraki tarih (kabul). Metni olmayan (taranmış) PDF → Okunamadı.
- `AdDuzeltici.Oner`: beyanname onay zamanı tek bir GİB sürümünün aralığına (§9.7) düşmeli; VKN müşterininki olmalı; yıl klasörü doğru olmalı
  (değilse YanlisKlasor, taşınmaz). Tahakkuk: aynı adlı beyanname eşinin sürümü (`.xml_THK`→`.xml_BYN`, `_TAHAKKUK`→`_BEYANNAME`, tek başına `T`→`B`),
  eşi yoksa dönem + kabul tarihiyle tek sürüm. Yeni ad `DosyaAdlari.BeyannameDosyasi` ile (KDV'de onay zamanı, düzeltme sırası GİB'den).
- Dokunulmayanlar: `ÖD`/`OD` (tek başına), MAHSUP, ÖDEME, DEKONT, `.xml_ALN`, KOPYA, ihbarnameler.
- **Kopya:** hedef ad zaten varsa, iki dosya aynı ada çıkıyorsa ya da **aynı sürüm klasörde başka bir standart adla zaten varsa**
  (ör. eski program KDV'yi yükleme zamanıyla adlamış: `…_14_02_23` varken elle konmuş kopyanın doğru adı `…_14_02_36`) → Kopya, dokunulmaz.
  Bu son kural ilk canlı denemede 2 kopya ve 1 ödeme dosyası (`_OD`) yanlış adlandırıldıktan sonra eklendi; o 3 dosya elle eski adına alındı.
- Uygulama: kullanıcı listede onaylar → `File.Move(overwrite:false)`; her taşıma `%APPDATA%\GibEvrakIndirici\ad-duzeltme\yyyy-MM-dd_HHmmss.tsv`
  günlüğüne yazılır. "Düzeltmeyi geri al…" günlüğü tersten uygular (hedef varsa atlar), sonra günlüğü `.geri-alindi` yapar. Arşiv dizini güncellenir.
- Canlı deneme: 1 müşteri, 2021–2026, 7 tür → 16 eski + 420 önekli ad düzeltildi, 4 kopya, 3 belirsiz kaldı. Ardından tarama 04/2022 KDV1'i "var" saydı.

### 9.9 Toplu yazdırma sekmesi (2026-11-18)
- Tür + dönem tipi (aylık / 3 aylık / yıllık; türe göre varsayılan) + ay/yıl; evrak: beyanname, tahakkuk, ihbarname; MUHSGK'da ayrıca
  SGK tahakkuk / SGK hizmet listesi. "Yalnız son sürüm" (en büyük `_DÜZ`; SGK'ya uygulanmaz, asıl/ek/iptal ayrı belge).
- `ArsivListesi.Bul`: Y'de `{etiket}_{TÜR}_{BELGE}` önekiyle (yalnız standart/önekli adlar; eski adlar için önce §9.8), X'te dönemin her ayı için
  müşterinin **tüm işyeri klasörleri**. Elle eklenenler (`…_ÖD`, borç durum yazısı) listelenmez. Yalnız birim filtresindeki müşteriler.
- Sıralama: müşteriye göre (müşteri › beyanname evrakları › işyeri › ay › SGK tahakkuk › hizmet listesi) ya da evrak türüne göre.
- `PdfYazdirici`: Windows.Data.Pdf ile sayfa 200 DPI resme çizilir → `PrintDocument` tek iş (PDF okuyucusuna bağlı değil); yatay sayfa döndürülür.
  Bunun için uygulama TFM'i `net10.0-windows10.0.19041.0` (çıktı klasörü aynı kaldı: `bin\Debug\net10.0-windows`).
- Canlı: MUHSGK 10/2026, 35–37 dosya ağ yazıcısına gönderildi, hatasız.

## 10. Eski program: Luca Entegrasyon (C:\LucaEntegrasyon) — inceleme
- Java 11 masaüstü (Swing/SWT), `LucaEntegrasyon.jar` (Agem/Luca), başlatma `lucaentegrasyon.bat`.
- Ayar: `LucaBeyanname.xml` → `filePath = \\SUNUCU\beyannameler\LUCA` (= Y:\), dönem seçimi, GİB ve Luca giriş bilgileri.
  ⚠ GİB kullanıcı kodu/parola/şifre ve Luca parolası bu dosyada **düz metin** saklanıyor. Yeni uygulama şifre saklamayacak.
- OCR kütüphanesi (tess4j + tessdata) var → captcha'yı otomatik okumaya çalışıyor. Yeni uygulamada YOK (captcha kullanıcıda).
- **Neden yavaş:** müşteri başına ayrı `BEYANNAMELISTESI` sorgusu. 16.11.2026 çalışması: 3 dk'da 133 sorgu, 116'sı "bulunamamıştır", 17'si dolu (`tmp\beyanname_listesi.N.html`).
  Yeni yaklaşım: tarih aralığıyla toplu sorgu (~14 sayfa) → çok daha hızlı.
- Kullandığı servisler (jar içi adresler):
  - e-Beyanname `dispatch` (LOGIN, liste, IMAJ, PAKETYUKLEME), `eyeks`, `fileUpload`
  - **SGK e-Bildirge**: `ebildirge.sgk.gov.tr/EBildirgeV2/...` → `tahakkukonaylanmisTahakkukDonemSecildi.action`, `tahakkuk/pdfGosterim.action`
    → **X:\ SGK tahakkuk/hizmet listesi dosyaları e-Bildirge'den, işyeri bazında iniyor** (dosya adındaki belge türü/kanun/ASIL bilgisi buradan).
  - **Luca web servisi** (Agem, `webService.lucaBeyanname.agem.com.tr`, 85.111.1.95): `FirmaListesi`, `IsyeriListesi` (IsyeriKisaAd, IsyeriKodu, işyeri şifresi), `CariListesi`, `Ajanda`, `LucaToken`
    → **İşyeri klasör adları (MERKEZ, KARTAL…) Luca'daki "işyeri kısa adı"**; e-Bildirge girişi için işyeri kodu/şifresi de Luca'dan geliyor.
  - SMS (isnetsms), e-posta gönderimi, `uyg.sgk.gov.tr/IsverenBorcSorgu` (müfredat kartı)

### 10.1 SGK belgeleri için iki yol (karar bekliyor)
- **A) e-Beyanname'den** (MUHSGK satırı → SGK bildirimleri → tahakkukfis/hizmetfis). Aynı GİB oturumu, ek giriş yok.
  İşyeri klasörü: PDF içindeki sicil no → mevcut X:\ PDF'lerinden öğrenilen tablo.
- **B) e-Bildirge'den** (eski programın yöntemi). İşyeri başına SGK girişi (kullanıcı adı + işyeri kodu + şifreler + captcha) gerekir → tam otomatikle çelişir.
- **KARAR (2026-11-16): A — SGK belgeleri e-Beyanname'den indirilecek.** e-Bildirge ve Luca web servisi kullanılmayacak.
  - X:\ dosya adı için belge türü / kanun / ASIL bilgisi: SGK bildirim satırı metninden ("Asıl, TÜM SİG.KOLLARI/…, 05510-Say.Kan.MYO, 09 / 2026, …").
  - İşyeri klasörü: PDF'teki işyeri sicil no → X:\ mevcut PDF'lerinden öğrenilen tablo; bilinmeyen → tek işyeri ise MERKEZ, değilse bir kez sor.

## 11. İşyeri adlarının kaynağı: Luca Mali Müşavir Paketi
- Giriş: `agiris.luca.com.tr/LUCASSO/main.erp` → "Luca Mali Müşavir Paketi" (`gonder('formTarget')` → POST `auygs.luca.com.tr/Luca/ssoGiris.do`, yeni pencere açar)
- Menü: Personel › İşyeri İşlemleri › **İşyeri Listesi** → `auygs.luca.com.tr/Luca/listIsyeriAction.do` (frm3 çerçevesi)
- Liste **seçili firmaya** göre (firma üst çerçevede/oturumda seçiliyor), sütunlar:
  Kısa Adı | Uzun Adı | **SSK Sicil No** | Açıklama | İşe Başlama Tarihi | İşin Bitiş Tarihi
  Örnek (ÖRNEKMOB): `MERKEZ` · ÖRNEK MOBİLYA … LTD. ŞTİ. · `25678010101234560340110000` · 01/11/2023
- **Kısa Adı = X:\ işyeri klasörü.** SSK sicil no 26 hane, boşluksuz.
- Excel/dışa aktarma düğmesi yok (sadece Adres, İletişim, Taşeron, Yeni, Sil, Detay).
- 2. örnek (ÖRNEK ŞAHIS): `ANTEP` 24321080807654320340220000 (07/12/2020–) · `MALTEPE` 29999010103333330340330000 (16/02/2017–31/12/2020)
  → X:\ ile tutarlı: 2017–19 `MERKEZ`, 2020 `ANTEP`+`MALTEPE`, 2021+ `ANTEP`. Kapanmış işyerleri de listede (bitiş tarihli).
- Firma değiştirme: üst çerçeve `TopFrameAction.do?SIRKET_ID={id}&DONEM_ID={id}&DONEM_TXT=…` (toplu içe aktarma için ileride).

### 11.0 Luca'dan toplu işyeri listesi (2026-11-16 denendi, çalışıyor)
Luca'da tüm firmaların işyerlerini sicil no ile birlikte veren tek ekran YOK; ama 2 adımda toplu çekilebiliyor:
1. **Firma–işyeri listesi (tek istek):** `GET /Luca/editEBildirgeGirisDosyaAction.do?c_musteri_id={üyeNo}`
   "E-Bildirge Giriş Dosyası Tanımlama" — 252 satır, her satır: `"{FİRMA KISA AD} - {İŞYERİ KISA AD}"` + gizli `SIRKET_ID`, `ISYERI_ID`.
   (Satırda e-Bildirge kullanıcı adı/işyeri kodu/şifreleri de var → **okunmayacak/saklanmayacak**.) Sicil no YOK.
   Alternatif firma listesi: `eBildirgeTopluGonderimAction.do?ISLEM=1` → Şirket seçim kutusu.
2. **Her firma için (2 istek):**
   - Firma seç: `GET /Luca/TopFrameAction.do?time={ms}&SIRKET_ID={id}` (DONEM_ID vermeden de çalıştı)
   - Liste: `GET /Luca/listIsyeriAction.do?c_musteri_id={üyeNo}&time={ms}` → Kısa Adı · Uzun Adı · SSK Sicil No · Başlama · Bitiş
   - Örnek: ÖRNEKHAKYI → `MERKEZ` 28888010104444440340440000
3. Bittiğinde kullanıcının asıl firmasına geri dön: `TopFrameAction.do?SIRKET_ID={eski}&DONEM_ID={eski}&DONEM_TXT=…` (denendi ✓)
- `selectIsyeriAction.do?ISYERI_ID=` başka firmanın işyeri için **500** döner → firma değiştirmeden detay okunamaz.
- Yanıtlar UTF-8. ~110 firma × 2 istek ≈ 1–2 dk.
- **KARAR (2026-11-16): uygulamaya eklenecek.**
- Uygulamada: "Luca'dan işyerlerini al" düğmesi → kullanıcı WebView'da Luca'ya kendisi girer → uygulama yukarıdaki adımları yapar → işyeri tablosu (firma kısa ad, işyeri kısa ad, sicil, başlama/bitiş) yerel olarak kaydedilir. İsteğe bağlı; PDF'ten öğrenme yine yedek.
- ⚠ Firma değiştirme kullanıcının açık Luca oturumunu etkiler (aynı oturum) → işlem sonunda eski firmaya dönülmeli; Luca'da başka işlem yapılırken çalıştırılmamalı.

### 11.1 Sicil no eşleştirme kuralı (doğrulandı)
- SGK PDF'inde (tahakkuk ve hizmet listesi) sicil tek metin parçası: `211110808076543203402-20/000` → rakamlar = 26 hane.
  Regex: `^\d{21}-\d{2}/\d{3}$`
- 26 hane yapısı: M(1) · **İşkolu(4)** · Değişiklik(2) · İl/ilçe vb.(2) · Sıra no(7) · İl(3) · İlçe(2) · Kontrol(2) · Aracı(3)
- **İşkolu kodu değişebiliyor** (SGK güncelliyor), Luca'daki eski kalabiliyor:
  | İşyeri | PDF | Luca |
  |---|---|---|
  | ÖRNEKMOB MERKEZ | 2**1234**0101012345603401 10 000 | 2**5678**0101012345603401 10 000 |
  | ÖRNEK ŞAHIS ANTEP | 2**1111**0808076543203402 20 000 | 2**4321**0808076543203402 20 000 |
  | ÖRNEK ŞAHIS MALTEPE | 29999010103333330340330000 | aynı |
- **Eşleştirme anahtarı = 1. hane + 6.–26. haneler (işkolu hariç 22 hane).** Tam eşleşme yerine bu kullanılacak.
- Sonuç: plan aynı — işyeri klasörü, PDF'teki sicil no anahtarı ile eşleşir.
  Eşleme tablosu: X:\ mevcut PDF'lerinden otomatik + eşleme ekranında elle (Luca'daki Kısa Adı / SSK Sicil No buradan bakılarak girilebilir).
  İleride istenirse: Luca oturumunda firma firma `listIsyeriAction.do` okunarak toplu içe aktarma (firma değiştirme mekanizması incelenmeli).

## 12. İlk uçtan uca deneme (2026-11-16, test klasörü D:\dev\y, D:\dev\x)
- Son 31 gün: 330 onaylı beyanname → 797 PDF (659 Y, 138 X), 0 hata, 71 uyarı (Ekim MUHSGK'larda SGK bildirimi henüz PDF'siz/işlemde). Hepsi geçerli PDF.
- Gerçek arşivle karşılaştırma (Y):
  - **114 dosya birebir aynı ad ve yol** → adlandırma/klasör kuralı doğru.
  - **114 dosyada önek aynı, saat farklı**: ör. ÖRNEKSEMET 09_EYLUL_KDV1 — bizde `27.10.2026_14_20_23` (listedeki yükleme zamanı), arşivde `27.10.2026_15_04_17`. Arşivdeki saatler farklı müşterilerde 15:04:16/17 → büyük ihtimalle **onay zamanı** (toplu onay). Tahakkuk PDF'inde saat yok; arşivdeki PDF şablonu biraz farklı ("Şasi No" alanı var) → eski program başka bir kaynaktan (e-Beyan?) indiriyor olabilir.
    → Gerçek arşivde bunlar "zaten var" sayılır (önek sayımı), çift dosya oluşmaz. Ama yeni dosyaların saati eski programdan farklı olur.
  - **Kök neden (jar incelemesi + tür dağılımı):** Saat farkı yalnızca **KDV1/KDV2/KDV4** (ve 1 KURUMLAR) dosyalarında; GGECICI/KGECICI hep birebir aynı.
    Eski programda iki yol var: `web/islem/BeyannameListesiIslem2` (e-Beyanname listesi, satır `title`'ından zaman; title = yükleme zamanı) ve
    `web/ebeyan/EBeyanBeyannameIslem` (**e-Beyan** sistemi, JSON liste, `onayZamani` → dosya adı `dd.MM.yyyy_HH:mm:ss`).
    KDV beyannameleri yeni **e-Beyan** sisteminden veriliyor; eski program bunların PDF'ini e-Beyan'dan, **onay zamanı** ile adlandırıp indiriyor
    (arşivdeki KDV tahakkuk şablonunda "Şasi No" alanı = e-Beyan şablonu). e-Beyanname listesi ise aynı beyannamenin yükleme zamanını gösteriyor (fark 0–60 dk).
  - Karar bekliyor: (A) yükleme zamanıyla devam, (B) KDV türleri için e-Beyan'dan onay zamanı/PDF.

### 12.1 İkinci tur: GİB görüntüleme kısıtlaması (2026-11-16 18:29–19:04)
- İlk turdan (17:40–18:00, 797 PDF) hemen sonra başlatılan turda ilk ~12 dk sorunsuz, sonra IMAJ GET'leri **boş yanıt (0 bayt, SERVERERROR yok)** dönmeye başladı;
  18:43'ten sonra neredeyse hepsi boş (191 indirildi, 252 hata). Oturum açıktı (arada tek tük PDF geldi). Yaklaşık 1 saatte ~1000 görüntüleme → GİB tarafında kota/kısıtlama olduğu düşünülüyor (kesin sınır bilinmiyor).
- Oturum zaman aşımı ayrı bir biçimde gelir: `<SERVICERESULT><EYEKSERROR>Oturum zamanaşımına uğradı, yeni oturum açınız.</EYEKSERROR></SERVICERESULT>`.
- Uygulanan: 2 boş yanıt → `EbynKisitlamaHatasi`; indirici `KisitlamaBeklemeleriDk` (varsayılan 2,5,10 dk) kadar bekleyip **aynı satırla** devam eder,
  tükenirse indirmeyi durdurur ("Eksikleri indir" kaldığı yerden sürer). EYEKSERROR → `EbynOturumHatasi`, indirme durur, yeniden giriş istenir.
- Bu yüzden istek aralığını düşürmek (1500 → 1100 ms) **ertelendi**.

### 12.2 Üçüncü tur: kota günlük görünüyor (2026-11-16 19:56–20:36)
- Gün içi toplam başarılı indirme ve engel anları:
  | Engel | O ana kadar toplam | Son 60 dk | Son 10 dk |
  |---|---|---|---|
  | 18:31 | 846 | 846 | 49 |
  | 19:58 | 988 | 2 | 0 |
  | 20:05 | 1056 | 68 | 68 |
  | 20:08 | 1069 | 81 | 81 |
  | 20:19 | 1187 | 199 | 118 |
- 19:58'de son saatte neredeyse hiç istek yokken engel → hız/saatlik sınır değil; **günlük kota** (~850 görüntüleme, tahmin). Aşılınca arada 60–120 PDF'lik izin veriliyor.
- Tur 2+5+10 dk beklemeden sonra durdu (199 indirildi, 1 hata). Günlük kullanımda yük küçük; sorun ilk yükleme / arşiv taraması.
- Önerilen (uygulanmadı): günlük bütçe sayacı, en yeniden başlama, arşiv taramasını günlere yayma, engelde kısa kesme. Yarın kota sıfırlanıyor mu bakılacak.

### 12.3 Dördüncü tur (2026-11-18 09:26–09:40)
- 18.10–18.11 aralığı: 362 onaylı beyanname → **471 indirildi, 384 zaten var, 0 hata, hiç kısıtlama yok** → kota günlük sıfırlanıyor (tahmin destekleniyor; 850 sınırına ulaşılmadı).
- Gerçek arşivle (Y:\, X:\) karşılaştırma: arşivde karşılığı olan 228 dosyanın 214'ü birebir; 14 farklı ad (eski programın birkaç sn farklı saati,
  elle verilmiş tarihli/damgasız adlar → gerçek arşivde "var" sayılır). Ekim MUHSGK + SGK arşivde henüz yok.
- SGK bildirim listesinde MUHSGK'nın vergi kısmı "Vergi" satırı olarak geliyor, PDF'i hiç olmaz → 70 gereksiz uyarı; artık sessizce atlanıyor.
- Liste (POST) isteklerinde de "İki istek arası en az 1 sn olabilir" hatası görüldü → 1 sn kuralı yalnız görüntülemede değil; bekleme tüm isteklerde kalmalı.
- Aynı gün toplam **890 PDF** (09:26 471 + 10:55 262 + 11:10 157) hiç kısıtlama olmadan indi → "~850/gün" tahmini **tutmadı**.
  16.11'deki engel 846. indirmede gelmişti ama o gün öncesinde keşif amaçlı elle çok sayıda görüntüleme de yapılmıştı. Sınır bilinmiyor (≥890/gün).

### 12.4 Gerçek arşive ilk bağlantı (2026-11-18 11:10, bu birim, son 31 gün)
- 362 beyanname: 157 indirildi, 227 zaten var, 0 hata, 211 filtre dışı. Diske yazılan 157 = log'daki 157; **hiç yeni klasör açılmadı**.
- İnenler: 72 Ekim MUHSGK (beyanname + tahakkuk), 80 SGK PDF (X:\ var olan işyeri klasörlerine: MERKEZ 70, diğer 5 işyeri 2'şer),
  5 düzeltme: 2024 Mayıs KDV1 `_DÜZ` (beyanname, tahakkuk, ihbarname; arşivde yalnız asıl vardı) ve 2026 Eylül KDV1 `_DÜZ2`.
  **Düzeltme (aynı gün, §9.6):** Eylül `_DÜZ2` aslında KOPYA çıktı — arşivdeki elle `_DÜZ` dosyası GİB'deki 3. sürümün kendisiydi.

## 13. e-Beyan (ebeyan.gib.gov.tr) incelemesi (2026-11-16)
- Geçiş: DVD › Geçiş Yapılabilecek Uygulamalar › e-Beyan → onay penceresi ("aktif e-Beyan oturumları sonlandırılır") →
  `window.open("https://ebeyan.gib.gov.tr/dijital-login?token=…", "ebeyannmeprod")` → `POST /api/kullanici/dvd/login` → çerezli oturum (Next.js SPA).
- Duyuru: 01/10/2026'dan itibaren **geçici vergi beyannameleri de e-Beyan'da**.
- API (JSON, `{data, messages:{errorMessages…}, traceId}`):
  - `GET /api/kullanici/oturum`, `GET /api/kullanici/mukellef/mukellef-list`, `GET /api/kullanici/beyanname-tur/kod-kisa-ad`
  - `POST /api/kullanici/mukellef/mukellef-sec {vkn}` → oturumdaki aktif mükellefi değiştirir (durum değiştirir!)
  - **Liste:** `POST /api/kullanici/beyanname/filtrele`
    `{beyannameDurum:["ONAYLANDI"], beyannameTuru, mukellefVkn, islemTarihi:{baslangic,bitis}|null, beyannameDonemTarihi:{baslangic,bitis}|null, page, size≤100}`
    → `data.beyannamePage{content[], currentPage, totalItems, totalPages}`; kayıt: `beyannameId, mukellefUnvan, mukellefVkn, mukellefTckn, vdKod,
       beyannameTuru{kod, beyannamelerimBaseLink:"/kdv1"…}, donemTip, donemBaslangicTarih, donemBitisTarih, islemTarihi (onay), beyannameDurum, sgkDurum, tahakkukOid, ihbKesildiMi`
    - Varsayılan yalnızca **bu kullanıcının oluşturduğu** beyannameler (bu hesapta Şubat 2026'dan beri 197: KDV1 166, KDV2 16, KONAKLAMA 12, DAMGAKK 3).
      Başka kullanıcının oluşturduğu (ör. ÖRNEKSEMET) görünmüyor; `mukellefVkn` dize "beklenen formatta değil" hatası, dizi → 0 kayıt.
  - **PDF (GET, modül tabanlı `/api{beyannamelerimBaseLink}`):**
    `/beyanname/pdf?beyannameId=` (Beyanname, `{VKN}_Kdv1_202610202610_Beyanname.pdf`),
    `/beyanname/pdf/tahakkuk?beyannameId=[&cezaTuru=]` (`…_TahakkukFisi.pdf`), `/beyanname/pdf/ihbarname?beyannameId=&cezaTuru=`,
    `/beyanname/pdf/liste?beyannameId=` → `{tahakkukOid, tahakkukBelgeList[]}` (ceza tahakkuk/ihbarnameleri)
  - ⛔ Asla: `/beyanname/onayla`, `/cokluOnayla`, `/duzeltme`, `DELETE /beyanname`, `/hesapla`, evrak/rapor düzeltme.
- **Arşiv karşılaştırması** (bu kullanıcının 130 KDV/KONAKLAMA kaydı, Ağu–Eki 2026, arşivde dosyası olan 15):
  8'inde arşiv saati = e-Beyan `islemTarihi`; 7'sinde arşiv saati e-Beyan işlem saatinden 10–45 sn önce (= e-Beyanname yükleme zamanı).
  → **Eski program da tutarlı değil**: bazen e-Beyan onay zamanı, bazen e-Beyanname yükleme zamanı kullanmış.
- (Önceki ara sonuç: yükleme zamanıyla devam önerisi — §13.1 ile güncellendi.)

### 13.1 Onay zamanı beyanname PDF'inde (kullanıcı bilgisi, doğrulandı)
- e-Beyanname'den inen **beyanname PDF'inin 1. sayfasında "Onay Zamanı : 27.10.2026 - 15:04:17"** var (tahakkukta yok).
- e-Beyanname satır `title`'ı = yükleme zamanı (onay değil) — ÖRNEKSEMET 2026 KDV1 9 dönemde de title = yükleme.
- Test indirmesi (113 beyanname, arşivde karşılığı olan) PDF onay zamanı vs arşiv adı:
  | Tür | Arşiv saati |
  |---|---|
  | KDV1 | 42 onay zamanı, 3 yükleme, 6 basit okuyucuyla okunamadı (örneklerde onay), 1 hiçbiri |
  | KDV2 / KDV4 | 5 + 1 onay zamanı (2 okunamadı) |
  | GGECICI | 36 yükleme |
  | KGECICI | 14 yükleme |
  → **Eski program: KDV türlerinde onay zamanı (e-Beyan yolu), diğerlerinde yükleme zamanı.**
- **UYGULANDI (2026-11-16):** `Ayarlar.OnayZamaniTurleri` (varsayılan `KDV1,KDV2,KDV4`) türlerinde beyanname PDF'i önce indirilir,
  `BeyannamePdfOkuyucu` (PdfPig, 1. sayfa) ile onay zamanı okunur; beyanname, tahakkuk ve ihbarname adlarında bu kullanılır. Okunamazsa yükleme zamanı + uyarı.
  - PDF'te iki biçim: e-Beyanname `Onay Zamanı :27.10.2026 - 15:04:17`, e-Beyan/TUBITAK `Onay Zamanı: 18/10/2026 16:24:47`.
  - Test indirmesindeki 329 beyanname PDF'inin **329'undan** okundu.
  - Arşivle örtüşme (yeni kural): GGECICI 38/38, KGECICI 14/14, KDV2 7/7, KDV4 1/1, KDV1 47/52 birebir (kalan 5'te eski program yükleme zamanı kullanmış), KURUMLAR 2020 1 farklı (eski elle dosya).
    Önceki kuralla: 114 birebir / 114 farklı → şimdi 107 birebir / 6 farklı.

### 13.2 e-Beyan resmi entegrasyon API'si
- Belge: https://ebeyan.gib.gov.tr/entegrator/docs/giris — Test `https://ebeyanpreprodapi.gib.gov.tr`, Prod `https://ebeyanapi.gib.gov.tr`
- Başvuru: DVD › Başvuru Formlarım › **"e-Beyan Entegrasyon Başvurusu"** (e-beyanname gönderme yetkisi olan kullanıcı).
- Kimlik: **Bearer API KEY** — e-Beyan › Profil › **Entegrasyon Yönetimi**'nden kullanıcı kendisi üretir (silinip yenilenebilir, eskisi geçersizleşir).
  "API bilgileri üzerinden **tüm entegrasyon işlemleri** yapılabilir" (onay dahil) → anahtar çok hassas.
- Zorunlu başlıklar: `CHANNEL: ENTEGRATOR`, `MUKELLEF-VKN: {mükellef VKN}` (istek başına tek mükellef), `ENTEGRATOR: {firma VKN}-{unvan ≤20, ASCII}`; eksikse 403.
- Kapsam: belgede beyanname türleri bazında oluşturma/toplu yükleme/onaylama akışları anlatılıyor (KDV1 intro: `POST /beyanname/toplu`, `/beyanname/onayla`…). Listeleme/PDF uçlarının referans sayfası tarayıcıda yüklenmedi → doğrulanmadı.
- 3. taraf kaynak: canlı ortamda sabit IP yetkilendirmesi istenebiliyor (doğrulanmadı).
- **Eski program bu API'yi doğrudan değil, Luca sunucusu üzerinden kullanıyor**: `ebeyanSorgu.do` + `EBEYAN-API-KEY`, `X-EBEYAN-SORGU` başlıkları + Luca token.
  `LucaBeyanname.xml` içindeki `ebeyanApiKey` = kullanıcının e-Beyan entegrasyon anahtarı (düz metin!).
  - 431 dosya arşivde yok: Ekim yüklemeleri ve klasörü olmayan "0_" müşteriler — beklenen.
- X: Ekim SGK'ları arşivde henüz yok → karşılaştırılamadı (Eylül ile denenmeli).
- Düzeltilen hata: WebView `Runtime.evaluate` boolean sonucu "True" dönüyordu → e-Beyanname geçişi algılanmıyordu.

## Sıradaki
- DVD tarafındaki e-Tebligat (ihbarname / tebligat) — gerekirse
- Uygulama iskeleti
