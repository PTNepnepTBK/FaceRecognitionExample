using System;
using System.Linq;
using System.Reflection;

class Program
{
    static void Main()
    {
        try
        {
            var assembly = Assembly.LoadFrom(@"C:\Users\TUF\.gemini\antigravity-ide\brain\7cfdf0f6-ed26-46ce-a75b-b8d452b2b9a4\scratch\TestUMapx\bin\Debug\net8.0-windows\FaceONNX.dll");
            
            var type = assembly.GetType("FaceONNX.FaceLandmarksExtractor");
            if (type != null) {
                foreach(var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)) {
                    Console.WriteLine("FaceLandmarksExtractor." + method.Name);
                    foreach(var param in method.GetParameters()) Console.WriteLine("  " + param.ParameterType.Name + " " + param.Name);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex.Message);
        }
    }
}
