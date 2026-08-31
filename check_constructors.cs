using System;
using System.Reflection;
using System.Linq;

class Program
{
    static void Main()
    {
        Assembly asm = Assembly.LoadFrom(@"FaceRecognitionExample\bin\Debug\net8.0-android\FaceONNX.dll");
        Type detector = asm.GetType("FaceONNX.FaceDetector");
        Type embedder = asm.GetType("FaceONNX.FaceEmbedder");

        Console.WriteLine("FaceDetector Constructors:");
        foreach(var c in detector.GetConstructors()) {
            var args = string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
            Console.WriteLine("  (" + args + ")");
        }

        Console.WriteLine("\nFaceEmbedder Constructors:");
        foreach(var c in embedder.GetConstructors()) {
            var args = string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
            Console.WriteLine("  (" + args + ")");
        }
    }
}
