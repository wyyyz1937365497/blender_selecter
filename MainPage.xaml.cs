using System.Collections.ObjectModel;

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
        
        // 注册触摸事件
        var tapGestureRecognizer = new TapGestureRecognizer();
        tapGestureRecognizer.Tapped += OnImageTapped;
        OverlayLayout.GestureRecognizers.Add(tapGestureRecognizer);
        
        var panGestureRecognizer = new PanGestureRecognizer();
        panGestureRecognizer.PanUpdated += OnImagePanUpdated;
        OverlayLayout.GestureRecognizers.Add(panGestureRecognizer);
        
        // 如果从命令行传入了图片路径，则自动加载
        if (!string.IsNullOrEmpty(ImagePathFromArgs))
        {
            LoadImageFromPath(ImagePathFromArgs);
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
            
            // 设置覆盖层尺寸
            OverlayLayout.WidthRequest = imageWidth;
            OverlayLayout.HeightRequest = imageHeight;
            
            // 显示选择区域相关控件
            OnPropertyChanged(nameof(IsImageSelected));
            ClearSelectionsButton.IsEnabled = true;
            SendToServerButton.IsEnabled = true;
            
            // 清除之前的选框
            ClearSelections();
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

    private void OnImagePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                HandleTouchStart(e.TotalX, e.TotalY);
                break;
            case GestureStatus.Running:
                HandleTouchMove(e.TotalX, e.TotalY);
                break;
            case GestureStatus.Completed:
                HandleTouchEnd();
                break;
            case GestureStatus.Canceled:
                HandleTouchCancel();
                break;
        }
    }

    private void HandleTouchStart(double x, double y)
    {
        if (!string.IsNullOrEmpty(selectedImagePath))
        {
            isDrawing = true;
            startPoint = new Point(x, y);
            endPoint = startPoint;
            
            // 创建新的选框
            currentBox = new Border
            {
                Background = Colors.Transparent,
                Stroke = Colors.Red,
                StrokeThickness = 2,
            };
            
            // 设置初始位置和大小为0
            Microsoft.Maui.Controls.AbsoluteLayout.SetLayoutBounds(currentBox, new Rect(startPoint.X, startPoint.Y, 0, 0));
            Microsoft.Maui.Controls.AbsoluteLayout.SetLayoutFlags(currentBox, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
            
            OverlayLayout.Children.Add(currentBox);
        }
    }

    private void HandleTouchMove(double x, double y)
    {
        if (isDrawing && currentBox != null)
        {
            endPoint = new Point(x, y);
            
            // 计算矩形框的位置和大小
            double left = Math.Min(startPoint.X, endPoint.X);
            double top = Math.Min(startPoint.Y, endPoint.Y);
            double width = Math.Abs(endPoint.X - startPoint.X);
            double height = Math.Abs(endPoint.Y - startPoint.Y);
            
            // 更新框的位置和大小
            Microsoft.Maui.Controls.AbsoluteLayout.SetLayoutBounds(currentBox, new Rect(left, top, width, height));
            Microsoft.Maui.Controls.AbsoluteLayout.SetLayoutFlags(currentBox, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        }
    }

    private void HandleTouchEnd()
    {
        if (isDrawing && currentBox != null)
        {
            isDrawing = false;
            
            // 计算最终的框坐标
            double left = Math.Min(startPoint.X, endPoint.X);
            double top = Math.Min(startPoint.Y, endPoint.Y);
            double width = Math.Abs(endPoint.X - startPoint.X);
            double height = Math.Abs(endPoint.Y - startPoint.Y);
            
            // 只有当框足够大时才保留
            if (width > 10 && height > 10)
            {
                // 保存选框信息
                var selection = new BoxSelection
                {
                    X1 = left,
                    Y1 = top,
                    X2 = left + width,
                    Y2 = top + height
                };
                selections.Add(selection);
                
                // 设置框的最终位置和大小
                Microsoft.Maui.Controls.AbsoluteLayout.SetLayoutBounds(currentBox, new Rect(left, top, width, height));
                Microsoft.Maui.Controls.AbsoluteLayout.SetLayoutFlags(currentBox, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
                
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
}

public class BoxSelection
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
}