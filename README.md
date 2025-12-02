# MIDI-3D Reconstruction Selector

这是一个用于Blender的3D重建工作流工具，结合了MAUI跨平台应用和Blender插件，实现了从图像捕获到3D模型重建的完整流程。

## 功能特性

1. **图像捕获**: 直接从Blender视口中捕获图像
2. **区域选择**: 使用跨平台MAUI应用选择感兴趣的区域
3. **服务器通信**: 将选择信息发送到FastAPI服务器进行3D重建
4. **模型导入**: 自动检查任务状态并在完成后导入重建的模型

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
  "selection": {
    "x1": 100,
    "y1": 100,
    "x2": 200,
    "y2": 200
  }
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