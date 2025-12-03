import json
import uuid
import requests
import websocket
import os
import time
from websocket import create_connection


# ---------- 配置区 ----------
SERVER_ADDRESS = "127.0.0.1:8188"
IMAGE_PATH = "test.jpg"          # 替换为你的图片路径
PROMPT_TEXT = "remove the dog on the sofa and replace it with a cat"             # 替换为你的文本提示
WORKFLOW_JSON_PATH = "image_omnigen2_image_edit.json"
OUTPUT_DIR = "output"             # 输出图片保存目录
# ---------------------------

# ---------- 工具函数 ----------
def upload_image(image_path, server_address=SERVER_ADDRESS):
    url = f"http://{server_address}/upload/image"
    with open(image_path, 'rb') as f:
        files = {"image": (os.path.basename(image_path), f, 'image/png')}
        response = requests.post(url, files=files)
    if response.status_code == 200:
        return response.json()
    else:
        raise Exception(f"图片上传失败: {response.status_code} - {response.text}")

def load_workflow(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)

def replace_prompt_in_workflow(workflow, new_prompt, image_name=None):
    for node in workflow.values():
        if node.get("class_type") == "CLIPTextEncode":
            node["inputs"]["text"] = new_prompt
        if node.get("class_type") == "LoadImage" and image_name:
            node["inputs"]["image"] = image_name
    return workflow

def queue_prompt(workflow, server_address=SERVER_ADDRESS):
    client_id = str(uuid.uuid4())
    p = {"prompt": workflow, "client_id": client_id}
    data = json.dumps(p).encode('utf-8')
    headers = {'Content-Type': 'application/json'}
    response = requests.post(
        f"http://{server_address}/prompt",
        data=data,
        headers=headers
    )
    if response.status_code == 200:
        return response.json(), client_id
    else:
        raise Exception(f"提交任务失败: {response.status_code} - {response.text}")

def wait_for_result(ws, prompt_id):
    while True:
        out = ws.recv()
        if isinstance(out, str):
            message = json.loads(out)
            if message['type'] == 'executing':
                data = message['data']
                if data['node'] is None and data['prompt_id'] == prompt_id:
                    break

def get_history(prompt_id, server_address=SERVER_ADDRESS):
    url = f"http://{server_address}/history/{prompt_id}"
    response = requests.get(url)
    if response.status_code == 200:
        return response.json()
    else:
        raise Exception(f"获取历史失败: {response.status_code} - {response.text}")

def get_image(filename, subfolder, folder_type, server_address=SERVER_ADDRESS):
    url = f"http://{server_address}/view"
    params = {"filename": filename, "subfolder": subfolder, "type": folder_type}
    response = requests.get(url, params=params)
    if response.status_code == 200:
        return response.content
    else:
        raise Exception(f"下载图片失败: {response.status_code} - {response.text}")

def ensure_dir(directory):
    if not os.path.exists(directory):
        os.makedirs(directory)

# ---------- 主流程 ----------
def main():
    try:
        # 1. 上传图片
        print("正在上传图片...")
        upload_resp = upload_image(IMAGE_PATH)
        image_name = upload_resp.get("name")
        print(f"图片上传成功: {image_name}")

        # 2. 加载工作流JSON
        print("加载工作流...")
        workflow = load_workflow(WORKFLOW_JSON_PATH)

        # 3. 替换文字和图片
        workflow = replace_prompt_in_workflow(workflow, PROMPT_TEXT, image_name)

        # 4. 提交任务
        print("提交任务到 ComfyUI...")
        prompt_res, client_id = queue_prompt(workflow)
        prompt_id = prompt_res["prompt_id"]
        print(f"任务已提交，ID: {prompt_id}")

        # 5. 等待完成（WebSocket）【修正部分】
        ws = create_connection(f"ws://{SERVER_ADDRESS}/ws?clientId={client_id}")
        print("等待任务完成...")
        wait_for_result(ws, prompt_id)
        ws.close()
        print("任务已完成！")

        # 6. 获取图片
        print("获取生成图片...")
        history = get_history(prompt_id)
        ensure_dir(OUTPUT_DIR)
        for node_id, node_output in history[prompt_id]["outputs"].items():
            if "images" in node_output:
                for image in node_output["images"]:
                    img_data = get_image(image["filename"], image["subfolder"], image["type"])
                    output_path = os.path.join(OUTPUT_DIR, f"output_{image['filename']}")
                    with open(output_path, "wb") as f:
                        f.write(img_data)
                    print(f"图片已保存: {output_path}")

    except Exception as e:
        print(f"发生错误: {e}")

if __name__ == "__main__":
    main()
