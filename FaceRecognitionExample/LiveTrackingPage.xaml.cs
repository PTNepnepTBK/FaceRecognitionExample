using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using FaceRecognitionExample.Services;
using FaceRecognitionExample.Controls;
using FaceONNX;

namespace FaceRecognitionExample
{
    public partial class LiveTrackingPage : ContentPage
    {
        private YuNetFaceDetector _yuNetDetector;
        private FaceBoundingBoxDrawable _drawable;
        private bool _isProcessingFrame = false;

        public LiveTrackingPage()
        {
            InitializeComponent();
            
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
                var sw = Stopwatch.StartNew();
                float[][,] imageMatrix = null;
                FaceDetectionResult[] faces = null;
                int finalWidth = 0;
                int finalHeight = 0;

                // Move heavy decode & AI to background thread
                await Task.Run(() => 
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

                        int[] pixels = new int[width * height];
                        finalBitmap.GetPixels(pixels, 0, width, 0, 0, width, height);

                        // Run Inference directly from pixels
                        faces = _yuNetDetector.FastForward(pixels, width, height);

                        finalWidth = width;
                        finalHeight = height;
                    }
#endif
                });

                sw.Stop();

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

                    lblTime.Text = $"Proses: {sw.ElapsedMilliseconds} ms ({(1000.0/Math.Max(1, sw.ElapsedMilliseconds)):F1} FPS)";

                    if (faces != null && faces.Length > 0)
                    {
                        lblFaceInfo.Text = $"Ada Wajah: Ya ({faces.Length})";
                        lblFaceInfo.TextColor = Colors.LightGreen;
                        
                        var box = faces[0].Box;
                        lblBoxSize.Text = $"Ukuran: {box.Width:F1} x {box.Height:F1} px";
                    }
                    else
                    {
                        lblFaceInfo.Text = "Tidak ada wajah";
                        lblFaceInfo.TextColor = Colors.White;
                        lblBoxSize.Text = "Ukuran: -";
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
