import requests
import json

def send_to_server():
    # 从图片中提取的4个边界框坐标
    boxes = [
        {"x1": 803, "y1": 388, "x2": 962, "y2": 747},
        {"x1": 623, "y1": 531, "x2": 788, "y2": 767},
        {"x1": 247, "y1": 365, "x2": 577, "y2": 759},
        {"x1": 54, "y1": 428, "x2": 224, "y2": 753}
    ]
    
    # 将边界框序列化为JSON
    boxes_json = json.dumps(boxes)
    print(f"Boxes JSON: {boxes_json}")
    
    # 服务器URL
    url = "http://127.0.0.1:8000/process"
    
    # 准备表单数据 - 使用data而不是params
    data = {
        "seg_mode": "box",
        "boxes_json": boxes_json,
        "polygon_refinement": "true",
        "detect_threshold": "0.3"
    }
    
    # 准备文件（请替换为实际的图片路径）
    image_path = "test.jpg"  # 替换为实际的图片路径
    
    try:
        # 发送POST请求 - 使用files和data参数
        with open(image_path, 'rb') as f:
            files = {"file": (image_path, f, "image/jpeg")}
            response = requests.post(url, data=data, files=files)
        
        print(f"Request URL: {response.url}")
        print(f"Status Code: {response.status_code}")
        
        if response.status_code == 200:
            print(f"Server response: {response.text}")
            
            # 解析响应JSON
            response_data = response.json()
            
            if "task_id" in response_data:
                task_id = response_data["task_id"]
                print(f"Task ID: {task_id}")
                print("Successfully sent to server!")
            else:
                print("Server returned unexpected response")
        else:
            print(f"Server error: {response.status_code} - {response.text}")
            
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    send_to_server()
