using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Controls; // PointerGestureRecognizer 在此命名空间中
using System.Net.Http;
using System.Net;
using System.Text;
using System.Net.Http.Headers;
using RestSharp;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace blender_selecter;

public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    private string _selectedImagePath = "";
    public static string ImagePathFromArgs = "";
    private Point startPoint;
    private Point endPoint;
    private bool isDrawing = false;
    private Border? currentBox;
    private ObservableCollection<BoxSelection> selections = new ObservableCollection<BoxSelection>();
    // 存储已完成的选框 UI 元素，用于窗口缩放时重新绘制
    private List<Border> completedBoxes = new List<Border>();
    private double imageWidth = 0;
    private double imageHeight = 0;
    private readonly HttpClient httpClient = new HttpClient();
    private bool isImageLoading = false;
    private ComfyUIServiceNew comfyUIService = new ComfyUIServiceNew();
    private bool isComfyUIProcessing = false;

    // 实现属性变更通知
    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // 使用属性包装器来触发 UI 更新
    private string selectedImagePath
    {
        get => _selectedImagePath;
        set
        {
            if (_selectedImagePath != value)
            {
                _selectedImagePath = value;
                OnPropertyChanged(nameof(IsImageSelected));
            }
        }
    }

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

        // 监听 OverlayLayout 的大小变化，用于重新绘制选框
        OverlayLayout.SizeChanged += OnOverlayLayoutSizeChanged;

        // 如果从命令行传入了图片路径，则自动加载
        if (!string.IsNullOrEmpty(ImagePathFromArgs))
        {
            LoadImageFromPath(ImagePathFromArgs);
        }

        // 初始化绑定上下文
        BindingContext = this;
    }

    private void OnOverlayLayoutSizeChanged(object? sender, EventArgs e)
    {
        // 当 OverlayLayout 大小变化时重新绘制选框
        if (selections.Count > 0 && completedBoxes.Count > 0 && !isImageLoading)
        {
            RedrawAllSelectionBoxes();
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

#if MACCATALYST
        if (Window != null)
        {
            Window.SizeChanged += OnWindowSizeChangedMac;
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
                    // macOS/MacCatalyst 使用 UTType 标识符
                    { DevicePlatform.MacCatalyst, new[] { "public.png", "public.jpeg", "public.image" } },
                    { DevicePlatform.Tizen, new[] { "*/*" } },
                })
        };

        try
        {
            var result = await FilePicker.Default.PickAsync(options);
            if (result != null)
            {
#if MACCATALYST
                // macOS 上需要通过 OpenReadAsync 获取流来读取文件
                await LoadImageFromFileResult(result);
#else
                LoadImageFromPath(result.FullPath);
#endif
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to pick image: {ex.Message}", "OK");
        }
    }

#if MACCATALYST
    private async Task LoadImageFromFileResult(FileResult result)
    {
        try
        {
            isImageLoading = true;
            
            // 复制文件到缓存目录
            var tempPath = Path.Combine(FileSystem.CacheDirectory, result.FileName);
            
            using (var sourceStream = await result.OpenReadAsync())
            using (var destStream = File.Create(tempPath))
            {
                await sourceStream.CopyToAsync(destStream);
            }
            
            selectedImagePath = tempPath;
            
            // 清除之前的选框
            ClearSelections();
            
            // 获取图片尺寸
            using (var imageStream = File.OpenRead(tempPath))
            {
#if MACCATALYST || IOS
                // 使用 UIKit 获取图片尺寸
                var imageData = Foundation.NSData.FromStream(imageStream);
                if (imageData != null)
                {
                    var uiImage = UIKit.UIImage.LoadFromData(imageData);
                    if (uiImage != null)
                    {
                        imageWidth = uiImage.Size.Width;
                        imageHeight = uiImage.Size.Height;
                    }
                }
#endif
            }
            
            // 使用文件流加载图片
            MainImage.Source = ImageSource.FromFile(tempPath);
            
            // 确保 ImageGrid 可见
            ImageGrid.IsVisible = true;
            
            isImageLoading = false;
            ClearSelectionsButton.IsEnabled = false; // 还没有选框
            
            // 如果已有 prompt，启用 OmniGen2 按钮
            if (!string.IsNullOrWhiteSpace(PromptEntry?.Text))
            {
                OmniGen2Button.IsEnabled = true;
            }
            
            Console.WriteLine($"Image loaded: {tempPath}, Size: {imageWidth}x{imageHeight}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading image: {ex.Message}");
            await DisplayAlert("Error", $"Failed to load image: {ex.Message}", "OK");
            isImageLoading = false;
        }
    }
#endif

    private void LoadImageFromPath(string imagePath)
    {
        isImageLoading = true;
        selectedImagePath = imagePath;

        // 清除之前的选框
        ClearSelections();

        MainImage.Source = ImageSource.FromFile(imagePath);

        // 确保 ImageGrid 可见
        ImageGrid.IsVisible = true;

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

            // 获取原始图片尺寸
            try
            {
                if (MainImage.Source is FileImageSource && !string.IsNullOrEmpty(selectedImagePath) && File.Exists(selectedImagePath))
                {
#if MACCATALYST || IOS
                    // macOS/iOS 使用 UIKit 获取图片尺寸
                    using var imageStream = File.OpenRead(selectedImagePath);
                    var imageData = Foundation.NSData.FromStream(imageStream);
                    if (imageData != null)
                    {
                        var uiImage = UIKit.UIImage.LoadFromData(imageData);
                        if (uiImage != null)
                        {
                            imageWidth = uiImage.Size.Width;
                            imageHeight = uiImage.Size.Height;
                        }
                    }
#else
                    var stream = File.OpenRead(selectedImagePath);
                    using var image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(stream);
                    imageWidth = image.Width;
                    imageHeight = image.Height;
                    stream.Close();
#endif
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting image dimensions: {ex.Message}");
                // 设置默认值
                imageWidth = MainImage.Width;
                imageHeight = MainImage.Height;
            }

            // 如果尺寸仍为0，使用控件尺寸
            if (imageWidth <= 0 || imageHeight <= 0)
            {
                imageWidth = MainImage.Width;
                imageHeight = MainImage.Height;
            }

#if WINDOWS
            // 调整图片尺寸以适应窗口
            AdjustImageSize();
#endif

#if MACCATALYST
            // macOS 上也调整图片尺寸
            AdjustImageSizeForMac();
#endif

            // 更新按钮状态
            ClearSelectionsButton.IsEnabled = true;

            Console.WriteLine($"Image size changed: {imageWidth}x{imageHeight}, Control size: {MainImage.Width}x{MainImage.Height}");
        }
    }

#if MACCATALYST
    private void AdjustImageSizeForMac()
    {
        // 使用 Grid 自动填充，不需要手动设置高度
        // 只需触发重绘选框
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RedrawAllSelectionBoxes();
        });
    }
    
    private void OnWindowSizeChangedMac(object? sender, EventArgs e)
    {
        if (MainImage.IsLoaded && !string.IsNullOrEmpty(selectedImagePath) && !isImageLoading)
        {
            // 窗口大小变化时重新绘制所有选框
            RedrawAllSelectionBoxes();
        }
    }
#endif

    /// <summary>
    /// 根据归一化坐标重新绘制所有选框（转换为当前图片显示区域的绝对坐标）
    /// </summary>
    private void RedrawAllSelectionBoxes()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                // 获取当前图片显示区域
                var imageBounds = GetImageDisplayBounds();

                for (int i = 0; i < selections.Count && i < completedBoxes.Count; i++)
                {
                    var selection = selections[i];
                    var box = completedBoxes[i];

                    // 将归一化坐标转换为当前的绝对坐标
                    double left = imageBounds.X + selection.NormalizedX1 * imageBounds.Width;
                    double top = imageBounds.Y + selection.NormalizedY1 * imageBounds.Height;
                    double width = (selection.NormalizedX2 - selection.NormalizedX1) * imageBounds.Width;
                    double height = (selection.NormalizedY2 - selection.NormalizedY1) * imageBounds.Height;

                    // 确保最小尺寸
                    width = Math.Max(1, width);
                    height = Math.Max(1, height);

                    AbsoluteLayout.SetLayoutBounds(box, new Rect(left, top, width, height));
                    AbsoluteLayout.SetLayoutFlags(box, AbsoluteLayoutFlags.None);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error redrawing selection boxes: {ex.Message}");
            }
        });
    }

#if WINDOWS
    private void OnWindowSizeChanged(object? sender, EventArgs e)
    {
        if (MainImage.IsLoaded && !string.IsNullOrEmpty(selectedImagePath) && !isImageLoading)
        {
            AdjustImageSize();
            // 窗口大小变化时重新绘制所有选框
            RedrawAllSelectionBoxes();
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
        RemoveCurrentBox();
        isDrawing = false;
    }

    private void HandleTouchStart(double x, double y)
    {
        // 只有当已有图片被选中时才允许绘制
        if (string.IsNullOrEmpty(selectedImagePath))
        {
            Console.WriteLine("HandleTouchStart: No image selected");
            return;
        }

        // 获取图片显示区域
        var imageBounds = GetImageDisplayBounds();

        Console.WriteLine($"HandleTouchStart: touch=({x}, {y}), imageBounds=({imageBounds.X}, {imageBounds.Y}, {imageBounds.Width}, {imageBounds.Height})");

        // 检查触摸点是否在图片区域内（如果图片尺寸有效）
        if (imageWidth > 0 && imageHeight > 0)
        {
            if (x < imageBounds.X || x > imageBounds.X + imageBounds.Width ||
                y < imageBounds.Y || y > imageBounds.Y + imageBounds.Height)
            {
                // 触摸点不在图片区域内，不响应
                Console.WriteLine("HandleTouchStart: Touch point outside image bounds");
                return;
            }
        }

        isDrawing = true;
        startPoint = new Point(x, y);
        endPoint = startPoint;

        Console.WriteLine($"HandleTouchStart: Starting drawing at ({x}, {y})");

        // 创建新的选框（会使用比例布局）
        currentBox = new Border
        {
            BackgroundColor = Colors.Transparent,
            Stroke = Colors.Red,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection(new double[] { 2, 2 })
        };

        // 设置初始位置和大小（使用比例坐标）
        UpdateBoxPositionAndSize();

        // 将框添加到 OverlayLayout 上（使用绝对坐标绘制）
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (currentBox != null && !OverlayLayout.Children.Contains(currentBox))
                {
                    OverlayLayout.Children.Add(currentBox);
                    Console.WriteLine("HandleTouchStart: Box added to overlay");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding box to overlay: {ex.Message}");
                isDrawing = false;
                currentBox = null;
            }
        });
    }

    private void HandleTouchMove(double x, double y)
    {
        if (!isDrawing) return;

        // 获取图片显示区域
        var imageBounds = GetImageDisplayBounds();

        // 将坐标限制在图片边界内（仅当图片尺寸有效时）
        double clampedX = x;
        double clampedY = y;

        if (imageWidth > 0 && imageHeight > 0)
        {
            clampedX = Math.Max(imageBounds.X, Math.Min(x, imageBounds.X + imageBounds.Width));
            clampedY = Math.Max(imageBounds.Y, Math.Min(y, imageBounds.Y + imageBounds.Height));
        }

        endPoint = new Point(clampedX, clampedY);
        UpdateBoxPositionAndSize();
    }

    private void HandleTouchEnd()
    {
        if (!isDrawing) return;

        Console.WriteLine($"HandleTouchEnd: startPoint=({startPoint.X}, {startPoint.Y}), endPoint=({endPoint.X}, {endPoint.Y})");

        // 获取图片显示的实际尺寸和位置
        var imageBounds = GetImageDisplayBounds();

        // 将结束点也限制在图片边界内（仅当图片尺寸有效时）
        if (imageWidth > 0 && imageHeight > 0)
        {
            double clampedEndX = Math.Max(imageBounds.X, Math.Min(endPoint.X, imageBounds.X + imageBounds.Width));
            double clampedEndY = Math.Max(imageBounds.Y, Math.Min(endPoint.Y, imageBounds.Y + imageBounds.Height));
            endPoint = new Point(clampedEndX, clampedEndY);

            // 同样限制起始点
            double clampedStartX = Math.Max(imageBounds.X, Math.Min(startPoint.X, imageBounds.X + imageBounds.Width));
            double clampedStartY = Math.Max(imageBounds.Y, Math.Min(startPoint.Y, imageBounds.Y + imageBounds.Height));
            startPoint = new Point(clampedStartX, clampedStartY);
        }

        // 计算最终矩形
        double width = Math.Abs(endPoint.X - startPoint.X);
        double height = Math.Abs(endPoint.Y - startPoint.Y);

        Console.WriteLine($"HandleTouchEnd: box size = {width} x {height}");

        // 只有当矩形足够大时才保留它
        if (width > 10 && height > 10)
        {
            // 计算缩放比例（处理图片尺寸未知的情况）
            double scaleX = imageWidth > 0 ? imageWidth / imageBounds.Width : 1;
            double scaleY = imageHeight > 0 ? imageHeight / imageBounds.Height : 1;

            // 保存选框信息（相对于原始图片的坐标）
            var selection = new BoxSelection
            {
                X1 = (Math.Min(startPoint.X, endPoint.X) - imageBounds.X) * scaleX,
                Y1 = (Math.Min(startPoint.Y, endPoint.Y) - imageBounds.Y) * scaleY,
                X2 = (Math.Max(startPoint.X, endPoint.X) - imageBounds.X) * scaleX,
                Y2 = (Math.Max(startPoint.Y, endPoint.Y) - imageBounds.Y) * scaleY,
                // 保存归一化坐标（相对于图片显示区域的百分比）
                NormalizedX1 = imageBounds.Width > 0 ? (Math.Min(startPoint.X, endPoint.X) - imageBounds.X) / imageBounds.Width : 0,
                NormalizedY1 = imageBounds.Height > 0 ? (Math.Min(startPoint.Y, endPoint.Y) - imageBounds.Y) / imageBounds.Height : 0,
                NormalizedX2 = imageBounds.Width > 0 ? (Math.Max(startPoint.X, endPoint.X) - imageBounds.X) / imageBounds.Width : 1,
                NormalizedY2 = imageBounds.Height > 0 ? (Math.Max(startPoint.Y, endPoint.Y) - imageBounds.Y) / imageBounds.Height : 1
            };
            selections.Add(selection);

            Console.WriteLine($"HandleTouchEnd: Selection added, total selections = {selections.Count}");

            // 有选框后启用 3D 重建按钮和清除按钮
            Midi3DButton.IsEnabled = true;
            ClearSelectionsButton.IsEnabled = true;

            // 保留框在画布上
            if (currentBox != null)
            {
                // 为已完成的框设置实心边框而不是虚线边框
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        currentBox.StrokeDashArray = null; // 使用实线边框
                        currentBox.StrokeThickness = 2;

                        // 为每个框设置不同颜色以便区分
                        Color[] colors = { Colors.Red, Colors.Blue, Colors.Green, Colors.Yellow, Colors.Magenta, Colors.Cyan };
                        int colorIndex = (selections.Count - 1) % colors.Length;
                        currentBox.Stroke = colors[colorIndex];

                        // 将已完成的选框添加到列表中（保持绝对坐标，窗口缩放时会重绘）
                        completedBoxes.Add(currentBox);
                        Console.WriteLine($"HandleTouchEnd: Box finalized with color index {colorIndex}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error updating completed box appearance: {ex.Message}");
                    }
                });
            }
        }
        else
        {
            Console.WriteLine("HandleTouchEnd: Box too small, removing");
            // 删除太小的临时框
            RemoveCurrentBox();
        }

        currentBox = null;
        isDrawing = false;
    }

    private void RemoveCurrentBox()
    {
        if (currentBox != null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (OverlayLayout.Children.Contains(currentBox))
                    {
                        OverlayLayout.Children.Remove(currentBox);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error removing box from overlay: {ex.Message}");
                }
                finally
                {
                    currentBox = null;
                }
            });
        }
    }

    private void UpdateBoxPositionAndSize()
    {
        if (currentBox == null) return;

        // 获取图片显示区域
        var imageBounds = GetImageDisplayBounds();

        // 计算矩形的左上角和尺寸（基于起点和终点）
        // 起点固定不动，终点跟随鼠标
        double left = Math.Min(startPoint.X, endPoint.X);
        double top = Math.Min(startPoint.Y, endPoint.Y);
        double width = Math.Abs(endPoint.X - startPoint.X);
        double height = Math.Abs(endPoint.Y - startPoint.Y);

        // 确保最小尺寸
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);

        // 绘制时使用绝对坐标（相对于 OverlayLayout），不使用比例坐标
        // 这样起点才不会移动
        try
        {
            AbsoluteLayout.SetLayoutBounds(currentBox, new Rect(left, top, width, height));
            AbsoluteLayout.SetLayoutFlags(currentBox, AbsoluteLayoutFlags.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating box position and size: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取图片在界面上的实际显示区域（相对于 OverlayLayout）
    /// </summary>
    /// <returns>图片显示区域的矩形</returns>
    private Rect GetImageDisplayBounds()
    {
        // 使用 OverlayLayout 的尺寸作为容器尺寸，因为触摸坐标是相对于它的
        double containerWidth = OverlayLayout.Width;
        double containerHeight = OverlayLayout.Height;

        // 如果 OverlayLayout 尺寸无效，尝试使用 ImageGrid 的尺寸
        if (containerWidth <= 0 || containerHeight <= 0)
        {
            containerWidth = ImageGrid.Width;
            containerHeight = ImageGrid.Height;
        }

        // 如果图片尺寸未知或容器尺寸无效，返回整个容器区域
        if (imageWidth <= 0 || imageHeight <= 0 || containerWidth <= 0 || containerHeight <= 0)
        {
            Console.WriteLine($"GetImageDisplayBounds: Invalid dimensions - imageWidth={imageWidth}, imageHeight={imageHeight}, containerWidth={containerWidth}, containerHeight={containerHeight}");
            return new Rect(0, 0, containerWidth > 0 ? containerWidth : 400, containerHeight > 0 ? containerHeight : 400);
        }

        // 考虑图片的 AspectFit 显示方式，计算实际显示区域
        double imageAspectRatio = imageWidth / imageHeight;
        double containerAspectRatio = containerWidth / containerHeight;

        double displayWidth, displayHeight;

        if (imageAspectRatio > containerAspectRatio)
        {
            // 图片较宽，以宽度为准
            displayWidth = containerWidth;
            displayHeight = containerWidth / imageAspectRatio;
        }
        else
        {
            // 图片较高，以高度为准
            displayHeight = containerHeight;
            displayWidth = containerHeight * imageAspectRatio;
        }

        // 计算居中显示时的偏移（相对于 OverlayLayout/容器）
        double offsetX = (containerWidth - displayWidth) / 2;
        double offsetY = (containerHeight - displayHeight) / 2;

        Console.WriteLine($"GetImageDisplayBounds: container=({containerWidth}, {containerHeight}), display=({displayWidth}, {displayHeight}), offset=({offsetX}, {offsetY})");
        return new Rect(offsetX, offsetY, displayWidth, displayHeight);
    }

    private void HandleTouchCancel()
    {
        // 只有在绘制状态下且存在当前绘制框时才进行移除
        if (isDrawing && currentBox != null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    // 从 OverlayLayout 中移除当前正在绘制的框
                    if (OverlayLayout.Children.Contains(currentBox))
                    {
                        OverlayLayout.Children.Remove(currentBox);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling touch cancel: {ex.Message}");
                }
                finally
                {
                    // 重置状态
                    isDrawing = false;
                    currentBox = null;
                }
            });
        }
    }

    private async void OnOmniGen2EditClicked(object sender, EventArgs e)
    {
        // ComfyUI 只需要图片和 prompt，不需要选框
        if (string.IsNullOrEmpty(selectedImagePath))
            return;

        // 获取用户输入的 prompt
        string userPrompt = PromptEntry?.Text ?? "";
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            await DisplayAlert("Prompt Required", "Please enter a prompt describing what you want to generate.", "OK");
            return;
        }

        // 防止重复处理
        if (isComfyUIProcessing)
        {
            await DisplayAlert("Processing", "ComfyUI is already processing an image. Please wait for it to complete.", "OK");
            return;
        }

        isComfyUIProcessing = true;
        LoadingIndicator.IsRunning = true;
        StatusMessage.Text = "🎨 Uploading image to ComfyUI...";
        OmniGen2Button.IsEnabled = false;

        try
        {
            Console.WriteLine($"Prompt: {userPrompt}");

            // 1. 上传图片到ComfyUI
            var uploadResult = await comfyUIService.UploadImageAsync(selectedImagePath);
            string imageName = uploadResult.ContainsKey("name") ? uploadResult["name"].ToString() : "";
            
            if (string.IsNullOrEmpty(imageName))
            {
                throw new Exception("Failed to upload image to ComfyUI");
            }

            Console.WriteLine($"Image uploaded successfully: {imageName}");
            StatusMessage.Text = "🎨 Image uploaded, loading workflow...";

            // 2. 从资源加载工作流
            var workflow = comfyUIService.LoadWorkflowFromResource();

            // 3. 替换工作流中的文本和图片
            workflow = comfyUIService.ReplacePromptInWorkflow(workflow, userPrompt, imageName);

            // 4. 提交任务
            StatusMessage.Text = "🎨 Submitting task to ComfyUI...";
            var (promptId, clientId) = await comfyUIService.QueuePromptAsync(workflow);
            Console.WriteLine($"Task submitted, ID: {promptId}");

            // 5. 等待任务完成并显示进度
            StatusMessage.Text = "🎨 Processing image with ComfyUI...";
            bool completed = await comfyUIService.WaitForCompletionAsync(promptId, clientId, progress => 
            {
                MainThread.BeginInvokeOnMainThread(() => 
                {
                    StatusMessage.Text = $"🎨 Processing with ComfyUI... {progress}%";
                });
            });

            if (!completed)
            {
                throw new Exception("ComfyUI task did not complete successfully");
            }

            // 6. 获取结果图片
            StatusMessage.Text = "🎨 Retrieving generated image...";
            var history = await comfyUIService.GetHistoryAsync(promptId);
            
            // 创建输出目录
            string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 保存结果图片
            bool imageSaved = false;
            string savedImagePath = "";
            
            if (history.ContainsKey(promptId))
            {
                var promptHistory = JsonSerializer.Deserialize<Dictionary<string, object>>(history[promptId].ToString());
                if (promptHistory != null && promptHistory.ContainsKey("outputs"))
                {
                    var outputs = JsonSerializer.Deserialize<Dictionary<string, object>>(promptHistory["outputs"].ToString());
                    
                    foreach (var nodeOutput in outputs)
                    {
                        var nodeData = JsonSerializer.Deserialize<Dictionary<string, object>>(nodeOutput.Value.ToString());
                        if (nodeData.ContainsKey("images"))
                        {
                            var images = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(nodeData["images"].ToString());
                            
                            foreach (var image in images)
                            {
                                string filename = image["filename"].ToString();
                                string subfolder = image.ContainsKey("subfolder") ? image["subfolder"].ToString() : "";
                                string folderType = image.ContainsKey("type") ? image["type"].ToString() : "output";
                                
                                savedImagePath = Path.Combine(outputDir, $"output_{filename}");
                                imageSaved = await comfyUIService.DownloadImageAsync(filename, subfolder, folderType, savedImagePath);
                                
                                if (imageSaved)
                                {
                                    Console.WriteLine($"Image saved to: {savedImagePath}");
                                    break; // 只保存第一张图片
                                }
                            }
                        }
                        
                        if (imageSaved) break;
                    }
                }
            }

            if (!imageSaved || string.IsNullOrEmpty(savedImagePath))
            {
                throw new Exception("Failed to retrieve generated image from ComfyUI");
            }

            // 7. 更新UI显示新图片
            MainThread.BeginInvokeOnMainThread(() => 
            {
                selectedImagePath = savedImagePath;
                MainImage.Source = ImageSource.FromFile(savedImagePath);
                StatusMessage.Text = "✨ Image edited successfully! You can now draw boxes for 3D reconstruction.";
                StatusMessage.TextColor = Colors.Green;

                // 显示选框提示
                SelectionHintLabel.IsVisible = true;

                // 启用 3D 重建按钮
                Midi3DButton.IsEnabled = true;
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
            MainThread.BeginInvokeOnMainThread(() => 
            {
                StatusMessage.Text = $"Error: {ex.Message}";
                StatusMessage.TextColor = Colors.Red;
            });
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() => 
            {
                LoadingIndicator.IsRunning = false;
                OmniGen2Button.IsEnabled = true;
                isComfyUIProcessing = false;
            });
        }
    }

    private async void OnMidi3DRebuildClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(selectedImagePath) || selections.Count == 0)
            return;

        LoadingIndicator.IsRunning = true;
        StatusMessage.Text = "Sending data to MIDI-3D server...";
        Midi3DButton.IsEnabled = false;

        try
        {
            var boxes = selections.Select(s => new BoundingBox
            {
                x1 = (int)Math.Round(Math.Min(s.X1, s.X2)),
                y1 = (int)Math.Round(Math.Min(s.Y1, s.Y2)),
                x2 = (int)Math.Round(Math.Max(s.X1, s.X2)),
                y2 = (int)Math.Round(Math.Max(s.Y1, s.Y2))
            }).ToList();

            var boxesJson = JsonSerializer.Serialize(boxes);
            Console.WriteLine($"Boxes JSON: {boxesJson}");

            // 创建RestClient - 连接到 MIDI-3D 服务
            var client = new RestClient("http://127.0.0.1:8000");

            // 创建请求
            var request = new RestRequest("/process", Method.Post);

            // 添加查询参数
            request.AddParameter("seg_mode", "box");
            request.AddParameter("boxes_json", boxesJson);
            request.AddParameter("polygon_refinement", "true");
            request.AddParameter("detect_threshold", "0.3");

            // 添加文件
            request.AddFile("file", selectedImagePath, "image/jpeg");

            Console.WriteLine($"Sending request to MIDI-3D: {client.BuildUri(request)}");

            // 发送请求
            var response = await client.ExecuteAsync(request);

            if (response.IsSuccessful)
            {
                Console.WriteLine($"Server response: {response.Content}");

                var responseObject = JsonSerializer.Deserialize<Dictionary<string, object>>(response.Content);

                if (responseObject != null && responseObject.ContainsKey("task_id"))
                {
                    var taskId = responseObject["task_id"].ToString();
                    TaskIdLabel.Text = $"MIDI-3D Task ID: {taskId}";
                    TaskIdLabel.IsVisible = true;
                    StatusMessage.Text = "🧊 3D reconstruction started!";
                    StatusMessage.TextColor = Colors.Green;
                }
                else
                {
                    StatusMessage.Text = "Server returned unexpected response";
                    StatusMessage.TextColor = Colors.Orange;
                }
            }
            else
            {
                Console.WriteLine($"Error response: {response.Content}");
                StatusMessage.Text = $"Server error: {response.StatusCode}";
                StatusMessage.TextColor = Colors.Red;
            }

            Midi3DButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
            StatusMessage.Text = $"Error: {ex.Message}";
            StatusMessage.TextColor = Colors.Red;
            Midi3DButton.IsEnabled = true;
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
        }
    }

    private void OnClearSelectionsClicked(object sender, EventArgs e)
    {
        // 清除所有选框
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                selections.Clear();
                // 从 OverlayLayout 中移除所有已完成的选框
                foreach (var box in completedBoxes)
                {
                    if (OverlayLayout.Children.Contains(box))
                    {
                        OverlayLayout.Children.Remove(box);
                    }
                }
                completedBoxes.Clear();
                ClearSelectionsButton.IsEnabled = false;
                // OmniGen2 按钮状态由 prompt 控制，不在这里改变
                Midi3DButton.IsEnabled = false;
                SelectionHintLabel.IsVisible = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing selections: {ex.Message}");
            }
        });
    }

    // Prompt 文本变化时更新 OmniGen2 按钮状态
    private void OnPromptTextChanged(object sender, TextChangedEventArgs e)
    {
        // OmniGen2 只需要图片 + prompt
        bool hasImage = !string.IsNullOrEmpty(selectedImagePath);
        bool hasPrompt = !string.IsNullOrWhiteSpace(PromptEntry.Text);
        OmniGen2Button.IsEnabled = hasImage && hasPrompt;
    }

    public bool IsImageSelected => !string.IsNullOrEmpty(selectedImagePath);

    private void ClearSelections()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                selections.Clear();
                // 从 OverlayLayout 中移除所有已完成的选框
                foreach (var box in completedBoxes)
                {
                    if (OverlayLayout.Children.Contains(box))
                    {
                        OverlayLayout.Children.Remove(box);
                    }
                }
                completedBoxes.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing selections: {ex.Message}");
            }
        });
    }

    private async Task<HttpResponseMessage> SendToFastAPIServer(ImageSelectionData data)
    {
        try
        {
            // 构造边界用于multipart/form-data
            var boundary = "----" + DateTime.Now.Ticks.ToString("x");
            var multipartContent = new MultipartFormDataContent(boundary);

            // 添加文件
            var fileBytes = File.ReadAllBytes(data.ImagePath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            multipartContent.Add(fileContent, "file", Path.GetFileName(data.ImagePath));

            // 添加分割模式
            var segModeContent = new StringContent("box");
            multipartContent.Add(segModeContent, "seg_mode");

            // 添加边界框（转换为整数并序列化为JSON）
            var boxesJson = JsonSerializer.Serialize(data.Selections);
            var boxesContent = new StringContent(boxesJson, Encoding.UTF8, "application/json");
            multipartContent.Add(boxesContent, "boxes_json");

            // 发送POST请求到FastAPI服务器
            HttpResponseMessage response = await httpClient.PostAsync("http://localhost:8000/process", multipartContent);
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
    // 原始图片坐标（用于发送到服务器）
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }

    // 归一化坐标（相对于图片显示区域的百分比，用于窗口缩放时重新绘制）
    public double NormalizedX1 { get; set; }
    public double NormalizedY1 { get; set; }
    public double NormalizedX2 { get; set; }
    public double NormalizedY2 { get; set; }

    public Point StartPoint
    {
        get => new Point(X1, Y1);
        set
        {
            X1 = value.X;
            Y1 = value.Y;
        }
    }
    public Point EndPoint
    {
        get => new Point(X2, Y2);
        set
        {
            X2 = value.X;
            Y2 = value.Y;
        }
    }
}

public class BoundingBox
{
    public int x1 { get; set; }
    public int y1 { get; set; }
    public int x2 { get; set; }
    public int y2 { get; set; }
}

public class ImageSelectionData
{
    public string ImagePath { get; set; }
    public List<BoundingBox> Selections { get; set; }
}
