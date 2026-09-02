namespace FaceRecognitionExample
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(nameof(LiveTrackingPage), typeof(LiveTrackingPage));
        }
    }
}
