# Project: Offline Face Recognition — .NET MAUI (Android)

## Ringkasan
Aplikasi Android (via .NET MAUI, target .NET 8.0) yang melakukan face recognition
sepenuhnya offline on-device. Tujuan utama: mencegah wajah yang sama terdeteksi
berulang dalam rentang waktu tertentu (default 5 menit).

## Target performa (WAJIB dipatuhi)
- Total response time dari capture frame sampai popup hasil: **< 1.5 detik**
- Target device: Android low-end (asumsikan Snapdragon 4xx-6xx series, RAM 3-4GB,
  Android API level 24+)
- Semua proses (detection, embedding, matching) HARUS jalan di background thread,
  UI update di-marshal ke main thread hanya untuk hasil akhir.

## Alur aplikasi (business logic)
1. Capture frame dari kamera (live/near-realtime, bukan tombol jepret manual)
2. Deteksi wajah di frame
3. Kalau tidak ada wajah terdeteksi → lanjut ke frame berikutnya, tidak ada aksi lain
4. Kalau ada wajah → crop & align wajah
5. Generate embedding vector dari wajah yang sudah di-align
6. Query local storage: ambil semua embedding tersimpan dengan timestamp
   dalam 5 menit terakhir (konstanta, taruh di config/appsettings agar mudah diubah)
7. Hitung cosine similarity antara embedding baru vs hasil query
8. Kalau ada yang similarity-nya di atas threshold (default 0.6, harus configurable):
   → tampilkan popup PENOLAKAN (duplicate/wajah sudah terdeteksi)
   → JANGAN simpan embedding baru
9. Kalau tidak ada yang match:
   → simpan embedding baru + metadata (timestamp, dan field lain yang relevan) ke local storage
   → tampilkan popup BERHASIL

## Tech stack (sudah diputuskan — JANGAN ganti tanpa diskusi)
- Framework: .NET MAUI, target framework `net8.0-android`
- Platform: Android only untuk saat ini (single-project OK, tidak perlu multi-platform)
- Face detection & recognition: library **FaceONNX** (NuGet package `FaceONNX`)
  - Package ini KEMUNGKINAN BESAR sudah membundle model .onnx di dalamnya
    (ukuran package ~150MB mengindikasikan ini). VERIFIKASI dulu dengan cara:
    cek isi folder `~/.nuget/packages/faceonnx/[versi]/` setelah restore,
    dan baca contoh kode di https://github.com/FaceONNX/FaceONNX/tree/main/netstandard/Examples
    sebelum asumsi model tersedia otomatis.
  - Dependency yang ikut: `Microsoft.ML.OnnxRuntime.Managed` (>=1.9.0), `UMapx` (>=7.5.1.5)
- Local storage: SQLite via `sqlite-net-pcl`
  - Simpan embedding sebagai BLOB (serialize float[] ke byte[])
  - Kolom minimal: Id, EmbeddingBlob, DetectedAtUtc, metadata tambahan (bebas, sesuaikan kebutuhan)
- Kamera: evaluasi dulu antara:
  a) `CommunityToolkit.Maui.Camera` (lebih cepat diimplementasikan, tapi ada overhead)
  b) Custom handler ke CameraX native Android (lebih cepat eksekusi, tapi effort lebih besar)
  → MULAI dengan opsi (a) untuk dapat working prototype cepat, ukur response time,
    BARU pindah ke (b) kalau ternyata (a) tidak memenuhi target < 1.5 detik.

## Struktur project yang diharapkan
```
/FaceRecogApp
  /Models          -> POCO: FaceRecord, RecognitionResult, dll
  /Services
    ICameraService.cs / CameraService.cs
    IFaceEmbeddingService.cs / FaceEmbeddingService.cs   -> wrap FaceONNX
    IFaceStorageService.cs / FaceStorageService.cs       -> wrap SQLite
    IFaceMatchingService.cs / FaceMatchingService.cs     -> cosine similarity logic
  /Views
    MainPage.xaml / MainPage.xaml.cs
    ResultPopup.xaml (atau ContentView/Popup custom)
  /Platforms/Android
    (native camera code kalau perlu custom handler)
  MauiProgram.cs   -> DI registration semua service di atas
  appsettings / Constants.cs -> threshold, durasi 5 menit, dll sebagai konstanta
```

## Konvensi kode
- Gunakan Dependency Injection (`MauiProgram.cs`) untuk semua service, jangan static/singleton manual
  kecuali ada alasan performa spesifik (jelaskan di komentar kalau begitu).
- Semua service yang I/O-bound (SQLite, kamera) harus async (`Task`/`Task<T>`).
- Threshold, durasi cooldown (5 menit), dan path model (kalau perlu manual) HARUS
  jadi konstanta/config, bukan magic number tersebar di banyak file.
- Tulis log/telemetry sederhana (misal `Debug.WriteLine` atau `ILogger`) di tiap tahap
  pipeline (capture, detect, embed, match, save) dengan timestamp, supaya gampang
  diukur di mana bottleneck kalau response time meleset dari 1.5 detik.

## Yang TIDAK perlu dikerjakan agent (di luar scope, akan dilakukan manual)
- Testing di device fisik — agent boleh bantu setup adb/deploy tapi observasi hasil
  visual & pengukuran waktu real akan dilakukan manusia.
- Keputusan bisnis soal regulasi data biometrik (UU PDP) — akan direview terpisah.
- Signing/release build untuk Play Store.

## Definition of Done (per fase)
- Fase 1: Project skeleton jalan, bisa capture kamera, tampil preview di layar.
- Fase 2: Face detection jalan, bounding box wajah tervisualisasi di preview (untuk debug).
- Fase 3: Embedding berhasil di-generate dari wajah terdeteksi, tersimpan ke SQLite.
- Fase 4: Matching logic (cosine similarity + filter 5 menit) berfungsi, popup
  berhasil/tolak muncul sesuai kondisi.
- Fase 5: Optimasi — ukur response time end-to-end, refactor kalau > 1.5 detik.

Kerjakan fase demi fase, jangan loncat. Setelah tiap fase selesai, laporkan
ringkasan apa yang sudah jalan dan apa yang perlu saya (manusia) verifikasi di device.
