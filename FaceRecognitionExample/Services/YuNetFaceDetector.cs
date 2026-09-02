using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using FaceONNX;
using System.IO;

namespace FaceRecognitionExample.Services
{
    public class YuNetFaceDetector : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly int _inputW;
        private readonly int _inputH;

        private static readonly FieldInfo BoxField = typeof(FaceDetectionResult).GetField("<Rectangle>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PointsField = typeof(FaceDetectionResult).GetField("<Points>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ScoreField = typeof(FaceDetectionResult).GetField("<Score>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Type Face5LandmarksType = typeof(FaceDetectionResult).Assembly.GetType("FaceONNX.Face5Landmarks");


        public YuNetFaceDetector(byte[] model, int inputW = 640, int inputH = 640)
        {
            var sessionOptions = new SessionOptions();
            sessionOptions.IntraOpNumThreads = 2; // Optimization
            _session = new InferenceSession(model, sessionOptions);
            _inputW = inputW;
            _inputH = inputH;
        }

        // Extremely fast forward directly from 1D int array (Android Bitmap Pixels)
        public FaceDetectionResult[] FastForward(int[] pixels, int w, int h)
        {
            var tensor = new DenseTensor<float>(new[] { 1, 3, _inputH, _inputW });
            var span = tensor.Buffer.Span;
            int strideG = _inputH * _inputW; // offset for G channel
            int strideR = 2 * _inputH * _inputW; // offset for R channel
            
            float scaleX = (float)w / _inputW;
            float scaleY = (float)h / _inputH;

            for (int y = 0; y < _inputH; y++)
            {
                int origY = (int)(y * scaleY);
                if (origY >= h) origY = h - 1;
                
                int yOffset = origY * w;
                int tensorYOffset = y * _inputW;
                
                for (int x = 0; x < _inputW; x++)
                {
                    int origX = (int)(x * scaleX);
                    if (origX >= w) origX = w - 1;

                    int color = pixels[yOffset + origX];
                    
                    int idx = tensorYOffset + x;
                    
                    // OpenCV YuNet expects BGR format 0-255.
                    span[idx] = (color & 0xff);                 // B
                    span[strideG + idx] = ((color >> 8) & 0xff);          // G
                    span[strideR + idx] = ((color >> 16) & 0xff);         // R
                }
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", tensor)
            };

            using var results = _session.Run(inputs);

            var candidates = new List<FaceDetectionResult>();
            float scoreThreshold = 0.6f;

            int[] strides = { 8, 16, 32 };
            foreach (int stride in strides)
            {
                var cls = results.First(r => r.Name == $"cls_{stride}").AsTensor<float>();
                var obj = results.First(r => r.Name == $"obj_{stride}").AsTensor<float>();
                var bbox = results.First(r => r.Name == $"bbox_{stride}").AsTensor<float>();
                var kps = results.First(r => r.Name == $"kps_{stride}").AsTensor<float>();

                int feature_w = _inputW / stride;
                int feature_h = _inputH / stride;

                for (int y = 0; y < feature_h; y++)
                {
                    for (int x = 0; x < feature_w; x++)
                    {
                        int idx = y * feature_w + x;
                        
                        // 2023mar outputs probabilities directly (already sigmoid)
                        float clsScore = cls[0, idx, 0];
                        float objScore = obj[0, idx, 0];
                        float score = (float)Math.Sqrt(clsScore * objScore);

                        if (score > scoreThreshold)
                        {
                            float cx = x * stride;
                            float cy = y * stride;
                            
                            float dx = bbox[0, idx, 0];
                            float dy = bbox[0, idx, 1];
                            float dw = bbox[0, idx, 2];
                            float dh = bbox[0, idx, 3];

                            // Bounding box decoding (OpenCV format)
                            float b_cx = cx + dx * stride;
                            float b_cy = cy + dy * stride;
                            float b_w = (float)Math.Exp(dw) * stride;
                            float b_h = (float)Math.Exp(dh) * stride;

                            float x1 = b_cx - b_w / 2;
                            float y1 = b_cy - b_h / 2;
                            float x2 = b_cx + b_w / 2;
                            float y2 = b_cy + b_h / 2;

                            x1 = Math.Max(0, Math.Min(w - 1, x1 * scaleX));
                            y1 = Math.Max(0, Math.Min(h - 1, y1 * scaleY));
                            x2 = Math.Max(0, Math.Min(w, x2 * scaleX));
                            y2 = Math.Max(0, Math.Min(h, y2 * scaleY));

                            int boxW = (int)(x2 - x1);
                            int boxH = (int)(y2 - y1);
                            if (boxW <= 0 || boxH <= 0) continue;

                            var points = new System.Drawing.Point[5];
                            for (int p = 0; p < 5; p++)
                            {
                                float px = cx + kps[0, idx, p * 2] * stride;
                                float py = cy + kps[0, idx, p * 2 + 1] * stride;
                                points[p] = new System.Drawing.Point(
                                    (int)Math.Max(0, Math.Min(w - 1, px * scaleX)), 
                                    (int)Math.Max(0, Math.Min(h - 1, py * scaleY))
                                );
                            }

                            var box = new System.Drawing.Rectangle((int)x1, (int)y1, boxW, boxH);
                            
                            object resultObj = Activator.CreateInstance(typeof(FaceDetectionResult));
                            object landmarksObj = Activator.CreateInstance(Face5LandmarksType, new object[] { points });

                            if (BoxField != null) BoxField.SetValue(resultObj, box);
                            if (PointsField != null) PointsField.SetValue(resultObj, landmarksObj);
                            if (ScoreField != null) ScoreField.SetValue(resultObj, score);

                            candidates.Add((FaceDetectionResult)resultObj);
                        }
                    }
                }
            }

            var nmsResults = NMS(candidates, 0.3f);
            return nmsResults.ToArray();
        }

        private List<FaceDetectionResult> NMS(List<FaceDetectionResult> faces, float nms_threshold)
        {
            var sorted = faces.OrderByDescending(f => f.Score).ToList();
            var results = new List<FaceDetectionResult>();

            while (sorted.Count > 0)
            {
                var best = sorted[0];
                results.Add(best);
                sorted.RemoveAt(0);

                for (int i = sorted.Count - 1; i >= 0; i--)
                {
                    if (IoU(best.Box, sorted[i].Box) > nms_threshold)
                    {
                        sorted.RemoveAt(i);
                    }
                }
            }
            return results;
        }

        private float IoU(System.Drawing.Rectangle a, System.Drawing.Rectangle b)
        {
            float x1 = Math.Max(a.Left, b.Left);
            float y1 = Math.Max(a.Top, b.Top);
            float x2 = Math.Min(a.Right, b.Right);
            float y2 = Math.Min(a.Bottom, b.Bottom);

            float interArea = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
            float unionArea = (a.Width * a.Height) + (b.Width * b.Height) - interArea;
            return interArea / unionArea;
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}
