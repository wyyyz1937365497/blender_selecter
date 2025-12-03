using Foundation;
using UIKit;

namespace blender_selecter
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp(Array.Empty<string>());

        public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
        {
            var result = base.FinishedLaunching(application, launchOptions);

            // 延迟执行全屏操作，确保窗口已创建
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(800); // 等待窗口完全初始化
                TryEnterFullScreen();
            });

            return result;
        }

        private void TryEnterFullScreen()
        {
            try
            {
                // 获取当前窗口场景并设置为全屏
                foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
                {
                    if (scene is UIWindowScene windowScene)
                    {
                        // 使用 performSelector 调用 toggleFullScreen
                        var windows = windowScene.Windows;
                        foreach (var window in windows)
                        {
                            // 通过 NSObject 的 performSelector 调用 AppKit 方法
                            var nsWindow = window.ValueForKey(new NSString("nsWindow"));
                            if (nsWindow != null)
                            {
                                nsWindow.PerformSelector(new ObjCRuntime.Selector("toggleFullScreen:"), null, 0.1);
                                Console.WriteLine("MacCatalyst: Toggled full screen via nsWindow");
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error entering full screen: {ex.Message}");
            }
        }
    }
}