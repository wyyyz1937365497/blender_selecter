using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Controls; // PointerGestureRecognizer 在此命名空间中
using System.Net.Http;
using System.Net;
using System.Text;

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
    private readonly HttpClient httpClient = new HttpClient();
    private bool isImageLoading = false;

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

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

#if WINDOWS
        if (Window != null)
        {
            Window.SizeChanged += OnWindowSizeChanged;
        }
#endif
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
    
    private void LoadImageFromPath(string imagePath)
    {
        isImageLoading = true;
        selectedImagePath = imagePath;
        MainImage.Source = ImageSource.FromFile(imagePath);
        
        // 图片加载完成后调整尺寸
        MainImage.SizeChanged += OnMainImageSizeChanged;
    }
    
    private void OnMainImageSizeChanged(object? sender, EventArgs e)
    {
        if (MainImage.IsLoaded && MainImage.Width > 0 && MainImage.Height > 0)
        {
            // 图片加载成功后移除事件监听器
            MainImage.SizeChanged -= OnMainImageSizeChanged;
            isImageLoading = false;
            
#if WINDOWS
            // 调整图片尺寸以适应窗口
            AdjustImageSize();
#endif
        }
    }

#if WINDOWS
    private void OnWindowSizeChanged(object sender, EventArgs e)
    {
        if (MainImage.IsLoaded && !string.IsNullOrEmpty(selectedImagePath) && !isImageLoading)
        {
            AdjustImageSize();
        }
    }
    
    private void AdjustImageSize()
    {
        if (Window == null) return;
        
        // 使用Dispatcher确保在UI线程上执行
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // 动态计算除了图片区域外的其他控件所需高度
            double otherControlsHeight = 0;
            
            // 获取页面根布局的padding
            var rootLayout = this.Content as VerticalStackLayout;
            if (rootLayout != null)
            {
                otherControlsHeight += rootLayout.Padding.Top + rootLayout.Padding.Bottom;
            }
            
            // 加上控件间的间距
            otherControlsHeight += rootLayout?.Spacing * 8 ?? 15 * 8; // 大约8个间距
            
            // 加上可见控件的大致高度
            otherControlsHeight += SelectImageButton.HeightRequest > 0 ? SelectImageButton.HeightRequest : 45;
            otherControlsHeight += ClearSelectionsButton.HeightRequest > 0 ? ClearSelectionsButton.HeightRequest : 45;
            otherControlsHeight += LoadingIndicator.HeightRequest > 0 ? LoadingIndicator.HeightRequest : 40;
            
            // 加上标签的大致高度
            otherControlsHeight += 25 * 4; // 四个标签的高度
            
            // 计算可用于图片的垂直空间（留出一些边距）
            double availableHeight = Window.Height - otherControlsHeight - 20;
            
            // 设置一个合理的最小高度和最大高度
            if (availableHeight > 300)
            {
                ImageGrid.HeightRequest = Math.Min(availableHeight, Window.Height * 0.7); // 最多占窗口高度的70%
            }
            else if (Window.Height > 400)
            {
                ImageGrid.HeightRequest = Window.Height - 400;
            }
            else
            {
                ImageGrid.HeightRequest = 400;
            }
            
            ImageGrid.InvalidateMeasure();
        });
    }
#endif

    private void OnImageSizeChanged(object sender, EventArgs e)
    {
        // 当图片尺寸发生变化时保存尺寸信息
        imageWidth = MainImage.Width;
        imageHeight = MainImage.Height;
    }

    private void OnImageTapped(object? sender, TappedEventArgs e)
    {
        // 如果正在绘制，则取消绘制状态
        if (isDrawing)
        {
            CancelDrawing();
        }
    }

    private void CancelDrawing()
    {
        if (currentBox != null && OverlayLayout.Contains(currentBox))
        {
            OverlayLayout.Remove(currentBox);
            currentBox = null;
        }
        isDrawing = false;
    }

    private void HandleTouchStart(double x, double y)
    {
        // 只有当已有图片被选中时才允许绘制
        if (string.IsNullOrEmpty(selectedImagePath)) return;

        isDrawing = true;
        startPoint = new Point(x, y);
        endPoint = startPoint;

        // 创建新的选框
        currentBox = new Border
        {
            BackgroundColor = Colors.Transparent,
            Stroke = Colors.Red,
            StrokeThickness = 2,
            StrokeDashArray = [2, 2]
        };

        // 设置初始位置和大小
        UpdateBoxPositionAndSize();
        OverlayLayout.Add(currentBox);
    }

    private void HandleTouchMove(double x, double y)
    {
        if (!isDrawing) return;

        endPoint = new Point(x, y);
        UpdateBoxPositionAndSize();
    }

    private void HandleTouchEnd()
    {
        if (!isDrawing) return;

        // 计算最终矩形
        double width = Math.Abs(endPoint.X - startPoint.X);
        double height = Math.Abs(endPoint.Y - startPoint.Y);

        // 只有当矩形足够大时才保留它
        if (width > 10 && height > 10)
        {
            // 获取图片显示的实际尺寸和位置
            var imageBounds = GetImageDisplayBounds();

            // 计算缩放比例
            double scaleX = imageWidth / imageBounds.Width;
            double scaleY = imageHeight / imageBounds.Height;

            // 保存选框信息（相对于原始图片的坐标）
            var selection = new BoxSelection
            {
                X1 = Math.Min(startPoint.X, endPoint.X) * scaleX,
                Y1 = Math.Min(startPoint.Y, endPoint.Y) * scaleY,
                X2 = Math.Max(startPoint.X, endPoint.X) * scaleX,
                Y2 = Math.Max(startPoint.Y, endPoint.Y) * scaleY
            };
            selections.Add(selection);

            // 创建新的框用于后续绘制
            currentBox = null;
        }
        else
        {
            // 否则删除临时框
            if (currentBox != null)
            {
                OverlayLayout.Remove(currentBox);
            }
        }

        currentBox = null;
        isDrawing = false;
    }

    private void UpdateBoxPositionAndSize()
    {
        if (currentBox == null) return;

        double left = Math.Min(startPoint.X, endPoint.X);
        double top = Math.Min(startPoint.Y, endPoint.Y);
        double width = Math.Abs(endPoint.X - startPoint.X);
        double height = Math.Abs(endPoint.Y - startPoint.Y);

        // 设置边框的位置和大小
        AbsoluteLayout.SetLayoutBounds(currentBox, new Rect(left, top, width, height));
        AbsoluteLayout.SetLayoutFlags(currentBox, AbsoluteLayoutFlags.None);
    }

    /// <summary>
    /// 获取图片在界面上的实际显示区域
    /// </summary>
    /// <returns>图片显示区域的矩形</returns>
    private Rect GetImageDisplayBounds()
    {
        // 获取图片控件的大小
        var imageSize = new Size(MainImage.Width, MainImage.Height);

        // 考虑图片的 AspectFit 显示方式，计算实际显示区域
        double imageAspectRatio = imageWidth / imageHeight;
        double controlAspectRatio = imageSize.Width / imageSize.Height;

        double displayWidth, displayHeight;

        if (imageAspectRatio > controlAspectRatio)
        {
            // 图片较宽，以宽度为准
            displayWidth = imageSize.Width;
            displayHeight = imageSize.Width / imageAspectRatio;
        }
        else
        {
            // 图片较高，以高度为准
            displayHeight = imageSize.Height;
            displayWidth = imageSize.Height * imageAspectRatio;
        }

        // 计算居中显示时的偏移
        double offsetX = (imageSize.Width - displayWidth) / 2;
        double offsetY = (imageSize.Height - displayHeight) / 2;

        return new Rect(offsetX, offsetY, displayWidth, displayHeight);
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
        if (string.IsNullOrEmpty(selectedImagePath) || selections.Count == 0)
            return;

        LoadingIndicator.IsRunning = true;
        StatusMessage.Text = "Sending data to server...";
        SendToServerButton.IsEnabled = false;

        try
        {
            // 准备发送到服务器的数据
            var imageData = new ImageSelectionData
            {
                ImagePath = selectedImagePath,
                Selections = selections.ToList()
            };

            // 发送到FastAPI服务器
            var response = await SendToFastAPIServer(imageData);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var responseObject = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                
                if (responseObject != null && responseObject.ContainsKey("task_id"))
                {
                    string taskId = responseObject["task_id"].ToString();
                    TaskIdLabel.Text = $"Task ID: {taskId}";
                    TaskIdLabel.IsVisible = true;
                    StatusMessage.Text = "Successfully sent to server!";
                    Console.WriteLine($"TASK_ID:{taskId}");
                }
                else
                {
                    StatusMessage.Text = "Sent to server but received unexpected response";
                }
            }
            else
            {
                StatusMessage.Text = $"Failed to send to server. Error: {response.StatusCode}";
                SendToServerButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            StatusMessage.Text = $"Error: {ex.Message}";
            SendToServerButton.IsEnabled = true;
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
        }
    }
    
    private void OnClearSelectionsClicked(object sender, EventArgs e)
    {
        // 清除所有选框
        selections.Clear();
        OverlayLayout.Children.Clear();
        ClearSelectionsButton.IsEnabled = false;
        SendToServerButton.IsEnabled = false;
    }
    
    public bool IsImageSelected => !string.IsNullOrEmpty(selectedImagePath);
    
    private void ClearSelections()
    {
        selections.Clear();
        OverlayLayout.Children.Clear();
    }

    private async Task<HttpResponseMessage> SendToFastAPIServer(ImageSelectionData data)
    {
        try
        {
            // 构造请求数据
            var payload = new
            {
                image_path = data.ImagePath,
                selections = data.Selections.Select(s => new
                {
                    x1 = Math.Min(s.StartPoint.X, s.EndPoint.X),
                    y1 = Math.Min(s.StartPoint.Y, s.EndPoint.Y),
                    x2 = Math.Max(s.StartPoint.X, s.EndPoint.X),
                    y2 = Math.Max(s.StartPoint.Y, s.EndPoint.Y)
                }).ToArray()
            };

            // 序列化为JSON
            string json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // 发送POST请求到FastAPI服务器
            HttpResponseMessage response = await httpClient.PostAsync("http://localhost:8000/process", content);
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending to FastAPI server: {ex.Message}");
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }
    }
}

public class BoxSelection
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    
    public Point StartPoint { 
        get => new Point(X1, Y1); 
        set {
            X1 = value.X;
            Y1 = value.Y;
        }
    }
    public Point EndPoint { 
        get => new Point(X2, Y2); 
        set {
            X2 = value.X;
            Y2 = value.Y;
        }
    }
}

public class ImageSelectionData
{
    public string? ImagePath { get; set; }
    public List<BoxSelection> Selections { get; set; } = new List<BoxSelection>();
}
