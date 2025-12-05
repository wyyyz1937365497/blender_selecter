using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Net.WebSockets; // 保留此命名空间，我们将使用 ClientWebSocket
using System.Threading;

namespace blender_selecter
{
    public class ComfyUIService
    {
        private readonly string serverAddress;
        private readonly HttpClient httpClient;

        public ComfyUIService(string address = "127.0.0.1:8188")
        {
            serverAddress = address;
            httpClient = new HttpClient();
        }

        /// <summary>
        /// 上传图片到ComfyUI服务器
        /// </summary>
        /// <param name="imagePath">图片路径</param>
        /// <returns>上传结果，包含图片名称</returns>
        public async Task<Dictionary<string, object>> UploadImageAsync(string imagePath)
        {
            try
            {
                using var form = new MultipartFormDataContent();
                using var fileStream = File.OpenRead(imagePath);
                using var fileContent = new StreamContent(fileStream);

                // --- 改进：根据文件扩展名动态设置 MIME 类型 ---
                var extension = Path.GetExtension(imagePath).ToLowerInvariant();
                var mimeType = extension switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    _ => "application/octet-stream" // 默认类型
                };
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
                // --- 改进结束 ---

                form.Add(fileContent, "image", Path.GetFileName(imagePath));

                var response = await httpClient.PostAsync($"http://{serverAddress}/upload/image", form);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(content)
                           ?? new Dictionary<string, object>();
                }
                else
                {
                    throw new Exception($"图片上传失败: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"上传图片时发生错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 提交工作流到ComfyUI服务器
        /// </summary>
        /// <param name="workflow">工作流JSON</param>
        /// <returns>任务ID和客户端ID</returns>
        public async Task<(string promptId, string clientId)> QueuePromptAsync(Dictionary<string, object> workflow)
        {
            try
            {
                var clientId = Guid.NewGuid().ToString();
                var requestData = new Dictionary<string, object>
                {
                    ["prompt"] = workflow,
                    ["client_id"] = clientId
                };

                var json = JsonSerializer.Serialize(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"http://{serverAddress}/prompt", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var responseData = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);

                    if (responseData != null && responseData.ContainsKey("prompt_id"))
                    {
                        return (responseData["prompt_id"].ToString(), clientId);
                    }

                    throw new Exception("服务器响应中未包含prompt_id");
                }
                else
                {
                    throw new Exception($"提交任务失败: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"提交工作流时发生错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 等待任务完成（使用 .NET 内置 ClientWebSocket）
        /// </summary>
        /// <param name="promptId">任务ID</param>
        /// <param name="clientId">客户端ID</param>
        /// <param name="progressCallback">进度回调函数</param>
        /// <returns>任务是否完成</returns>
        public async Task<bool> WaitForCompletionAsync(string promptId, string clientId, Action<int> progressCallback = null)
        {
            using var ws = new ClientWebSocket();
            var uri = new Uri($"ws://{serverAddress}/ws?clientId={clientId}");
            var completionSource = new TaskCompletionSource<bool>();
            var buffer = new byte[4096];

            try
            {
                // 连接WebSocket
                await ws.ConnectAsync(uri, CancellationToken.None);

                // 循环接收消息
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var message = JsonSerializer.Deserialize<Dictionary<string, object>>(messageJson);

                        if (message != null && message.ContainsKey("type"))
                        {
                            var type = message["type"].ToString();

                            if (type == "progress")
                            {
                                // 更新进度
                                var data = message.ContainsKey("data") ?
                                          JsonSerializer.Deserialize<Dictionary<string, object>>(message["data"].ToString()) :
                                          new Dictionary<string, object>();

                                if (data != null && data.ContainsKey("value") && data.ContainsKey("max"))
                                {
                                    var currentStep = double.Parse(data["value"].ToString());
                                    var totalSteps = double.Parse(data["max"].ToString());

                                    int progress = (int)((currentStep / totalSteps) * 100);
                                    progressCallback?.Invoke(progress);
                                }
                            }
                            else if (type == "executing")
                            {
                                var data = message.ContainsKey("data") ?
                                          JsonSerializer.Deserialize<Dictionary<string, object>>(message["data"].ToString()) :
                                          new Dictionary<string, object>();

                                if (data != null &&
                                    (!data.ContainsKey("node") || data["node"] == null) &&
                                    data.ContainsKey("prompt_id") &&
                                    data["prompt_id"].ToString() == promptId)
                                {
                                    completionSource.SetResult(true);
                                    break; // 任务完成，跳出循环
                                }
                            }
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                completionSource.SetException(new Exception($"WebSocket通信时发生错误: {ex.Message}", ex));
            }
            finally
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
            }

            // 设置超时（例如5分钟）
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
            var completedTask = await Task.WhenAny(completionSource.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("任务执行超时");
            }

            return await completionSource.Task;
        }

        /// <summary>
        /// 获取任务历史
        /// </summary>
        /// <param name="promptId">任务ID</param>
        /// <returns>任务历史</returns>
        public async Task<Dictionary<string, object>> GetHistoryAsync(string promptId)
        {
            try
            {
                var response = await httpClient.GetAsync($"http://{serverAddress}/history/{promptId}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(content)
                           ?? new Dictionary<string, object>();
                }
                else
                {
                    throw new Exception($"获取历史失败: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"获取任务历史时发生错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 下载生成的图片
        /// </summary>
        /// <param name="filename">文件名</param>
        /// <param name="subfolder">子文件夹</param>
        /// <param name="folderType">文件夹类型</param>
        /// <param name="savePath">保存路径</param>
        /// <returns>是否下载成功</returns>
        public async Task<bool> DownloadImageAsync(string filename, string subfolder, string folderType, string savePath)
        {
            try
            {
                var query = HttpUtility.ParseQueryString(string.Empty);
                query["filename"] = filename;
                query["subfolder"] = subfolder;
                query["type"] = folderType;

                var url = $"http://{serverAddress}/view?{query}";
                var response = await httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    // 确保目录存在
                    var directory = Path.GetDirectoryName(savePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // 保存图片
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(savePath, imageBytes);

                    return true;
                }
                else
                {
                    throw new Exception($"下载图片失败: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"下载图片时发生错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 加载工作流JSON文件
        /// </summary>
        /// <param name="jsonPath">JSON文件路径</param>
        /// <returns>工作流字典</returns>
        public Dictionary<string, object> LoadWorkflow(string jsonPath)
        {
            try
            {
                var json = File.ReadAllText(jsonPath);
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                       ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                throw new Exception($"加载工作流文件时发生错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 替换工作流中的文本和图片
        /// </summary>
        /// <param name="workflow">工作流字典</param>
        /// <param name="newPrompt">新的提示文本</param>
        /// <param name="imageName">图片名称</param>
        /// <param name="negativePrompt">负面提示词（可选）</param>
        /// <returns>修改后的工作流</returns>
        public Dictionary<string, object> ReplacePromptInWorkflow(Dictionary<string, object> workflow, string newPrompt, string imageName = null, string negativePrompt = "")
        {
            try
            {
                // 创建工作流副本
                var modifiedWorkflow = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    JsonSerializer.Serialize(workflow));

                // 遍历所有节点
                foreach (var nodeEntry in modifiedWorkflow)
                {
                    if (nodeEntry.Value is JsonElement element && element.ValueKind == JsonValueKind.Object)
                    {
                        var nodeDict = JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText());

                        if (nodeDict != null)
                        {
                            // 检查节点类型
                            if (nodeDict.ContainsKey("class_type"))
                            {
                                var classType = nodeDict["class_type"].ToString();

                                // 如果是文本编码节点，替换文本
                                if (classType == "CLIPTextEncode" && nodeDict.ContainsKey("inputs"))
                                {
                                    var inputs = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                        JsonSerializer.Serialize(nodeDict["inputs"]));

                                    if (inputs != null && inputs.ContainsKey("text"))
                                    {
                                        inputs["text"] = newPrompt;
                                        nodeDict["inputs"] = inputs;
                                    }
                                }

                                // 如果是图片加载节点，替换图片名称
                                if (classType == "LoadImage" && !string.IsNullOrEmpty(imageName) &&
                                    nodeDict.ContainsKey("inputs"))
                                {
                                    var inputs = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                        JsonSerializer.Serialize(nodeDict["inputs"]));

                                    if (inputs != null && inputs.ContainsKey("image"))
                                    {
                                        inputs["image"] = imageName;
                                        nodeDict["inputs"] = inputs;
                                    }
                                }

                                // 特别处理 Qwen Image Edit 模型的 TextEncodeQwenImageEditPlus 节点
                                if (classType == "TextEncodeQwenImageEditPlus" && nodeDict.ContainsKey("inputs"))
                                {
                                    var inputs = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                        JsonSerializer.Serialize(nodeDict["inputs"]));

                                    if (inputs != null && inputs.ContainsKey("prompt"))
                                    {
                                        // 对于节点110（负面提示词），使用negativePrompt
                                        if (nodeEntry.Key == "110")
                                        {
                                            inputs["prompt"] = negativePrompt;
                                        }
                                        // 对于节点111（正面提示词），使用newPrompt
                                        else if (nodeEntry.Key == "111")
                                        {
                                            inputs["prompt"] = newPrompt;
                                        }

                                        nodeDict["inputs"] = inputs;
                                    }
                                }
                            }

                            modifiedWorkflow[nodeEntry.Key] = nodeDict;
                        }
                    }
                }

                return modifiedWorkflow;
            }
            catch (Exception ex)
            {
                throw new Exception($"修改工作流时发生错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 从资源加载工作流JSON文件
        /// </summary>
        /// <param name="resourceName">资源名称</param>
        /// <returns>工作流字典</returns>
        public Dictionary<string, object> LoadWorkflowFromResource(string resourceName = "image_omnigen2_image_edit.json")
        {
            try
            {
                // 尝试多种可能的资源名称格式
                string[] possibleNames = {
                    $"blender_selecter.Resources.{resourceName}",
                    $"blender_selecter.{resourceName}",
                    resourceName
                };

                Stream stream = null;
                foreach (var name in possibleNames)
                {
                    stream = typeof(MainPage).Assembly.GetManifestResourceStream(name);
                    if (stream != null)
                    {
                        Console.WriteLine($"成功加载资源: {name}");
                        break;
                    }
                }

                if (stream == null)
                {
                    // 列出所有可用资源以便调试
                    var allResources = typeof(MainPage).Assembly.GetManifestResourceNames();
                    Console.WriteLine("可用资源:");
                    foreach (var res in allResources)
                    {
                        Console.WriteLine($"  - {res}");
                    }
                    throw new FileNotFoundException($"无法找到资源文件: {resourceName}");
                }

                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                       ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                throw new Exception($"从资源加载工作流文件时发生错误: {ex.Message}", ex);
            }
        }
    }
}
