# Setup Guide — Face Recognition Offline (.NET MAUI)

## Sebelum mulai pakai AI agent (checklist manusia)

- [ ] Install .NET 8 SDK
- [ ] Install MAUI workload: `dotnet workload install maui`
- [ ] Install Android SDK (via Visual Studio installer, atau `dotnet workload install android`)
- [ ] Colok device Android fisik low-end target, aktifkan Developer Options +
      USB Debugging, dan **izinkan prompt "Allow USB debugging?"** yang muncul
      di layar HP saat pertama connect (ini wajib dilakukan manusia, tidak bisa
      diotomasi lewat command line)
- [ ] Jalankan `adb devices` untuk pastikan device terdeteksi
- [ ] Taruh file `CLAUDE.md`, `SPEC.md`, dan `Directory.Packages.props` ini di
      root folder project (setelah project di-generate di langkah berikutnya)

## Langkah generate project awal

```bash
# Generate skeleton project MAUI
dotnet new maui -n FaceRecogApp
cd FaceRecogApp

# Copy file-file ini (CLAUDE.md, SPEC.md, Directory.Packages.props, .gitignore)
# ke root folder FaceRecogApp
```

## Menjalankan AI agent

Setelah file `CLAUDE.md` ada di root project, buka Claude Code (atau agent
lain) di folder tersebut. Agent akan otomatis membaca `CLAUDE.md` sebagai
konteks. Contoh instruksi awal ke agent:

```
Mulai dari Fase 1 sesuai CLAUDE.md: setup project skeleton, tambahkan
package NuGet dari Directory.Packages.props, dan pastikan kamera preview
bisa jalan di MainPage.
```

Kerjakan **satu fase per sesi**, jangan minta agent langsung kerjakan semua
fase sekaligus — ini supaya kamu bisa verifikasi tiap tahap sebelum lanjut,
dan supaya kalau ada yang salah, gampang di-trace di fase mana masalahnya.

## Setelah tiap fase (checklist verifikasi manusia)

- [ ] Build project: `dotnet build`
- [ ] Deploy ke device fisik: `dotnet build -t:Run -f net8.0-android`
      (atau lewat Visual Studio Run button)
- [ ] Amati hasil visual di layar device (preview kamera, bounding box,
      popup, dll) — ini tidak bisa diverifikasi oleh agent sendiri
- [ ] Kalau ada bug/behavior aneh yang hanya muncul di device fisik,
      laporkan ke agent dengan detail: langkah reproduksi, pesan error
      dari `adb logcat` kalau ada, dan device spec yang dipakai

## Verifikasi khusus terkait FaceONNX (lakukan sebelum Fase 3)

```bash
# Setelah dotnet restore, cek apakah model .onnx benar ter-bundle
find ~/.nuget/packages/faceonnx -name "*.onnx"
```

Kalau hasil kosong, berarti model TIDAK otomatis include dan perlu
didownload manual dari https://github.com/FaceONNX/FaceONNX.Models —
laporkan hasil ini ke agent supaya strategi loading model disesuaikan.

## Milestone pengukuran performa (Fase 5)

Siapkan device fisik low-end target sebelum fase ini. Ukur waktu tiap
tahap (capture → detect → embed → match → save) secara terpisah, catat
hasilnya, baru putuskan bagian mana yang perlu dioptimasi kalau total
melebihi 1.5 detik.
