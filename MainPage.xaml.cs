using System.Net.Http.Json;

namespace blender_selecter;

public partial class MainPage : ContentPage
{
    private string selectedImagePath = "";
    public static string ImagePathFromArgs = "";

    public MainPage()
    {
        InitializeComponent();
        
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
    
    private void LoadImageFromPath(string path)
    {
        selectedImagePath = path;
        MainImage.Source = ImageSource.FromFile(selectedImagePath);
        SelectImageButton.Text = Path.GetFileName(selectedImagePath);
        
        // 显示选择区域相关控件
        OnPropertyChanged(nameof(IsImageSelected));
        SendToServerButton.IsEnabled = true;
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
    
    public bool IsImageSelected => !string.IsNullOrEmpty(selectedImagePath);
}