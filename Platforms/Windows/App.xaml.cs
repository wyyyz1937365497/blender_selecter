using Microsoft.UI.Xaml;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace blender_selecter.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        private static string[]? Args;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp(Args ?? Array.Empty<string>());

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            string[] commandLineArgs = Environment.GetCommandLineArgs();
            // 第二个元素（索引为1）是图片路径参数
            if (commandLineArgs.Length > 1)
            {
                Args = new string[] { commandLineArgs[1] };
            }
            else
            {
                Args = Array.Empty<string>();
            }
            base.OnLaunched(args);
        }
    }
}