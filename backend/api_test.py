import requests
import json
import urllib.parse

def test_fastapi_endpoint_correct():
    url = "http://127.0.0.1:8000/process"
    
    # 准备测试数据
    boxes = [
        {"x1": 1810, "y1": 407, "x2": 1966, "y2": 755},
        {"x1": 1613, "y1": 530, "x2": 1795, "y2": 778},
        {"x1": 1228, "y1": 364, "x2": 1604, "y2": 762},
        {"x1": 1064, "y1": 440, "x2": 1214, "y2": 770}
    ]
    
    boxes_json = json.dumps(boxes)
    
    # 构建query参数
    params = {
        'seg_mode': 'box',
        'boxes_json': boxes_json,  # 这里需要URL编码
        'polygon_refinement': 'true',
        'detect_threshold': '0.3'
    }
    
    # 准备文件（只在body中）
    files = {
        'file': ('test.jpg', open('test.jpg', 'rb'), 'image/jpeg')
    }
    
    print("=== Correct Python Test Request ===")
    print(f"URL: {url}")
    print(f"Query params: {params}")
    print("===============================")
    
    try:
        # 关键：params作为query参数，files作为form data
        response = requests.post(url, params=params, files=files)
        print(f"Status Code: {response.status_code}")
        print(f"Response: {response.text}")
        
        if response.status_code == 200:
            result = response.json()
            print(f"Task ID: {result.get('task_id')}")
        else:
            print("Request failed")
            
    except Exception as e:
        print(f"Error: {e}")
    finally:
        files['file'][1].close()

# 手动构建URL的版本
def test_with_manual_url():
    boxes = [{"x1": 100, "y1": 100, "x2": 200, "y2": 200}]
    boxes_json = json.dumps(boxes)
    
    # URL编码boxes_json
    encoded_boxes = urllib.parse.quote(boxes_json)
    
    # 手动构建完整URL
    url = f"http://127.0.0.1:8000/process?seg_mode=box&boxes_json={encoded_boxes}&polygon_refinement=true&detect_threshold=0.3"
    
    files = {
        'file': ('test.jpg', open('test.jpg', 'rb'), 'image/jpeg')
    }
    
    print(f"Manual URL: {url}")
    
    try:
        response = requests.post(url, files=files)
        print(f"Status: {response.status_code}")
        print(f"Response: {response.text}")
    except Exception as e:
        print(f"Error: {e}")
    finally:
        files['file'][1].close()

if __name__ == "__main__":
    test_fastapi_endpoint_correct()
    test_with_manual_url()
