# Spesifikasi Teknis Detail

## 1. Skema database (SQLite)

```sql
CREATE TABLE FaceRecords (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Embedding BLOB NOT NULL,        -- float[] hasil serialize (mis. via Buffer.BlockCopy)
    DetectedAtUtc TEXT NOT NULL,    -- ISO 8601 UTC timestamp
    Note TEXT NULL                  -- metadata tambahan, opsional
);

CREATE INDEX idx_facerecords_detectedat ON FaceRecords (DetectedAtUtc);
```

## 2. Konstanta yang wajib configurable (bukan hardcoded di tengah logic)

| Nama | Default | Keterangan |
|---|---|---|
| `CooldownMinutes` | 5 | Jendela waktu anti-duplicate |
| `SimilarityThreshold` | 0.6 | Ambang batas cosine similarity dianggap "wajah sama" — PERLU DI-TUNE manual pakai data asli |
| `TargetResponseTimeMs` | 1500 | Dipakai untuk logging/warning kalau pipeline melebihi ini |
| `FaceDetectionMinConfidence` | 0.7 | Ambang minimum confidence deteksi wajah, hindari false positive dari objek bukan wajah |

## 3. Format popup

- **Popup berhasil**: informasi minimal — pesan sukses + timestamp deteksi.
- **Popup penolakan**: informasi minimal — pesan bahwa wajah sudah terdeteksi
  sebelumnya + (opsional) berapa menit/detik lalu terdeteksi.
- Implementasi: mulai dari `DisplayAlert` bawaan MAUI untuk cepat jalan, baru
  ganti ke custom popup/ContentView kalau perlu styling khusus.

## 4. Alur error handling yang perlu ditangani

- Kamera tidak tersedia / permission ditolak → tampilkan pesan error, jangan crash.
- Tidak ada wajah terdeteksi dalam frame → skip diam-diam, lanjut ke frame berikutnya
  (jangan spam popup/error).
- Lebih dari satu wajah dalam satu frame → untuk versi awal, proses hanya wajah dengan
  bounding box terbesar (asumsi: wajah paling dekat ke kamera). Catat sebagai
  known limitation di README.
- Gagal generate embedding (model error) → log error, jangan crash aplikasi.
- SQLite locked/error saat read-write bersamaan → gunakan connection yang thread-safe
  (sqlite-net-pcl sudah handle ini secara default, tapi tetap perlu di-test).

## 5. Hal yang perlu diverifikasi manual sebelum lanjut ke Fase 5 (optimasi)

- [ ] Cek isi package NuGet FaceONNX setelah restore — pastikan model .onnx benar ada
- [ ] Konfirmasi ukuran final APK setelah build (karena package ~150MB)
- [ ] Test di device fisik low-end target — bukan emulator
- [ ] Ukur waktu tiap tahap pipeline (capture, detect, embed, match) secara terpisah
      untuk tahu bottleneck di mana kalau response time > 1.5 detik
- [ ] Test dengan variasi pencahayaan (terang, remang, backlight)
- [ ] Test dengan lebih dari 100-1000 record di SQLite untuk lihat apakah query
      "5 menit terakhir" masih cepat pada skala data lebih besar

## 6. Referensi
- FaceONNX repo & examples: https://github.com/FaceONNX/FaceONNX
- FaceONNX models reference: https://github.com/FaceONNX/FaceONNX.Models
- sqlite-net-pcl docs: https://github.com/praeclarum/sqlite-net
- .NET MAUI camera options: cek `CommunityToolkit.Maui.Camera` docs sebelum implementasi
