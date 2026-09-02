using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using FaceRecognitionExample.Services;
using FaceRecognitionExample.Controls;
using FaceONNX;
using FaceRecognitionExample.Models;

namespace FaceRecognitionExample
{
    public partial class LiveTrackingPage : ContentPage
    {
        private YuNetFaceDetector _yuNetDetector;
        private FaceBoundingBoxDrawable _drawable;
        private bool _isProcessingFrame = false;

        private FaceEmbedder _faceEmbedder;
        private readonly IFaceStorageService _storageService;
        private readonly IFaceMatchingService _matchingService;

        // Rolling averages
        private Queue<long> _detectionTimes = new Queue<long>();
        private Queue<long> _pipelineTimes = new Queue<long>();
        private Queue<string> _pipelineHistory = new Queue<string>();
        private const int MaxSamples = 10;

        public LiveTrackingPage(IFaceStorageService storageService, IFaceMatchingService matchingService)
        {
            InitializeComponent();
            
            _storageService = storageService;
            _matchingService = matchingService;

            _drawable = new FaceBoundingBoxDrawable { IsMirrored = true };
            graphicsView.Drawable = _drawable;
            
            InitializeDetectorAsync();
        }

        private async void InitializeDetectorAsync()
        {
            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(AppConstants.LiveTrackingModelFile);
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                byte[] modelBytes = memoryStream.ToArray();
                
                _yuNetDetector = new YuNetFaceDetector(modelBytes, AppConstants.LiveTrackingModelSize, AppConstants.LiveTrackingModelSize);
                
                // Initialize Embedder
                _faceEmbedder = new FaceEmbedder();

                lblStatus.Text = "Model Siap! Tracking Aktif";
                lblStatus.TextColor = Colors.LightGreen;
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Error Load Model: {ex.Message}";
                lblStatus.TextColor = Colors.Red;
                Debug.WriteLine($"Failed to initialize YuNet: {ex}");
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

        protected override void OnAppearing()
        {
            base.OnAppearing();
            liveCamera.FrameReady += OnCameraFrameReady;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            liveCamera.FrameReady -= OnCameraFrameReady;
        }

        private async void OnCameraFrameReady(object sender, CameraFrameEventArgs e)
        {
            // Drop frames if we are still processing the previous one (to avoid bottleneck)
            if (_isProcessingFrame || _yuNetDetector == null) return;
            _isProcessingFrame = true;

            try
            {
                var swPipeline = Stopwatch.StartNew();
                var swDetect = new Stopwatch();

                FaceDetectionResult[] faces = null;
                int finalWidth = 0;
                int finalHeight = 0;
                bool pipelineExecuted = false;
                string pipelineResultMsg = null;

                // Move heavy decode & AI to background thread
                await Task.Run(async () => 
                {
#if ANDROID
                    int maxDim = AppConstants.LiveTrackingModelSize;

                    using var originalBitmap = global::Android.Graphics.Bitmap.CreateBitmap(e.Width, e.Height, global::Android.Graphics.Bitmap.Config.Argb8888);
                    using var buffer = Java.Nio.ByteBuffer.Wrap(e.FrameData);
                    originalBitmap.CopyPixelsFromBuffer(buffer);
                    
                    if (originalBitmap != null)
                    {
                        // 1:1 Center Crop source dimensions
                        int size = Math.Min(originalBitmap.Width, originalBitmap.Height);
                        int xOffset = (originalBitmap.Width - size) / 2;
                        int yOffset = (originalBitmap.Height - size) / 2;

                        var matrix = new global::Android.Graphics.Matrix();
                        matrix.PostRotate(e.Rotation);
                        
                        // Scale down directly to target size
                        float scale = (float)maxDim / size;
                        matrix.PostScale(scale, scale);

                        // Perform crop, rotate, and scale in a single step (1 allocation)
                        using var finalBitmap = global::Android.Graphics.Bitmap.CreateBitmap(originalBitmap, xOffset, yOffset, size, size, matrix, true);

                        int width = finalBitmap.Width;
                        int height = finalBitmap.Height;
                        finalWidth = width;
                        finalHeight = height;

                        int[] pixels = new int[width * height];
                        finalBitmap.GetPixels(pixels, 0, width, 0, 0, width, height);

                        // 1. Detection
                        swDetect.Start();
                        faces = _yuNetDetector.FastForward(pixels, width, height);
                        swDetect.Stop();

                        // 2. Full Pipeline (If faces detected)
                        if (faces != null && faces.Length > 0)
                        {
                            var largestFace = faces.OrderByDescending(f => f.Box.Width * f.Box.Height).First();
                            
                            // 3. Size Filter
                            if (largestFace.Box.Width >= AppConstants.MinFaceSizeThreshold || largestFace.Box.Height >= AppConstants.MinFaceSizeThreshold)
                            {
                                pipelineExecuted = true;
                                
                                // Build Image Matrix
                                float[][,] imageMatrix = new float[3][,];
                                imageMatrix[0] = new float[height, width];
                                imageMatrix[1] = new float[height, width];
                                imageMatrix[2] = new float[height, width];

                                for (int y = 0; y < height; y++)
                                {
                                    for (int x = 0; x < width; x++)
                                    {
                                        int color = pixels[y * width + x];
                                        imageMatrix[0][y, x] = (color & 0xff) / 255f;
                                        imageMatrix[1][y, x] = ((color >> 8) & 0xff) / 255f;
                                        imageMatrix[2][y, x] = ((color >> 16) & 0xff) / 255f;
                                    }
                                }

                                // 4. Align
                                float[][,] faceImage = FaceProcessingExtensions.Align(imageMatrix, largestFace.Box, 0f, true);
                                
                                // 5. Embed
                                float[] embedding = _faceEmbedder.Forward(faceImage);
                                
                                // 6. Match (Check Duplicate)
                                DateTime cutoffTime = DateTime.UtcNow.AddMinutes(-AppConstants.CooldownMinutes);
                                var recentRecords = await _storageService.GetRecentFaceRecordsAsync(cutoffTime);
                                bool isDuplicate = _matchingService.IsFaceDuplicate(embedding, recentRecords, AppConstants.SimilarityThreshold);

                                // 7. Save
                                if (!isDuplicate)
                                {
                                    byte[] embeddingBlob = new byte[embedding.Length * sizeof(float)];
                                    Buffer.BlockCopy(embedding, 0, embeddingBlob, 0, embeddingBlob.Length);

                                    var record = new FaceRecord
                                    {
                                        Name = "Live Tracked Face",
                                        EmbeddingBlob = embeddingBlob,
                                        DetectedAtUtc = DateTime.UtcNow
                                    };
                                    
                                    await _storageService.SaveFaceRecordAsync(record);
                                    pipelineResultMsg = "Wajah Baru Tersimpan";
                                }
                                else
                                {
                                    pipelineResultMsg = "Duplikat (< 5 Menit)";
                                }
                            }
                        }
                    }
#endif
                });

                swPipeline.Stop();

                // Update Telemetry Queues
                if (_detectionTimes.Count >= MaxSamples) _detectionTimes.Dequeue();
                _detectionTimes.Enqueue(swDetect.ElapsedMilliseconds);
                double avgDetect = _detectionTimes.Average();

                double avgPipeline = 0;
                if (pipelineExecuted)
                {
                    if (_pipelineTimes.Count >= MaxSamples) _pipelineTimes.Dequeue();
                    _pipelineTimes.Enqueue(swPipeline.ElapsedMilliseconds);
                }
                if (_pipelineTimes.Count > 0)
                {
                    avgPipeline = _pipelineTimes.Average();
                }

                // Update UI on Main Thread
                await Dispatcher.DispatchAsync(() => 
                {
                    if (finalWidth > 0 && finalHeight > 0)
                    {
                        _drawable.ImageWidth = finalWidth;
                        _drawable.ImageHeight = finalHeight;
                    }
                    _drawable.Faces = faces;
                    graphicsView.Invalidate();

                    lblTime.Text = $"Proses Total Frame: {swPipeline.ElapsedMilliseconds} ms";
                    lblAvgFps.Text = $"Avg Deteksi: {avgDetect:F1} ms ({(1000.0/Math.Max(1, avgDetect)):F1} FPS)";
                    lblAvgLatency.Text = _pipelineTimes.Count > 0 ? $"Avg Latensi (Full Pipeline): {avgPipeline:F1} ms" : "Avg Latensi (Full Pipeline): - ms";

                    if (faces != null && faces.Length > 0)
                    {
                        lblFaceInfo.Text = $"Ada Wajah: Ya ({faces.Length})";
                        lblFaceInfo.TextColor = pipelineExecuted ? Colors.Yellow : Colors.LightGreen;
                        
                        var box = faces.OrderByDescending(f => f.Box.Width * f.Box.Height).First().Box;
                        lblBoxSize.Text = $"Ukuran Terbesar: {box.Width:F1} x {box.Height:F1} px";
                    }
                    else
                    {
                        lblFaceInfo.Text = "Tidak ada wajah";
                        lblFaceInfo.TextColor = Colors.White;
                        lblBoxSize.Text = "Ukuran: -";
                    }

                    if (pipelineResultMsg != null)
                    {
                        string timestamp = DateTime.Now.ToString("HH:mm:ss");
                        _pipelineHistory.Enqueue($"{timestamp} - {pipelineResultMsg}");
                        if (_pipelineHistory.Count > 3)
                        {
                            _pipelineHistory.Dequeue();
                        }
                        lblPipelineResults.Text = string.Join("\n", _pipelineHistory.Reverse());
                    }
                });
            }
            finally
            {
                _isProcessingFrame = false;
            }
        }
    }
}
