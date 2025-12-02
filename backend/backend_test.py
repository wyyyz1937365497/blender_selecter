import requests
import json

def simple_test():
    """简化版测试代码"""
    
    # 矩形框数据
    boxes_data = [
        {"x1": 1802, "y1": 372, "x2": 1974, "y2": 750},
        {"x1": 1613, "y1": 535, "x2": 1779, "y2": 795},
        {"x1": 1216, "y1": 341, "x2": 1584, "y2": 773},
        {"x1": 1084, "y1": 446, "x2": 1201, "y2": 760}
    ]
    
    # 参数
    params = {
        'seg_mode': 'box',
        'boxes_json': json.dumps(boxes_data),
        'polygon_refinement': 'true',
        'detect_threshold': '0.3'
    }
    
    # 文件
    files = {
        'file': ('test.jpg', open('test.jpg', 'rb'), 'image/jpeg')
    }
    
    try:
        response = requests.post(
            "http://127.0.0.1:8000/process",
            params=params,
            files=files
        )
        
        print(f"状态码: {response.status_code}")
        print(f"响应: {response.text}")
        
    except Exception as e:
        print(f"错误: {e}")
    finally:
        files['file'][1].close()

if __name__ == "__main__":
    simple_test()
