using Microsoft.ML.OnnxRuntime;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Primitives;
using CommunityToolkit.Maui.Views;
using FaceONNX;
using FaceRecognitionExample.Models;
using FaceRecognitionExample.Services;
using Microsoft.Maui.Graphics;
using System.Diagnostics;

namespace FaceRecognitionExample
{
    public partial class MainPage : ContentPage
    {
        // private FaceDetector _faceDetector;
        private YuNetFaceDetector _yuNetDetector;
        private FaceEmbedder _faceEmbedder;
        private FaceBoundingBoxDrawable _drawable;
        private readonly IFaceStorageService _storageService;
        private readonly IFaceMatchingService _matchingService;
        private bool _isModelLoading = true;
        private string _modelLoadError = null;

        public MainPage(IFaceStorageService storageService, IFaceMatchingService matchingService)
        {
            InitializeComponent();
            _storageService = storageService;
            _matchingService = matchingService;
            _drawable = new FaceBoundingBoxDrawable();
            graphicsView.Drawable = _drawable;

            // Initialize FaceDetector and FaceEmbedder on a background thread to prevent UI freeze
            Task.Run(async () =>
            {
                try
                {
                    // Optimize ONNX Runtime for Unisoc T616 (2x Cortex-A75 Big Cores)
                    var options = new SessionOptions();
                    options.IntraOpNumThreads = 2; // Dedicated to the 2 High-Performance Cortex-A75 cores
                    options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;

                    // Initialize with custom options and default thresholds (0.3f, 0.4f, 0.5f)
                    // _faceDetector = new FaceDetector(options, 0.3f, 0.4f, 0.5f);
                    
                    using var stream = await FileSystem.OpenAppPackageFileAsync(AppConstants.CaptureModelFile);
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    byte[] modelBytes = memoryStream.ToArray();
                    _yuNetDetector = new YuNetFaceDetector(modelBytes, AppConstants.CaptureModelSize, AppConstants.CaptureModelSize);

                    _faceEmbedder = new FaceEmbedder(options);
                    _isModelLoading = false;
                    MainThread.BeginInvokeOnMainThread(() => lblStatus.Text = "Model Siap");
                    Debug.WriteLine("Models initialized successfully.");
                }
                catch (Exception ex)
                {
                    _isModelLoading = false;
                    _modelLoadError = ex.Message;
                    MainThread.BeginInvokeOnMainThread(() => lblStatus.Text = $"Error Load Model: {ex.Message}");
                    Debug.WriteLine($"Failed to initialize Models: {ex}");
                }
            });
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _storageService.InitializeAsync();
            
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Camera>();
            }
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            // Force the camera container to be a perfect square based on screen width
            if (width > 0 && cameraContainer.HeightRequest != width)
            {
                cameraContainer.HeightRequest = width;
            }
        }

        private async void OnSwitchCameraClicked(object sender, EventArgs e)
        {
            var cameras = await cameraView.GetAvailableCameras(CancellationToken.None);
            var newCamera = cameras.FirstOrDefault(c => c != cameraView.SelectedCamera);
            
            if (newCamera != null)
            {
                cameraView.SelectedCamera = newCamera;
            }
        }

        private Stopwatch _totalStopwatch;

        private async void OnCaptureClicked(object sender, EventArgs e)
        {
            if (cameraView.IsAvailable)
            {
                // Start the true end-to-end stopwatch here!
                _totalStopwatch = Stopwatch.StartNew();

                // Show loading state, hide controls
                btnCapture.IsVisible = false;
                btnSwitchCamera.IsVisible = false;
                loadingIndicator.IsVisible = true;
                loadingIndicator.IsRunning = true;
                lblStatus.Text = "Sedang mendeteksi...";

                await cameraView.CaptureImage(CancellationToken.None);
            }
        }

        private void OnRetakeClicked(object sender, EventArgs e)
        {
            // Reset UI
            loadingIndicator.IsRunning = false;
            loadingIndicator.IsVisible = false;
            btnRetake.IsVisible = false;
            btnCapture.IsVisible = true;
            btnSwitchCamera.IsVisible = true;
            
            resultContainer.IsVisible = false;
            sliderPreview.IsVisible = false;
            sliderPreview.Value = 0;
            
            imgPreview.Source = null;
            imgCroppedFace.Source = null;
            
            _drawable.Faces = null;
            graphicsView.Invalidate();
            lblStatus.Text = "Model Siap";
        }

        private async void OnLiveTrackingClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(LiveTrackingPage));
        }

        private async void OnMediaCaptured(object sender, MediaCapturedEventArgs e)
        {
            if (_isModelLoading)
            {
                await Dispatcher.DispatchAsync(() => 
                {
                    DisplayAlert("Tunggu", "Model FaceONNX sedang dimuat.", "OK");
                    OnRetakeClicked(null, EventArgs.Empty);
                });
                return;
            }

            if (_yuNetDetector == null || _faceEmbedder == null)
            {
                await Dispatcher.DispatchAsync(() => 
                {
                    DisplayAlert("Error", $"Model gagal dimuat:\n{_modelLoadError}", "OK");
                    OnRetakeClicked(null, EventArgs.Empty);
                });
                return;
            }

            try
            {
                var swStep = new Stopwatch();

                using var stream = e.Media;
                if (stream == null) 
                {
                    await Dispatcher.DispatchAsync(() => OnRetakeClicked(null, EventArgs.Empty));
                    return;
                }

                // Copy stream because we need it for both FaceDetector and the UI Image
                swStep.Restart();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                
                byte[] imageBytes = memoryStream.ToArray();
                swStep.Stop();
                Debug.WriteLine($"[Telemetry] Stream Copy: {swStep.ElapsedMilliseconds} ms");

                // Telemetry breakdown variables
                long tCamera = _totalStopwatch?.ElapsedMilliseconds ?? 0;
                long tPreprocess = 0;
                long tDetect = 0;
                long tAlign = 0;
                long tEmbed = 0;
                long tMatch = 0;
                long tSave = 0;

                FaceDetectionResult[] faces = null;
                float[][,] imageMatrix = null;
                int finalWidth = 0;
                int finalHeight = 0;
                string statusText = "Tidak ada wajah";

                byte[] croppedFaceBytes = null;

                // UI update variables
                bool isDuplicateFinal = false;
                long totalTimeFinal = 0;

                // Move all heavy processing to a background thread to prevent UI freezing
                await Task.Run(async () => 
                {
#if ANDROID
                    var swStage = Stopwatch.StartNew();

                    // Fast decode bounds to calculate inSampleSize
                    var options = new global::Android.Graphics.BitmapFactory.Options();
                    options.InJustDecodeBounds = true;
                    global::Android.Graphics.BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length, options);
                    
                    int maxDim = AppConstants.CaptureModelSize; // Match YuNet model input size
                    options.InSampleSize = 1;
                    if (options.OutWidth > maxDim || options.OutHeight > maxDim)
                    {
                        int halfWidth = options.OutWidth / 2;
                        int halfHeight = options.OutHeight / 2;
                        while ((halfWidth / options.InSampleSize) >= maxDim || (halfHeight / options.InSampleSize) >= maxDim)
                        {
                            options.InSampleSize *= 2;
                        }
                    }
                    
                    options.InJustDecodeBounds = false;
                    using var originalBitmap = global::Android.Graphics.BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length, options);
                    
                    if (originalBitmap != null)
                    {
                        // 1:1 Center Crop
                        int size = Math.Min(originalBitmap.Width, originalBitmap.Height);
                        int xOffset = (originalBitmap.Width - size) / 2;
                        int yOffset = (originalBitmap.Height - size) / 2;
                        using var squareBitmap = global::Android.Graphics.Bitmap.CreateBitmap(originalBitmap, xOffset, yOffset, size, size);

                        // Original imageBytes is preserved for the UI.
                        // We use squareBitmap only to feed the AI.

                        int width = size;
                        int height = size;
                    
                        if (width > maxDim || height > maxDim)
                        {
                            float ratio = Math.Min((float)maxDim / width, (float)maxDim / height);
                            width = (int)(width * ratio);
                            height = (int)(height * ratio);
                        }

                        // Create scaled bitmap for AI from the square one
                        using var androidBitmap = global::Android.Graphics.Bitmap.CreateScaledBitmap(squareBitmap, width, height, true);

                        int[] pixels = new int[width * height];
                        androidBitmap.GetPixels(pixels, 0, width, 0, 0, width, height);

                        imageMatrix = new float[3][,];
                        imageMatrix[0] = new float[height, width]; // B
                        imageMatrix[1] = new float[height, width]; // G
                        imageMatrix[2] = new float[height, width]; // R

                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                int color = pixels[y * width + x];
                                // FaceONNX expects BGR format!
                                imageMatrix[0][y, x] = (color & 0xff) / 255f;                 // Blue
                                imageMatrix[1][y, x] = ((color >> 8) & 0xff) / 255f;          // Green
                                imageMatrix[2][y, x] = ((color >> 16) & 0xff) / 255f;         // Red
                            }
                        }

                        swStage.Stop();
                        tPreprocess = swStage.ElapsedMilliseconds;
                        Debug.WriteLine($"[Telemetry] Preprocessing (1:1 & Pixel Extr): {tPreprocess} ms");

                        swStage.Restart();
                        faces = _yuNetDetector.FastForward(pixels, width, height);
                        swStage.Stop();
                        tDetect = swStage.ElapsedMilliseconds;
                        Debug.WriteLine($"[Telemetry] FaceDetector.Forward: {tDetect} ms");
                        
                        finalWidth = width;
                        finalHeight = height;
                    }
#endif

                    if (faces != null)
                    {
                        if (faces.Length > 0)
                        {
                            var mainFace = faces[0];
                            
                            swStage.Restart();
                            // Crop & align face
                            float[][,] faceImage = FaceProcessingExtensions.Align(imageMatrix, mainFace.Box, 0f, true);
                            swStage.Stop();
                            tAlign = swStage.ElapsedMilliseconds;
                            Debug.WriteLine($"[Telemetry] Face Alignment/Crop: {tAlign} ms");
                            
#if ANDROID
                            try
                            {
                                int faceH = faceImage[0].GetLength(0);
                                int faceW = faceImage[0].GetLength(1);
                                int[] facePixels = new int[faceW * faceH];
                                for (int y = 0; y < faceH; y++)
                                {
                                    for (int x = 0; x < faceW; x++)
                                    {
                                        int b = (int)(Math.Max(0f, Math.Min(1f, faceImage[0][y, x])) * 255);
                                        int g = (int)(Math.Max(0f, Math.Min(1f, faceImage[1][y, x])) * 255);
                                        int r = (int)(Math.Max(0f, Math.Min(1f, faceImage[2][y, x])) * 255);
                                        facePixels[y * faceW + x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                                    }
                                }
                                using var faceBitmap = global::Android.Graphics.Bitmap.CreateBitmap(facePixels, faceW, faceH, global::Android.Graphics.Bitmap.Config.Argb8888);
                                using var msFace = new MemoryStream();
                                faceBitmap.Compress(global::Android.Graphics.Bitmap.CompressFormat.Jpeg, 100, msFace);
                                croppedFaceBytes = msFace.ToArray();
                            }
                            catch (Exception cropEx)
                            {
                                Debug.WriteLine($"[Telemetry] Failed to encode cropped face: {cropEx.Message}");
                            }
#endif

                            swStage.Restart();
                            // Generate embedding
                            float[] embedding = _faceEmbedder.Forward(faceImage);
                            swStage.Stop();
                            tEmbed = swStage.ElapsedMilliseconds;
                            Debug.WriteLine($"[Telemetry] FaceEmbedder.Forward: {tEmbed} ms");
                            
                            swStage.Restart();
                            // Check for duplicates in the last 5 minutes (In-Memory Cache)
                            DateTime cutoffTime = DateTime.UtcNow.AddMinutes(-AppConstants.CooldownMinutes);
                            var recentRecords = await _storageService.GetRecentFaceRecordsAsync(cutoffTime);
                            
                            isDuplicateFinal = _matchingService.IsFaceDuplicate(embedding, recentRecords, AppConstants.SimilarityThreshold);
                            swStage.Stop();
                            tMatch = swStage.ElapsedMilliseconds;
                            Debug.WriteLine($"[Telemetry] In-Memory Matching: {tMatch} ms");

                            if (isDuplicateFinal)
                            {
                                statusText = "Ditolak: Duplikat Wajah!";
                            }
                            else
                            {
                                swStage.Restart();
                                // Convert float[] to byte[] for SQLite
                                byte[] embeddingBlob = new byte[embedding.Length * sizeof(float)];
                                Buffer.BlockCopy(embedding, 0, embeddingBlob, 0, embeddingBlob.Length);

                                // Save to database
                                var record = new FaceRecord
                                {
                                    Name = "Captured Face",
                                    EmbeddingBlob = embeddingBlob,
                                    DetectedAtUtc = DateTime.UtcNow
                                };
                                
                                await _storageService.SaveFaceRecordAsync(record);
                                swStage.Stop();
                                tSave = swStage.ElapsedMilliseconds;
                                Debug.WriteLine($"[Telemetry] Save to SQLite: {tSave} ms");

                                statusText = $"Berhasil disimpan! (Total {faces.Length} Wajah)";
                            }
                        }
                    }
                    
                    // Stop stopwatch immediately after heavy work!
                    _totalStopwatch?.Stop();
                    totalTimeFinal = _totalStopwatch?.ElapsedMilliseconds ?? 0;
                    Debug.WriteLine($"[Telemetry] TOTAL END-TO-END TIME (Core): {totalTimeFinal} ms");
                    
                }); // End of Task.Run

                // Update drawable size on main thread
                if (finalWidth > 0 && finalHeight > 0)
                {
                    _drawable.ImageWidth = finalWidth;
                    _drawable.ImageHeight = finalHeight;
                }

                // Append total time to status text
                statusText += $"\n[Waktu Proses: {totalTimeFinal} ms]";

                // Format breakdown text for Popup
                string alertTitle = isDuplicateFinal ? "Ditolak (Duplikat)" : (faces != null && faces.Length > 0 ? "Berhasil Disimpan" : "Tidak Ada Wajah");
                string alertBody = 
                    (isDuplicateFinal 
                        ? "Wajah sudah terdeteksi dalam 5 menit terakhir.\n\n" 
                        : (faces != null && faces.Length > 0 ? "Wajah baru berhasil disimpan.\n\n" : "Tidak ada wajah yang terdeteksi.\n\n")) +
                    "📊 BREAKDOWN WAKTU PROSES:\n" +
                    $"• 1. Shutter & Transfer : {tCamera} ms\n" +
                    $"• 2. Preprocessing (1:1): {tPreprocess} ms\n" +
                    $"• 3. Face Detection     : {tDetect} ms\n" +
                    $"• 4. Face Alignment     : {tAlign} ms\n" +
                    $"• 5. Face Embedding     : {tEmbed} ms\n" +
                    $"• 6. In-Memory Match    : {tMatch} ms\n" +
                    (isDuplicateFinal ? "• 7. Simpan Database    : [Dilewati - Duplikat]\n" : $"• 7. Simpan Database    : {tSave} ms\n") +
                    "------------------------------------\n" +
                    $"⏱️ TOTAL WAKTU MURNI    : {totalTimeFinal} ms";

                // Update UI on main thread
                await Dispatcher.DispatchAsync(() =>
                {
                    // Stop loading state
                    loadingIndicator.IsRunning = false;
                    loadingIndicator.IsVisible = false;

                    // Switch from live camera to static image container
                    cameraView.IsVisible = false;
                    resultContainer.IsVisible = true;
                    
                    // Reset slider state
                    sliderPreview.Value = 0;
                    sliderPreview.IsVisible = true;
                    
                    // Set image source
                    imgPreview.Source = ImageSource.FromStream(() => new MemoryStream(imageBytes));

                    if (croppedFaceBytes != null)
                    {
                        imgCroppedFace.Source = ImageSource.FromStream(() => new MemoryStream(croppedFaceBytes));
                    }

                    // Show retake button
                    btnRetake.IsVisible = true;
                    
                    // Update status text
                    lblStatus.Text = statusText;

                    // Draw bounding boxes
                    _drawable.Faces = faces;
                    graphicsView.Invalidate();
                    
                    // Show detailed breakdown alert to the user
                    DisplayAlert(alertTitle, alertBody, "OK");
                });


            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error detecting faces: {ex}");
                await Dispatcher.DispatchAsync(async () => 
                {
                    loadingIndicator.IsRunning = false;
                    loadingIndicator.IsVisible = false;
                    btnCapture.IsVisible = true;
                    btnSwitchCamera.IsVisible = true;
                    lblStatus.Text = $"Error: {ex.Message}";
                    await DisplayAlert("Error Saat Deteksi", $"{ex.GetType().Name}:\n{ex.Message}", "OK");
                });
            }
        }

        private void OnPreviewSliderValueChanged(object sender, ValueChangedEventArgs e)
        {
            if (e.NewValue < 0.5)
            {
                gridOriginal.IsVisible = true;
                gridCropped.IsVisible = false;
                lblPreviewCaption.Text = "Gambar Asli + Bounding Box";
            }
            else
            {
                gridOriginal.IsVisible = false;
                gridCropped.IsVisible = true;
                lblPreviewCaption.Text = "Gambar Potongan Wajah (Input Embedder)";
            }
        }
    }

    public class FaceBoundingBoxDrawable : IDrawable
    {
        public FaceDetectionResult[] Faces { get; set; }
        public int ImageWidth { get; set; } = 1;
        public int ImageHeight { get; set; } = 1;
        public bool IsMirrored { get; set; } = false;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Faces == null || Faces.Length == 0)
                return;

            // Calculate scale based on view size and image size
            float scaleX = dirtyRect.Width / ImageWidth;
            float scaleY = dirtyRect.Height / ImageHeight;

            // Draw bounding boxes
            canvas.StrokeColor = Colors.Red;
            canvas.StrokeSize = 3;

            foreach (var face in Faces)
            {
                var box = face.Box;
                // Scale coordinates
                float x = box.X * scaleX;
                float y = box.Y * scaleY;
                float width = box.Width * scaleX;
                float height = box.Height * scaleY;

                // Flip X for Front Camera mirroring if applicable
                if (IsMirrored)
                {
                    x = dirtyRect.Width - x - width;
                }

                canvas.DrawRectangle(x, y, width, height);
                
                // Optional: draw score
                canvas.FontColor = Colors.Red;
                canvas.FontSize = 14;
                canvas.DrawString($"{face.Score:P0}", x, y - 5, HorizontalAlignment.Left);
            }
        }
    }
}
