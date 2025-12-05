import json
import requests
import websocket
import uuid
import time
from PIL import Image
import io

def edit_image_with_qwen(input_image_path, positive_prompt, negative_prompt="", output_path="output.png", server_address="127.0.0.1:8188"):
    """
    使用Qwen Image Edit模型编辑图片
    
    Args:
        input_image_path (str): 输入图片路径
        positive_prompt (str): 正面描述（编辑指令）
        negative_prompt (str): 负面描述（可选，默认为空）
        output_path (str): 输出图片保存路径
        server_address (str): ComfyUI服务器地址
    
    Returns:
        str: 生成的图片路径
    """
    
    # 构建工作流JSON（基于提供的配置）
    workflow = {
        "3": {
            "inputs": {
                "seed": 477315547380083,
                "steps": 20,
                "cfg": 2.5,
                "sampler_name": "euler",
                "scheduler": "simple",
                "denoise": 1,
                "model": ["75", 0],
                "positive": ["111", 0],
                "negative": ["110", 0],
                "latent_image": ["88", 0]
            },
            "class_type": "KSampler"
        },
        "8": {
            "inputs": {
                "samples": ["3", 0],
                "vae": ["39", 0]
            },
            "class_type": "VAEDecode"
        },
        "37": {
            "inputs": {
                "unet_name": "qwen_image_edit_2509_fp8_e4m3fn.safetensors",
                "weight_dtype": "default"
            },
            "class_type": "UNETLoader"
        },
        "38": {
            "inputs": {
                "clip_name": "qwen_2.5_vl_7b_fp8_scaled.safetensors",
                "type": "qwen_image",
                "device": "default"
            },
            "class_type": "CLIPLoader"
        },
        "39": {
            "inputs": {
                "vae_name": "qwen_image_vae.safetensors"
            },
            "class_type": "VAELoader"
        },
        "60": {
            "inputs": {
                "filename_prefix": "ComfyUI",
                "images": ["8", 0]
            },
            "class_type": "SaveImage"
        },
        "66": {
            "inputs": {
                "shift": 3,
                "model": ["37", 0]
            },
            "class_type": "ModelSamplingAuraFlow"
        },
        "75": {
            "inputs": {
                "strength": 1,
                "model": ["66", 0]
            },
            "class_type": "CFGNorm"
        },
        "78": {
            "inputs": {
                "image": input_image_path
            },
            "class_type": "LoadImage"
        },
        "88": {
            "inputs": {
                "pixels": ["390", 0],
                "vae": ["39", 0]
            },
            "class_type": "VAEEncode"
        },
        "110": {
            "inputs": {
                "prompt": negative_prompt,
                "clip": ["38", 0],
                "vae": ["39", 0],
                "image1": ["390", 0]
            },
            "class_type": "TextEncodeQwenImageEditPlus"
        },
        "111": {
            "inputs": {
                "prompt": positive_prompt,
                "clip": ["38", 0],
                "vae": ["39", 0],
                "image1": ["390", 0]
            },
            "class_type": "TextEncodeQwenImageEditPlus"
        },
        "390": {
            "inputs": {
                "image": ["78", 0]
            },
            "class_type": "FluxKontextImageScale"
        }
    }
    
    # 上传图片到ComfyUI
    with open(input_image_path, 'rb') as f:
        image_data = f.read()
    
    # 构建multipart/form-data请求
    files = {
        'image': (input_image_path, image_data, 'image/png')
    }
    
    upload_response = requests.post(f"http://{server_address}/upload/image", files=files)
    
    if upload_response.status_code != 200:
        raise Exception(f"图片上传失败: {upload_response.text}")
    
    # 更新工作流中的图片路径
    upload_result = upload_response.json()
    image_name = upload_result.get('name', input_image_path)
    workflow["78"]["inputs"]["image"] = image_name
    
    # 提交工作流
    prompt_id = str(uuid.uuid4())
    
    payload = {
        "prompt": workflow,
        "client_id": prompt_id
    }
    
    response = requests.post(f"http://{server_address}/prompt", json=payload)
    
    if response.status_code != 200:
        raise Exception(f"工作流提交失败: {response.text}")
    
    result = response.json()
    prompt_id = result.get("prompt_id")
    
    if not prompt_id:
        raise Exception("未获取到prompt_id")
    
    # 监听WebSocket获取结果
    ws_url = f"ws://{server_address}/ws?clientId={prompt_id}"
    
    def on_message(ws, message):
        data = json.loads(message)
        if data.get('type') == 'executed':
            # 获取生成的图片
            history_response = requests.get(f"http://{server_address}/history/{prompt_id}")
            if history_response.status_code == 200:
                history = history_response.json()
                if prompt_id in history:
                    outputs = history[prompt_id]['outputs']
                    for node_id, node_output in outputs.items():
                        if 'images' in node_output:
                            for image_info in node_output['images']:
                                # 下载图片
                                image_filename = image_info['filename']
                                image_url = f"http://{server_address}/view?filename={image_filename}"
                                
                                img_response = requests.get(image_url)
                                if img_response.status_code == 200:
                                    with open(output_path, 'wb') as f:
                                        f.write(img_response.content)
                                    print(f"图片已保存到: {output_path}")
                                    ws.close()
                                    return
    
    def on_error(ws, error):
        print(f"WebSocket错误: {error}")
    
    def on_close(ws, close_status_code, close_msg):
        print("WebSocket连接已关闭")
    
    def on_open(ws):
        print("WebSocket连接已建立")
    
    # 创建WebSocket连接
    ws = websocket.WebSocketApp(
        ws_url,
        on_open=on_open,
        on_message=on_message,
        on_error=on_error,
        on_close=on_close
    )
    
    # 运行WebSocket
    ws.run_forever()
    
    return output_path

# 使用示例
if __name__ == "__main__":
    # 示例调用
    input_image = "test.jpg"  # 输入图片路径
    positive_desc = "Replace the cat with a dalmatian"  # 正面描述
    negative_desc = ""  # 负面描述（可选）
    output_image = "output/woutput_image.png"  # 输出图片路径
    
    try:
        result_path = edit_image_with_qwen(
            input_image_path=input_image,
            positive_prompt=positive_desc,
            negative_prompt=negative_desc,
            output_path=output_image,
            server_address="127.0.0.1:8188"  # ComfyUI服务器地址
        )
        print(f"图片编辑完成，结果保存在: {result_path}")
    except Exception as e:
        print(f"图片编辑失败: {e}")
