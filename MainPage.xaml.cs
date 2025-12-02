using System.Collections.ObjectModel;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Controls; // PointerGestureRecognizer 在此命名空间中

namespace blender_selecter;

public partial class MainPage : ContentPage
{
    private string selectedImagePath = "";
    public static string ImagePathFromArgs = "";
    private Point startPoint;
    private Point endPoint;
    private bool isDrawing = false;
    private Border? currentBox;
    private ObservableCollection<BoxSelection> selections = new ObservableCollection<BoxSelection>();
    private double imageWidth = 0;
    private double imageHeight = 0;

    public MainPage()
    {
        InitializeComponent();
        
        // 使用 .NET MAUI 9.0 的新 PointerGestureRecognizer
        var pointerGestureRecognizer = new PointerGestureRecognizer();
        pointerGestureRecognizer.PointerPressed += OnOverlayPointerPressed;
        pointerGestureRecognizer.PointerMoved += OnOverlayPointerMoved;
        pointerGestureRecognizer.PointerReleased += OnOverlayPointerReleased;
        OverlayLayout.GestureRecognizers.Add(pointerGestureRecognizer);

        // 注册点击手势用于取消正在进行的绘制
        var tapGestureRecognizer = new TapGestureRecognizer();
        tapGestureRecognizer.Tapped += OnImageTapped;
        OverlayLayout.GestureRecognizers.Add(tapGestureRecognizer);
        
        // 如果从命令行传入了图片路径，则自动加载
        if (!string.IsNullOrEmpty(ImagePathFromArgs))
        {
            LoadImageFromPath(ImagePathFromArgs);
        }
    }

    private void OnOverlayPointerPressed(object? sender, PointerEventArgs e)
    {
        // 获取相对于OverlayLayout的触摸点坐标
        var touchPoint = e.GetPosition(OverlayLayout);
        if (touchPoint.HasValue)
        {
            // 手指按下，开始绘制
            HandleTouchStart(touchPoint.Value.X, touchPoint.Value.Y);
        }
    }

    private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
    {
        // 只有在绘制状态下才处理移动事件
        if (isDrawing)
        {
            var touchPoint = e.GetPosition(OverlayLayout);
            if (touchPoint.HasValue)
            {
                // 手指移动，更新选框
                HandleTouchMove(touchPoint.Value.X, touchPoint.Value.Y);
            }
        }
    }

    private void OnOverlayPointerReleased(object? sender, PointerEventArgs e)
    {
        // 只有在绘制状态下才处理释放事件
        if (isDrawing)
        {
            // 手指抬起，完成绘制
            HandleTouchEnd();
        }
    }

    private async void OnSelectImageButtonClicked(object sender, EventArgs e)
    {
        var options = new PickOptions
        {
            PickerTitle = "Please select an image",
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg" } },
                    { DevicePlatform.Android, new[] { "image/png", "image/jpeg" } },
                    { DevicePlatform.iOS, new[] { "public.png", "public.jpeg" } },
                    { DevicePlatform.MacCatalyst, new[] { "png", "jpeg" } },
                    { DevicePlatform.Tizen, new[] { "*/*" } },
                })
        };

        try
        {
            var result = await FilePicker.Default.PickAsync(options);
            if (result != null)
            {
                LoadImageFromPath(result.FullPath);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to pick image: {ex.Message}", "OK");
        }
    }
    
    private async void LoadImageFromPath(string path)
    {
        selectedImagePath = path;
        SelectImageButton.Text = Path.GetFileName(selectedImagePath);
        
        try
        {
            // 获取图像尺寸
            using (var stream = new FileStream(selectedImagePath, FileMode.Open, FileAccess.Read))
            {
                var image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(stream);
                imageWidth = image.Width;
                imageHeight = image.Height;
            }
            
            // 加载图像
            MainImage.Source = ImageSource.FromFile(selectedImagePath);
            
            // 显示选择区域相关控件
            OnPropertyChanged(nameof(IsImageSelected));
            ClearSelectionsButton.IsEnabled = true;
            SendToServerButton.IsEnabled = true;
            
            // 清除之前的选框
            ClearSelections();
            
            // 等待布局完成后再设置覆盖层尺寸
            await Task.Delay(100); // 给UI一些时间来布局
            
            this.Dispatcher.Dispatch(() =>
            {
                // 设置覆盖层尺寸，与图片显示区域一致
                var imageBounds = GetImageDisplayBounds();
                OverlayLayout.WidthRequest = imageBounds.Width;
                OverlayLayout.HeightRequest = imageBounds.Height;
                OverlayLayout.TranslationX = imageBounds.X;
                OverlayLayout.TranslationY = imageBounds.Y;
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load image: {ex.Message}", "OK");
        }
    }

    private void OnImageTapped(object? sender, TappedEventArgs e)
    {
        // 点击清除当前正在进行的绘制
        if (isDrawing)
        {
            isDrawing = false;
            if (currentBox != null)
            {
                OverlayLayout.Children.Remove(currentBox);
                currentBox = null;
            }
        }
    }

    private void HandleTouchStart(double x, double y)
    {
        if (!string.IsNullOrEmpty(selectedImagePath))
        {
            isDrawing = true;
            
            // 限制起始点在图片显示区域内
            var imageBounds = GetImageDisplayBounds();
            startPoint = new Point(
                Math.Max(0, Math.Min(x, imageBounds.Width)),
                Math.Max(0, Math.Min(y, imageBounds.Height))
            );
            endPoint = startPoint;
            
            // 创建新的选框
            currentBox = new Border
            {
                Background = Colors.Transparent,
                Stroke = Colors.Red,
                StrokeThickness = 2,
            };
            
            // 设置初始位置和大小
            AbsoluteLayout.SetLayoutBounds(currentBox, new Rect(startPoint.X, startPoint.Y, 0, 0));
            AbsoluteLayout.SetLayoutFlags(currentBox, AbsoluteLayoutFlags.None);
            
            OverlayLayout.Children.Add(currentBox);
        }
    }

    private void HandleTouchMove(double x, double y)
    {
        if (isDrawing && currentBox != null)
        {
            // 限制移动点在图片显示区域内
            var imageBounds = GetImageDisplayBounds();
            endPoint = new Point(
                Math.Max(0, Math.Min(x, imageBounds.Width)),
                Math.Max(0, Math.Min(y, imageBounds.Height))
            );
            
            // 计算矩形框的位置和大小
            double left = Math.Min(startPoint.X, endPoint.X);
            double top = Math.Min(startPoint.Y, endPoint.Y);
            double width = Math.Abs(endPoint.X - startPoint.X);
            double height = Math.Abs(endPoint.Y - startPoint.Y);
            
            // 更新框的位置和大小
            AbsoluteLayout.SetLayoutBounds(currentBox, new Rect(left, top, width, height));
            AbsoluteLayout.SetLayoutFlags(currentBox, AbsoluteLayoutFlags.None);
        }
    }

    private void HandleTouchEnd()
    {
        if (isDrawing && currentBox != null)
        {
            isDrawing = false;
            
            // 获取图片显示的实际尺寸和位置
            var imageBounds = GetImageDisplayBounds();
            
            // 计算最终的框坐标
            double left = Math.Min(startPoint.X, endPoint.X);
            double top = Math.Min(startPoint.Y, endPoint.Y);
            double width = Math.Abs(endPoint.X - startPoint.X);
            double height = Math.Abs(endPoint.Y - startPoint.Y);
            
            // 只有当框足够大时才保留
            if (width > 10 && height > 10)
            {
                // 计算缩放比例
                double scaleX = imageWidth / imageBounds.Width;
                double scaleY = imageHeight / imageBounds.Height;

                // 保存选框信息（相对于原始图片的坐标）
                var selection = new BoxSelection
                {
                    X1 = left * scaleX,
                    Y1 = top * scaleY,
                    X2 = (left + width) * scaleX,
                    Y2 = (top + height) * scaleY
                };
                selections.Add(selection);
                
                // 创建新的框用于后续绘制
                currentBox = null;
            }
            else
            {
                // 移除太小的框
                OverlayLayout.Children.Remove(currentBox);
                currentBox = null;
            }
        }
    }

    private void HandleTouchCancel()
    {
        if (isDrawing && currentBox != null)
        {
            isDrawing = false;
            OverlayLayout.Children.Remove(currentBox);
            currentBox = null;
        }
    }

    private async void OnSendToServerClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(selectedImagePath))
        {
            await DisplayAlert("Error", "Please select an image first", "OK");
            return;
        }

        LoadingIndicator.IsRunning = true;
        SendToServerButton.IsEnabled = false;
        StatusMessage.Text = "Sending to server...";
        
        try
        {
            // 准备发送到服务器的数据
            var requestData = new
            {
                image_path = selectedImagePath,
                image_width = imageWidth,
                image_height = imageHeight,
                selections = selections.Select(s => new { 
                    x1 = s.X1, 
                    y1 = s.Y1, 
                    x2 = s.X2, 
                    y2 = s.Y2 
                }).ToArray()
            };
            
            // 这里是模拟与服务器通信的过程
            // 在实际应用中，您需要替换为真实的API调用
            await Task.Delay(2000); // 模拟网络延迟
            
            // 输出任务ID到控制台，供Blender插件读取
            string taskId = Guid.NewGuid().ToString();
            Console.WriteLine($"TASK_ID:{taskId}");
            
            // 更新UI
            StatusMessage.Text = "Successfully sent to server!";
            TaskIdLabel.Text = $"Task ID: {taskId}";
            TaskIdLabel.IsVisible = true;
            LoadingIndicator.IsRunning = false;
            
            // 启用重置按钮
            SendToServerButton.IsEnabled = true;
            SendToServerButton.Text = "Send Again";
        }
        catch (Exception ex)
        {
            LoadingIndicator.IsRunning = false;
            SendToServerButton.IsEnabled = true;
            await DisplayAlert("Error", $"Failed to send to server: {ex.Message}", "OK");
        }
    }
    
    private void OnClearSelectionsClicked(object sender, EventArgs e)
    {
        ClearSelections();
    }
    
    private void ClearSelections()
    {
        // 清除所有选框
        selections.Clear();
        
        // 移除所有绘制的框
        var boxesToRemove = OverlayLayout.Children.OfType<Border>().ToList();
        foreach (var box in boxesToRemove)
        {
            OverlayLayout.Children.Remove(box);
        }
        
        // 重置当前绘制状态
        isDrawing = false;
        currentBox = null;
    }
    
    public bool IsImageSelected => !string.IsNullOrEmpty(selectedImagePath);

    /// <summary>
    /// 获取图片在屏幕上显示的实际位置和尺寸
    /// </summary>
    /// <returns>图片显示的边界矩形</returns>
    private Rect GetImageDisplayBounds()
    {
        // 获取图片控件的尺寸
        double imageControlWidth = MainImage.Width;
        double imageControlHeight = MainImage.Height;

        // 如果控件尺寸未初始化，使用默认值
        if (imageControlWidth <= 0 || imageControlHeight <= 0)
        {
            imageControlWidth = 400; // 与XAML中Grid的HeightRequest一致
            imageControlHeight = 400;
        }

        // 获取原始图片的尺寸
        double originalImageWidth = imageWidth;
        double originalImageHeight = imageHeight;

        // 计算缩放比例（AspectFit模式）
        double scaleX = imageControlWidth / originalImageWidth;
        double scaleY = imageControlHeight / originalImageHeight;
        double scale = Math.Min(scaleX, scaleY);

        // 计算显示后的图片尺寸
        double displayWidth = originalImageWidth * scale;
        double displayHeight = originalImageHeight * scale;

        // 计算图片在控件中的偏移（居中显示）
        double offsetX = (imageControlWidth - displayWidth) / 2;
        double offsetY = (imageControlHeight - displayHeight) / 2;

        // 返回图片在覆盖层中的实际显示区域
        return new Rect(offsetX, offsetY, displayWidth, displayHeight);
    }
}

public class BoxSelection
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
}
