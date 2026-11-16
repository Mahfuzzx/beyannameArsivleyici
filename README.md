# GİB Evrak İndirici

e-Beyanname'den onaylı beyannamelerin **beyanname, tahakkuk, vergi ihbarnamesi, SGK tahakkuk ve SGK hizmet listesi** PDF'lerini indirip ofis arşivine (Y:\ ve X:\) mevcut adlandırma kurallarıyla yerleştirir.

Tasarım kararları ve GİB/Luca keşif notları: [GIB_AKIS_NOTLARI.md](GIB_AKIS_NOTLARI.md)

## Gereksinimler
- Windows 10/11, .NET 10 SDK
- **Microsoft Edge WebView2 Runtime** (Evergreen) — https://developer.microsoft.com/microsoft-edge/webview2/

## Çalıştırma
```
dotnet run --project src/GibEvrakIndirici
```
Testler:
```
dotnet test
```

## Akış
1. **GİB girişi** sekmesinde Dijital Vergi Dairesi'ne bilgilerinizi ve doğrulama kodunu **siz** girersiniz (program şifre saklamaz, doğrulama kodunu çözmez).
2. Program e-Beyanname'ye kendisi geçer, son N günün (Ayarlar › AcilisGunSayisi) **onaylı** beyannamelerini listeler ve **eksikleri otomatik indirir**.
3. **Arşiv taraması** son N yılı (Ayarlar › TaramaYilSayisi) aylık dilimlerle tarar.
4. Var olan dosyanın üzerine asla yazılmaz; `_MAHSUP`, `_ÖD`, `B.xls` gibi elle eklenen dosyalara dokunulmaz.

## İlk kurulum
1. **Ayarlar**: `YArsivKok` (Y:\), `XArsivKok` (X:\), `LucaUyeNo`.
2. **Müşteri Excel'i al**: Luca "Müşteri Listesi" Excel'i (Kısa Ad → klasör adı, Vergi No / TCKN → eşleşme).
3. SGK işyeri eşlemesi (sicil no → X:\ işyeri klasörü):
   - **Luca** sekmesi → Luca'ya girip "Luca Mali Müşavir Paketi"ni açın → **İşyerlerini Luca'dan al**, ve/veya
   - **X arşivinden işyeri öğren** (mevcut SGK PDF'lerinden sicil okur).

## Güvenlik
- e-Beyanname'ye yalnızca beyaz listedeki okuma/görüntüleme komutları gönderilir (`EbynKomutlari.Dogrula`). Onay, iptal, paket gönderme komutları kodda yoktur ve reddedilir.
- Luca'dan işyeri aktarımında e-Bildirge kullanıcı adı/şifre alanları okunmaz.

## Proje yapısı
- `src/GibEvrakIndirici.Core` — kurallar ve mantık (adlandırma, ayrıştırıcılar, arşiv denetimi, indirme, Luca aktarımı)
- `src/GibEvrakIndirici` — WinForms + WebView2 arayüz
- `tests/GibEvrakIndirici.Tests` — xUnit testleri
- Veriler: `%APPDATA%\GibEvrakIndirici\` (ayarlar.json, musteriler.json, isyerleri.json, log\)
