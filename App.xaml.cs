using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
#if WINDOWS
using Microsoft.UI.Windowing;
using WinRT.Interop;
using WinUIWindow = Microsoft.UI.Xaml.Window;
using WinUIWindowId = Microsoft.UI.WindowId;
using WindowsGraphicsSizeInt32 = Windows.Graphics.SizeInt32;
using Microsoft.UI; // 添加此命名空间以获取 AppWindowPresenterKind
#endif
using Application = Microsoft.Maui.Controls.Application;
using Window = Microsoft.Maui.Controls.Window;

namespace blender_selecter
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new MainPage());
#if WINDOWS
            // 在Windows平台上最大化窗口
            window.Created += (s, e) => {
                if (window.Handler?.PlatformView is WinUIWindow platformWindow)
                {
                    IntPtr windowHandle = WindowNative.GetWindowHandle(platformWindow);
                    WinUIWindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
                    AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
                    
                    // 使用OverlappedPresenter来最大化窗口
                    if (appWindow.Presenter is OverlappedPresenter presenter)
                    {
                        presenter.IsMaximizable = true;
                        presenter.Maximize();
                    }
                    else
                    {
                        // 如果Presenter不是OverlappedPresenter，则先设置为OverlappedPresenter
                        appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                        if (appWindow.Presenter is OverlappedPresenter newPresenter)
                        {
                            newPresenter.IsMaximizable = true;
                            newPresenter.Maximize();
                        }
                    }
                }
            };
#endif
            return window;
        }
    }
}