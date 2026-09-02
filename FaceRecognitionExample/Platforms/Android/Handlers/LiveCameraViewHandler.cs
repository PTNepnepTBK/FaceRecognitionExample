using global::Android.Content;
using global::Android.Graphics;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using Java.Nio;
using Java.Util.Concurrent;
using Microsoft.Maui.Handlers;
using System;
using FaceRecognitionExample.Controls;

namespace FaceRecognitionExample.Platforms.Android.Handlers
{
    public partial class LiveCameraViewHandler : ViewHandler<LiveCameraView, PreviewView>
    {
        private ProcessCameraProvider _cameraProvider;
        private IExecutorService _cameraExecutor;
        private ImageAnalysis _imageAnalysis;

        public LiveCameraViewHandler(IPropertyMapper mapper, CommandMapper commandMapper = null) 
            : base(mapper, commandMapper)
        {
        }

        public LiveCameraViewHandler() : base(new PropertyMapper<LiveCameraView, LiveCameraViewHandler>())
        {
        }

        protected override PreviewView CreatePlatformView()
        {
            var previewView = new PreviewView(Context);
            return previewView;
        }

        protected override void ConnectHandler(PreviewView platformView)
        {
            base.ConnectHandler(platformView);
            _cameraExecutor = Executors.NewSingleThreadExecutor();
            StartCamera();
        }

        protected override void DisconnectHandler(PreviewView platformView)
        {
            if (_cameraProvider != null)
            {
                _cameraProvider.UnbindAll();
                _cameraProvider = null;
            }

            if (_cameraExecutor != null)
            {
                _cameraExecutor.Shutdown();
                _cameraExecutor = null;
            }

            base.DisconnectHandler(platformView);
        }

        private void StartCamera()
        {
            var cameraProviderFuture = ProcessCameraProvider.GetInstance(Context);
            cameraProviderFuture.AddListener(new Java.Lang.Runnable(() =>
            {
                _cameraProvider = (ProcessCameraProvider)cameraProviderFuture.Get();

                var preview = new Preview.Builder().Build();
                preview.SetSurfaceProvider(PlatformView.SurfaceProvider);

                _imageAnalysis = new ImageAnalysis.Builder()
                    .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)
                    .SetOutputImageFormat(2) // 2 = ImageAnalysis.OUTPUT_IMAGE_FORMAT_RGBA_8888
                    .Build();

                _imageAnalysis.SetAnalyzer(_cameraExecutor, new FrameAnalyzer(VirtualView));

                var cameraSelector = CameraSelector.DefaultFrontCamera; // Use front camera for face recog

                try
                {
                    _cameraProvider.UnbindAll();
                    
                    // Note: We need a LifecycleOwner. In MAUI, the current Activity is a LifecycleOwner.
                    var lifecycleOwner = (ILifecycleOwner)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                    
                    _cameraProvider.BindToLifecycle(lifecycleOwner, cameraSelector, preview, _imageAnalysis);
                }
                catch (Exception exc)
                {
                    System.Diagnostics.Debug.WriteLine($"Use case binding failed: {exc.Message}");
                }

            }), ContextCompat.GetMainExecutor(Context));
        }

        private class FrameAnalyzer : Java.Lang.Object, ImageAnalysis.IAnalyzer
        {
            private readonly LiveCameraView _virtualView;

            public FrameAnalyzer(LiveCameraView virtualView)
            {
                _virtualView = virtualView;
            }

            public global::Android.Util.Size DefaultTargetResolution => null;

            public void UpdateTransform(global::Android.Graphics.Matrix matrix)
            {
            }

            public void Analyze(IImageProxy image)
            {
                if (_virtualView == null)
                {
                    image.Close();
                    return;
                }

                try
                {
                    var plane = image.GetPlanes()[0];
                    var buffer = plane.Buffer;
                    int rowStride = plane.RowStride;
                    int width = image.Width;
                    int height = image.Height;

                    byte[] rgbaBytes;
                    if (rowStride == width * 4)
                    {
                        rgbaBytes = new byte[buffer.Remaining()];
                        buffer.Get(rgbaBytes);
                    }
                    else
                    {
                        rgbaBytes = new byte[width * height * 4];
                        for (int y = 0; y < height; y++)
                        {
                            buffer.Position(y * rowStride);
                            buffer.Get(rgbaBytes, y * width * 4, width * 4);
                        }
                    }

                    _virtualView.RaiseFrameReady(rgbaBytes, width, height, image.ImageInfo.RotationDegrees);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error analyzing frame: {ex.Message}");
                }
                finally
                {
                    image.Close(); // MUST close the image to receive the next one
                }
            }


        }
    }
}
