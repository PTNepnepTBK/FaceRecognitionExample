# Offline Face Recognition (Android)

Selamat datang di proyek **Face Recognition**! Aplikasi Android ini dibuat menggunakan **.NET MAUI** dan dirancang untuk bisa mengenali wajah seseorang secara **100% Offline** langsung di dalam HP Anda, tanpa butuh koneksi internet sama sekali.

Aplikasi ini juga sangat pintar karena memiliki fitur **Anti-Spam**: Jika wajah Anda sudah terekam, sistem tidak akan mencatat wajah Anda lagi selama 5 menit ke depan.

---

## Dibuat Menggunakan Apa Saja?

Aplikasi ini ditenagai oleh beberapa paket (*library*) yaitu:

- **`FaceONNX`**: Otak utama AI kita. Digunakan untuk merapikan foto wajah (*Alignment*) dan mengekstrak identitas wajah menjadi angka matematika (*Embedding*).
- **`Microsoft.ML.OnnxRuntime`**: Mesin penggerak yang membuat AI bisa berjalan mulus di HP Android Anda.
- **`sqlite-net-pcl`**: Database lokal (SQLite) tempat kita menyimpan histori wajah yang sudah terdeteksi.
- **`CommunityToolkit.Maui.Camera`**: Kamera bawaan MAUI untuk mengambil foto beresolusi tinggi.

---

## Model AI & Lokasinya

Kita menggunakan dua AI yang bekerja sama ibarat **"Mata"** dan **"Otak"**:

1. **"Mata" (Model YuNet)**
   - **Tugas:** Hanya mencari tahu *di mana* lokasi wajah Anda di layar (menghasilkan kotak merah).
   - **Lokasi File:** Tersimpan di dalam folder `Resources/Raw/face_detection_yunet_320.onnx`.
   - **Ukuran:** Menggunakan resolusi `320x320` piksel agar bisa melihat wajah dari jarak cukup jauh.

2. **"Otak" (Model ArcFace / FaceEmbedder)**
   - **Tugas:** Mengingat dan mengenali *siapa* Anda dari potongan foto wajah tersebut.
   - **Lokasi File:** Model ini **sudah tertanam langsung** di dalam *package* `FaceONNX`. Jadi Anda tidak akan menemukan file `.onnx`-nya secara fisik di folder proyek.

---

## Pengaturan Sensitivitas (Konstanta)

Semua pengaturan angka atau ambang batas sensitivitas aplikasi berpusat di satu file khusus, yaitu:
**[`AppConstants.cs`](/AppConstants.cs)**

Di dalam file ini, Anda bebas mengubah parameter berikut:
- **`SimilarityThreshold` (0.6f)**: Tingkat toleransi kemiripan wajah. Semakin mendekati 1.0, AI akan semakin ketat (hanya mengenali wajah yang benar-benar mirip 100%).
- **`MinFaceSizeThreshold` (270)**: Ukuran kotak wajah minimum dalam piksel agar sistem *Live Tracking* mau memproses wajah tersebut.
- **`CooldownMinutes` (5)**: Lama waktu anti-duplikat. Wajah yang sama tidak akan disimpan ulang sebelum durasi ini habis.

---

## Dua Macam Mode Kamera

Karena deteksi wajah butuh perlakuan berbeda, kami menyiapkan 2 mode kamera:

- **Mode "Capture & Detect" (Manual)**
  Seperti kamera HP biasa. Anda menekan tombol jepret, lalu aplikasi akan berpikir sejenak untuk menganalisa foto tersebut secara perlahan namun sangat akurat.
  
- **Mode "Live Tracking" (Otomatis & Ngebut)**
  Kamera ini menyala terus dan melacak wajah Anda secara *real-time*. Untuk mencapai kecepatan tinggi, kami tidak memakai kamera MAUI biasa, melainkan menggunakan *Native Android CameraX agar gambar dari lensa bisa langsung diberikan ke AI dalam hitungan milidetik!

---

## Bagaimana Cara Kerjanya? (Alur / Pipeline)

Bayangkan Anda berdiri di depan kamera, inilah yang terjadi di dalam mesin dalam waktu kurang dari 1 detik:

1. **Melihat (Deteksi):** AI YuNet memindai layar dan menggambar kotak merah di wajah Anda.
2. **Filter Pintar (Khusus Live):** Jika wajah Anda terlalu jauh (kotak merah kurang dari `270px`), mesin akan berhenti untuk menghemat baterai. Anda harus mendekat!
3. **Merapikan (Alignment):** Jika ukuran pas, wajah Anda akan dipotong, dilebarkan sedikit, dan posisinya diluruskan.
4. **Mengingat (Embedding):** Wajah Anda diubah menjadi deretan angka sandi rahasia (Vektor).
5. **Mencocokkan (Matching):** Sistem mengecek database: *"Apakah sandi wajah ini sudah pernah lewat dalam 5 menit terakhir?"*
6. **Simpan:** Jika sudah pernah, maka **Ditolak (Duplikat)**. Jika belum pernah, maka wajah Anda **Tersimpan** di database!

---
*Dibuat menggunakan .NET 8.0 MAUI.*
