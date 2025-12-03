"""
OmniGen2 Image Editing API Server
使用 ComfyUI 后端进行图像编辑

启动方式:
    python omnigen2_server.py

API 端点:
    POST /omnigen2/edit - 使用 OmniGen2 编辑图像
    GET /omnigen2/status/{task_id} - 获取任务状态
    GET /omnigen2/result/{task_id} - 下载编辑后的图像
"""

import os
import uuid
import time
import json
import base64
import io
import requests
import websocket
from PIL import Image
from typing import Optional, List
from fastapi import FastAPI, File, UploadFile, HTTPException, BackgroundTasks, Form
from fastapi.responses import FileResponse, JSONResponse
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

# Constants
TMP_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tmp")
COMFYUI_HOST = "127.0.0.1"
COMFYUI_PORT = 8188

# 默认负面提示词
DEFAULT_NEGATIVE_PROMPT = "deformed, blurry, over saturation, bad anatomy, disfigured, poorly drawn face, mutation, mutated, extra_limb, ugly, poorly drawn hands, fused fingers, messy drawing, broken legs censor, censored, censor_bar"

# Ensure tmp directory exists
os.makedirs(TMP_DIR, exist_ok=True)

# Initialize FastAPI app
app = FastAPI(title="OmniGen2 API", description="API for OmniGen2 image editing via ComfyUI")

# Add CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# In-memory storage for task statuses
task_statuses = {}

# Response models
class TaskResponse(BaseModel):
    task_id: str
    status_url: str

class TaskStatus(BaseModel):
    status: str
    message: str
    progress: Optional[float] = None
    result_url: Optional[str] = None

class BoundingBox(BaseModel):
    x1: int
    y1: int
    x2: int
    y2: int


def update_task_status(task_id: str, status: str, message: str, progress: float = None, result_url: str = None):
    """Update the status of a task"""
    task_statuses[task_id] = {
        "status": status,
        "message": message,
        "progress": progress,
        "result_url": result_url,
        "timestamp": time.time()
    }


def image_to_base64(image_path: str) -> str:
    """Convert image file to base64 string"""
    with open(image_path, "rb") as f:
        return base64.b64encode(f.read()).decode("utf-8")


def base64_to_image(base64_str: str, output_path: str):
    """Convert base64 string to image file"""
    image_data = base64.b64decode(base64_str)
    with open(output_path, "wb") as f:
        f.write(image_data)


def create_mask_from_boxes(image_path: str, boxes: List[dict], output_path: str) -> str:
    """
    Create a mask image from bounding boxes
    White (255) = area to edit, Black (0) = area to keep
    """
    # Open the original image to get dimensions
    with Image.open(image_path) as img:
        width, height = img.size
    
    # Create a black mask (all zeros = keep everything)
    mask = Image.new("L", (width, height), 0)
    
    # Draw white rectangles for each bounding box (areas to edit)
    from PIL import ImageDraw
    draw = ImageDraw.Draw(mask)
    
    for box in boxes:
        x1 = max(0, min(box["x1"], width))
        y1 = max(0, min(box["y1"], height))
        x2 = max(0, min(box["x2"], width))
        y2 = max(0, min(box["y2"], height))
        draw.rectangle([x1, y1, x2, y2], fill=255)
    
    # Save the mask
    mask.save(output_path)
    return output_path


def upload_image_to_comfyui(image_path: str, filename: str = None) -> str:
    """Upload an image to ComfyUI and return the filename"""
    if filename is None:
        filename = os.path.basename(image_path)
    
    url = f"http://{COMFYUI_HOST}:{COMFYUI_PORT}/upload/image"
    
    with open(image_path, "rb") as f:
        files = {"image": (filename, f, "image/png")}
        response = requests.post(url, files=files)
    
    if response.status_code == 200:
        result = response.json()
        return result.get("name", filename)
    else:
        raise Exception(f"Failed to upload image: {response.text}")


def create_omnigen2_workflow(
    image_filename: str,
    mask_filename: str,
    prompt: str,
    negative_prompt: str = DEFAULT_NEGATIVE_PROMPT,
    steps: int = 30,
    cfg: float = 3.5,
    denoise: float = 0.8,
    seed: int = -1
) -> dict:
    """
    Create a ComfyUI workflow for OmniGen2 image editing
    
    This workflow uses:
    - LoadImage for input image
    - LoadImage for mask
    - OmniGen2 node for generation
    - SaveImage for output
    """
    
    # Generate random seed if not specified
    if seed == -1:
        import random
        seed = random.randint(0, 2**32 - 1)
    
    workflow = {
        "1": {
            "class_type": "LoadImage",
            "inputs": {
                "image": image_filename
            }
        },
        "2": {
            "class_type": "LoadImage", 
            "inputs": {
                "image": mask_filename
            }
        },
        "3": {
            "class_type": "OmniGen2Sampler",
            "inputs": {
                "prompt": prompt,
                "negative_prompt": negative_prompt,
                "image": ["1", 0],
                "mask": ["2", 0],
                "steps": steps,
                "cfg": cfg,
                "denoise": denoise,
                "seed": seed,
                "width": 1024,
                "height": 1024,
                "batch_size": 1
            }
        },
        "4": {
            "class_type": "SaveImage",
            "inputs": {
                "images": ["3", 0],
                "filename_prefix": "omnigen2_edit"
            }
        }
    }
    
    return workflow


def create_simple_inpaint_workflow(
    image_filename: str,
    mask_filename: str,
    prompt: str,
    negative_prompt: str = DEFAULT_NEGATIVE_PROMPT,
    steps: int = 30,
    cfg: float = 7.5,
    denoise: float = 0.75,
    seed: int = -1
) -> dict:
    """
    Create a simpler inpainting workflow using SDXL Inpaint model
    This is a fallback if OmniGen2 nodes are not available
    """
    
    if seed == -1:
        import random
        seed = random.randint(0, 2**32 - 1)
    
    workflow = {
        "1": {
            "class_type": "LoadImage",
            "inputs": {
                "image": image_filename
            }
        },
        "2": {
            "class_type": "LoadImage",
            "inputs": {
                "image": mask_filename
            }
        },
        "3": {
            "class_type": "CheckpointLoaderSimple",
            "inputs": {
                "ckpt_name": "sd_xl_base_1.0.safetensors"
            }
        },
        "4": {
            "class_type": "CLIPTextEncode",
            "inputs": {
                "text": prompt,
                "clip": ["3", 1]
            }
        },
        "5": {
            "class_type": "CLIPTextEncode",
            "inputs": {
                "text": negative_prompt,
                "clip": ["3", 1]
            }
        },
        "6": {
            "class_type": "VAEEncode",
            "inputs": {
                "pixels": ["1", 0],
                "vae": ["3", 2]
            }
        },
        "7": {
            "class_type": "SetLatentNoiseMask",
            "inputs": {
                "samples": ["6", 0],
                "mask": ["2", 0]
            }
        },
        "8": {
            "class_type": "KSampler",
            "inputs": {
                "model": ["3", 0],
                "positive": ["4", 0],
                "negative": ["5", 0],
                "latent_image": ["7", 0],
                "seed": seed,
                "steps": steps,
                "cfg": cfg,
                "sampler_name": "euler",
                "scheduler": "normal",
                "denoise": denoise
            }
        },
        "9": {
            "class_type": "VAEDecode",
            "inputs": {
                "samples": ["8", 0],
                "vae": ["3", 2]
            }
        },
        "10": {
            "class_type": "SaveImage",
            "inputs": {
                "images": ["9", 0],
                "filename_prefix": "inpaint_edit"
            }
        }
    }
    
    return workflow


def queue_prompt(workflow: dict, client_id: str = None) -> str:
    """Queue a prompt in ComfyUI and return the prompt_id"""
    if client_id is None:
        client_id = str(uuid.uuid4())
    
    url = f"http://{COMFYUI_HOST}:{COMFYUI_PORT}/prompt"
    
    payload = {
        "prompt": workflow,
        "client_id": client_id
    }
    
    response = requests.post(url, json=payload)
    
    if response.status_code == 200:
        result = response.json()
        return result.get("prompt_id")
    else:
        raise Exception(f"Failed to queue prompt: {response.text}")


def get_history(prompt_id: str) -> dict:
    """Get the history/result of a prompt"""
    url = f"http://{COMFYUI_HOST}:{COMFYUI_PORT}/history/{prompt_id}"
    response = requests.get(url)
    
    if response.status_code == 200:
        return response.json()
    else:
        return {}


def wait_for_completion(prompt_id: str, timeout: int = 300) -> dict:
    """Wait for a prompt to complete and return the result"""
    start_time = time.time()
    
    while time.time() - start_time < timeout:
        history = get_history(prompt_id)
        
        if prompt_id in history:
            return history[prompt_id]
        
        time.sleep(1)
    
    raise TimeoutError(f"Prompt {prompt_id} did not complete within {timeout} seconds")


def get_output_images(prompt_id: str) -> List[str]:
    """Get the output image filenames from a completed prompt"""
    history = get_history(prompt_id)
    
    if prompt_id not in history:
        return []
    
    outputs = history[prompt_id].get("outputs", {})
    images = []
    
    for node_id, node_output in outputs.items():
        if "images" in node_output:
            for img in node_output["images"]:
                images.append(img["filename"])
    
    return images


def download_image(filename: str, output_path: str, subfolder: str = "") -> str:
    """Download an image from ComfyUI output folder"""
    url = f"http://{COMFYUI_HOST}:{COMFYUI_PORT}/view"
    params = {
        "filename": filename,
        "type": "output",
        "subfolder": subfolder
    }
    
    response = requests.get(url, params=params)
    
    if response.status_code == 200:
        with open(output_path, "wb") as f:
            f.write(response.content)
        return output_path
    else:
        raise Exception(f"Failed to download image: {response.text}")


def process_omnigen2_edit(
    task_id: str,
    image_path: str,
    boxes: List[dict],
    prompt: str
):
    """Process OmniGen2 image editing in background"""
    try:
        update_task_status(task_id, "processing", "Starting OmniGen2 image editing...", 0.1)
        
        # Create mask from bounding boxes
        update_task_status(task_id, "processing", "Creating edit mask...", 0.2)
        mask_path = os.path.join(TMP_DIR, f"{task_id}_mask.png")
        create_mask_from_boxes(image_path, boxes, mask_path)
        
        # Upload images to ComfyUI
        update_task_status(task_id, "processing", "Uploading images to ComfyUI...", 0.3)
        image_filename = upload_image_to_comfyui(image_path, f"{task_id}_input.png")
        mask_filename = upload_image_to_comfyui(mask_path, f"{task_id}_mask.png")
        
        # Create workflow
        update_task_status(task_id, "processing", "Creating OmniGen2 workflow...", 0.4)
        
        # Try OmniGen2 first, fallback to simple inpaint
        try:
            workflow = create_omnigen2_workflow(
                image_filename=image_filename,
                mask_filename=mask_filename,
                prompt=prompt,
                negative_prompt=DEFAULT_NEGATIVE_PROMPT
            )
        except Exception as e:
            print(f"OmniGen2 workflow creation failed, using fallback: {e}")
            workflow = create_simple_inpaint_workflow(
                image_filename=image_filename,
                mask_filename=mask_filename,
                prompt=prompt,
                negative_prompt=DEFAULT_NEGATIVE_PROMPT
            )
        
        # Queue the prompt
        update_task_status(task_id, "processing", "Queuing generation task...", 0.5)
        prompt_id = queue_prompt(workflow)
        
        # Wait for completion
        update_task_status(task_id, "processing", "Generating edited image...", 0.6)
        
        try:
            result = wait_for_completion(prompt_id, timeout=600)
            update_task_status(task_id, "processing", "Generation complete, downloading result...", 0.9)
        except TimeoutError:
            update_task_status(task_id, "error", "Generation timed out")
            return
        
        # Get output images
        output_images = get_output_images(prompt_id)
        
        if not output_images:
            update_task_status(task_id, "error", "No output images generated")
            return
        
        # Download the result
        result_path = os.path.join(TMP_DIR, f"{task_id}_result.png")
        download_image(output_images[0], result_path)
        
        # Clean up temporary files
        if os.path.exists(mask_path):
            os.remove(mask_path)
        
        # Update status with result URL
        update_task_status(
            task_id, 
            "completed", 
            "Image editing completed successfully!", 
            1.0, 
            f"/omnigen2/result/{task_id}"
        )
        
    except Exception as e:
        print(f"Error processing task {task_id}: {str(e)}")
        update_task_status(task_id, "error", f"Error: {str(e)}")


@app.get("/")
async def root():
    """Root endpoint to check API status"""
    return {"message": "OmniGen2 API is running", "status": "active"}


@app.post("/omnigen2/edit", response_model=TaskResponse)
async def edit_image(
    background_tasks: BackgroundTasks,
    file: UploadFile = File(...),
    prompt: str = Form(...),
    boxes_json: str = Form(...)
):
    """
    Edit an image using OmniGen2
    
    Parameters:
    - file: The input image file
    - prompt: Positive prompt describing what to generate in the masked areas
    - boxes_json: JSON string of bounding boxes, format: [{"x1":0,"y1":0,"x2":100,"y2":100}, ...]
    """
    # Validate file type
    if not file.content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="Uploaded file is not an image")
    
    # Parse boxes
    try:
        boxes = json.loads(boxes_json)
    except json.JSONDecodeError:
        raise HTTPException(status_code=400, detail="Invalid JSON format for boxes_json")
    
    if not boxes or len(boxes) == 0:
        raise HTTPException(status_code=400, detail="At least one bounding box is required")
    
    # Validate prompt
    if not prompt or prompt.strip() == "":
        raise HTTPException(status_code=400, detail="Prompt is required")
    
    # Generate task ID
    task_id = str(uuid.uuid4())
    
    # Save uploaded image
    image_path = os.path.join(TMP_DIR, f"{task_id}_input.png")
    with open(image_path, "wb") as buffer:
        content = await file.read()
        buffer.write(content)
    
    # Initialize task status
    update_task_status(task_id, "queued", "Task queued for processing")
    
    # Add background task
    background_tasks.add_task(
        process_omnigen2_edit,
        task_id,
        image_path,
        boxes,
        prompt
    )
    
    return TaskResponse(
        task_id=task_id,
        status_url=f"/omnigen2/status/{task_id}"
    )


@app.get("/omnigen2/status/{task_id}", response_model=TaskStatus)
async def get_task_status(task_id: str):
    """Get the status of an OmniGen2 editing task"""
    if task_id not in task_statuses:
        raise HTTPException(status_code=404, detail="Task not found")
    
    status = task_statuses[task_id]
    return TaskStatus(
        status=status["status"],
        message=status["message"],
        progress=status.get("progress"),
        result_url=status.get("result_url")
    )


@app.get("/omnigen2/result/{task_id}")
async def get_result(task_id: str):
    """Download the edited image result"""
    if task_id not in task_statuses:
        raise HTTPException(status_code=404, detail="Task not found")
    
    status = task_statuses[task_id]
    
    if status["status"] != "completed":
        raise HTTPException(status_code=400, detail="Task not completed yet")
    
    result_path = os.path.join(TMP_DIR, f"{task_id}_result.png")
    
    if not os.path.exists(result_path):
        raise HTTPException(status_code=404, detail="Result image not found")
    
    return FileResponse(
        result_path,
        media_type="image/png",
        filename=f"omnigen2_edit_{task_id}.png"
    )


@app.get("/health")
async def health_check():
    """Check if ComfyUI is reachable"""
    try:
        response = requests.get(f"http://{COMFYUI_HOST}:{COMFYUI_PORT}/system_stats", timeout=5)
        if response.status_code == 200:
            return {"status": "healthy", "comfyui": "connected"}
        else:
            return {"status": "degraded", "comfyui": "unreachable"}
    except Exception as e:
        return {"status": "degraded", "comfyui": "unreachable", "error": str(e)}


# Start the server
if __name__ == "__main__":
    import uvicorn
    print("Starting OmniGen2 API Server on port 8001...")
    print(f"ComfyUI endpoint: http://{COMFYUI_HOST}:{COMFYUI_PORT}")
    uvicorn.run(app, host="0.0.0.0", port=8001)
