import requests
import json

# API URL
url = "https://ks.tongji.edu.cn/waterapi/api/AccUseHzWatch"

# 请求参数
params = {
    'info': 'Wni6c60M3Arw0zFWh%2F1Js56b87dFULpmlNyQ04oSc9s%3D',
    'token': 'eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJVc2VySWQiOiJrc3dhdGVyIiwiUGFzc3dvcmQiOiJrdjdYalB6ckROSlkwcGRaIyIsImV4cCI6MTc2NDkzODI2OS4wfQ.ZPLroJl-kHSxC0iRAEwSrVtQXYhNz-kTvFDGaajg9-c'
}

try:
    # 发送GET请求
    response = requests.get(url, params=params)
    
    # 检查响应状态码
    if response.status_code == 200:
        # 解析JSON响应
        data = response.json()
        
        # 打印格式化的JSON数据
        print("API响应数据:")
        print(json.dumps(data, indent=2, ensure_ascii=False))
        
        # 如果你想访问特定字段，可以这样做：
        if 'data' in data:
            print("\n数据内容:")
            print(data['data'])
    else:
        print(f"请求失败，状态码: {response.status_code}")
        print(f"响应内容: {response.text}")
        
except requests.exceptions.RequestException as e:
    print(f"请求发生错误: {e}")
except json.JSONDecodeError as e:
    print(f"解析JSON数据时出错: {e}")
