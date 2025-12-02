# MIDI-3D Reconstruction Selector

这是一个用于Blender的3D重建工作流工具，结合了MAUI跨平台应用和Blender插件，实现了从图像捕获到3D模型重建的完整流程。

## 功能特性

1. **图像捕获**: 直接从Blender视口中捕获图像
2. **区域选择**: 使用跨平台MAUI应用选择感兴趣的区域
3. **多区域框选**: 在图像上绘制多个方框以标记不同的物体
4. **服务器通信**: 将选择信息发送到FastAPI服务器进行3D重建
5. **模型导入**: 自动检查任务状态并在完成后导入重建的模型

## 项目结构

```
blender_selecter/
├── Platforms/              # 平台特定代码
│   ├── Android/
│   ├── iOS/
│   ├── MacCatalyst/
│   ├── Tizen/
│   └── Windows/
├── Resources/              # 应用资源
├── MainPage.xaml/.cs       # 主界面
├── MauiProgram.cs          # 应用入口点
├── blender_plugin.py       # Blender插件
└── README.md
```

## 在VS Code中调试MAUI应用

### 准备工作

1. 确保已安装C#扩展（由Microsoft提供）
2. 确保已安装.NET MAUI工作负载

### 调试步骤

1. 打开项目文件夹
2. 按F5或进入"Run and Debug"面板选择配置启动应用
3. 提供测试用的图像路径（如果需要）

### 调试配置说明

- **Launch Maui App (Windows)**: 构建并启动MAUI应用，不带参数
- **Launch Maui App with Image (Windows)**: 构建并启动MAUI应用，提示输入图像路径作为参数

## 图像框选功能使用说明

1. 启动应用后，通过"Select Image"按钮选择一张图像，或者通过命令行参数自动加载图像
2. 在图像上按下鼠标左键并拖拽以绘制方框
3. 释放鼠标左键以完成方框绘制
4. 可以为同一张图像绘制多个方框
5. 点击"Clear Selections"按钮清除所有已绘制的方框
6. 点击"Send to Server"将图像路径和所有方框坐标发送到服务器

## 编译和部署

### 编译MAUI应用

1. **Windows平台**:
   ```bash
   dotnet publish -f net9.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None --self-contained true -r win-x64
   ```

2. **macOS平台**:
   ```bash
   dotnet publish -f net9.0-maccatalyst -c Release --self-contained true -r osx-x64
   ```

3. **Linux平台**:
   ```bash
   # MAUI目前不直接支持Linux，可以考虑使用Uno Platform等替代方案
   ```

### 安装Blender插件

1. 打开Blender
2. 进入 `Edit > Preferences > Add-ons`
3. 点击 `Install...` 按钮
4. 选择 `blender_plugin.py` 文件
5. 启用插件

### 配置插件

1. 在Addon Preferences中设置MAUI应用的路径
2. 确保应用具有执行权限

## 使用流程

1. 在Blender中打开3D视图
2. 在右侧边栏找到"MIDI-3D"选项卡
3. 点击"Capture Viewport"捕获当前视图
4. 在弹出的应用中选择感兴趣区域
5. 点击"Send to Server"发送到重建服务器
6. 插件会自动检查任务状态并在完成后导入模型

## 技术细节

### MAUI应用

- 接受命令行参数作为输入图像路径
- 通过标准输出返回任务ID (`TASK_ID:xxxx`)
- 支持在图像上绘制多个方框以标记不同物体
- 支持Windows、macOS、iOS和Android平台

### Blender插件

- 捕获视口图像并保存为PNG格式
- 启动MAUI应用并监听其输出
- 解析任务ID并与服务器通信
- 自动导入完成的模型(Glb格式)

## API接口约定

### 发送任务
```
POST /reconstruction
Content-Type: application/json

{
  "image_path": "/path/to/image.png",
  "selections": [
    {
      "x1": 100,
      "y1": 100,
      "x2": 200,
      "y2": 200
    },
    {
      "x1": 300,
      "y1": 300,
      "x2": 400,
      "y2": 400
    }
  ]
}

Response:
{
  "task_id": "uuid-string"
}
```

### 查询任务状态
```
GET /reconstruction/{task_id}

Response:
{
  "status": "processing|completed|failed",
  "progress": 0.75,
  "model_url": "https://example.com/models/uuid.glb"  // 当status为completed时
}
```

## 开发说明

### 修改MAUI应用

1. 更新UI设计: 修改 `MainPage.xaml`
2. 更新业务逻辑: 修改 `MainPage.xaml.cs`
3. 添加依赖项: 更新 `blender_selecter.csproj`

### 修改Blender插件

1. 核心功能在 `blender_plugin.py` 中实现
2. 可以添加新的操作符或面板
3. 注意保持与MAUI应用的通信协议一致

## 故障排除

### MAUI应用无法启动

如果您遇到类似以下错误：
```
引发的异常: "System.InvalidOperationException" (位于 Microsoft.Extensions.DependencyInjection.dll 中)
程序 "[XXXX] blender_selecter.exe" 已退出，代码为 -1073741189 (0xc000027b)。
```

请尝试以下解决方案：

1. 清理并重新构建项目：
   ```bash
   dotnet clean
   dotnet build
   ```

2. 确保所有平台入口点正确配置：
   - Windows: [Platforms\Windows\App.xaml.cs](file:///g:/AI/blender_selecter/Platforms/Windows/App.xaml.cs)
   - Android: [Platforms\Android\MainApplication.cs](file:///g:/AI/blender_selecter/Platforms/Android/MainApplication.cs)
   - iOS: [Platforms\iOS\AppDelegate.cs](file:///g:/AI/blender_selecter/Platforms/iOS/AppDelegate.cs)
   - MacCatalyst: [Platforms\MacCatalyst\AppDelegate.cs](file:///g:/AI/blender_selecter/Platforms/MacCatalyst/AppDelegate.cs)

3. 检查依赖注入配置是否正确，在[MauiProgram.cs](file:///g:/AI/blender_selecter/MauiProgram.cs)中确认没有重复注册服务。

### MAUI应用无法启动
- 检查应用路径是否正确配置
- 确认应用具有执行权限
- 查看Blender控制台输出获取错误详情

### 任务状态检查失败
- 检查网络连接
- 确认服务器地址配置正确
- 查看服务器日志获取更多信息

### 模型导入失败
- 确认模型URL有效
- 检查模型格式是否为GLB
- 确认Blender具有相应的导入插件

## 许可证

本项目仅供学习和研究使用。

## 联系方式

如有问题，请提交Issue或联系项目维护者。