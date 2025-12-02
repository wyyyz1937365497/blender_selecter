using Microsoft.Extensions.Logging;

namespace blender_selecter
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp(string[] args)
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            // 如果有命令行参数，则认为是图片路径
            if (args.Length > 0)
            {
                string imagePath = args[0];
                if (File.Exists(imagePath))
                {
                    // 将图片路径传递给MainPage
                    MainPage.ImagePathFromArgs = imagePath;
                }
                else
                {
                    // 输出错误信息到控制台
                    Console.WriteLine($"TASK_ID:CANCELLED - File not found: {imagePath}");
                }
            }

            return builder.Build();
        }
    }
}