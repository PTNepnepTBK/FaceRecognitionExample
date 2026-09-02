using System;

namespace FaceRecognitionExample.Controls
{
    public class LiveCameraView : View
    {
        // Event that gets fired when a frame is ready from the camera
        // The byte[] represents the raw RGB or JPEG data
        public event EventHandler<CameraFrameEventArgs> FrameReady;

        public void RaiseFrameReady(byte[] frameData, int width, int height, int rotation)
        {
            FrameReady?.Invoke(this, new CameraFrameEventArgs(frameData, width, height, rotation));
        }
    }

    public class CameraFrameEventArgs : EventArgs
    {
        public byte[] FrameData { get; }
        public int Width { get; }
        public int Height { get; }
        public int Rotation { get; }

        public CameraFrameEventArgs(byte[] frameData, int width, int height, int rotation)
        {
            FrameData = frameData;
            Width = width;
            Height = height;
            Rotation = rotation;
        }
    }
}
