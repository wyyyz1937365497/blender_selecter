import bpy
import subprocess
import os
import sys
import json
import time
import threading
import re
import requests
import tempfile
import shutil
from bpy.props import StringProperty, BoolProperty, FloatProperty
from bpy.types import Operator, Panel, Header

bl_info = {
    "name": "MIDI-3D Reconstruction",
    "author": "Your Name",
    "version": (1, 0),
    "blender": (2, 80, 0),
    "location": "3D View > Sidebar",
    "description": "Capture scene with MIDI3D camera and import generated model",
    "category": "3D View",
}

# 全局变量存储任务状态
task_status = {"checking": False, "task_id": None}

# --- 新增代码：创建侧边栏面板 ---
class MIDI3D_PT_Panel(Panel):
    """创建MIDI3D工具面板"""
    bl_label = "MIDI3D Reconstruction"
    bl_idname = "MIDI3D_PT_main_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "MIDI3D"  # 侧边栏中的标签页名称

    def draw(self, context):
        layout = self.layout
        
        # 添加执行按钮
        row = layout.row()
        row.scale_y = 1.5  # 增加按钮高度，使其更醒目
        op = row.operator("midi3d.execute", text="Start MIDI3D Reconstruction", icon='MESH_CUBE')
        
        # 添加状态显示
        if task_status.get("checking", False):
            box = layout.box()
            row = box.row()
            row.label(text="Task Status:", icon='INFO')
            row = box.row()
            row.label(text="Processing...", icon='TIME')
            
            # 添加取消按钮
            row = layout.row()
            op = row.operator("midi3d.cancel_task", text="Cancel Task", icon='CANCEL')
        
        # 添加分隔线
        layout.separator()
        
        # 添加说明信息
        box = layout.box()
        row = box.row()
        row.label(text="Instructions:", icon='HELP')
        row = box.row()
        row.label(text="1. Ensure MIDI3D is installed")
        row = box.row()
        row.label(text="2. Set up your scene")
        row = box.row()
        row.label(text="3. Click the button above")
# --- 新增代码结束 ---


# --- 新增代码：创建取消任务的操作符 ---
class MIDI3D_OT_CancelTask(Operator):
    """取消当前任务"""
    bl_idname = "midi3d.cancel_task"
    bl_label = "Cancel Task"
    bl_options = {'REGISTER'}

    def execute(self, context):
        task_status["checking"] = False
        task_status["task_id"] = None
        self.report({'INFO'}, "Task cancelled")
        return {'FINISHED'}
# --- 新增代码结束 ---


class MIDI3D_OT_Execute(Operator):
    """Execute MIDI3D reconstruction"""
    bl_idname = "midi3d.execute"
    bl_label = "MIDI3D Reconstruction"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        # 获取或创建MIDI3D摄像机，并检查是否为新创建的
        camera, was_created = self.get_or_create_midi3d_camera(context)

        # 如果是新创建的摄像机，则停止操作，提示用户调整
        if was_created:
            self.report({'INFO'}, "A new MIDI3D camera was created. Please adjust its position and run again.")
            # 仍然将摄像机设置为活动摄像机，方便用户直接调整
            context.scene.camera = camera
            return {'CANCELLED'}

        # 如果摄像机已存在，则继续执行流程
        # 设置为活动摄像机
        context.scene.camera = camera

        # 捕获场景图像
        image_path = self.capture_scene(context)

        # 启动MIDI3D程序
        self.start_midi3d_process(context, image_path)

        return {'FINISHED'}

    def get_or_create_midi3d_camera(self, context):
        """获取或创建名为MIDI3D的摄像机。返回摄像机对象和一个布尔值，指示是否为新建的。"""
        # 查找名为MIDI3D的摄像机
        camera = None
        for obj in bpy.data.objects:
            if obj.name == "MIDI3D" and obj.type == 'CAMERA':
                camera = obj
                break

        was_created = False
        # 如果没找到，创建一个新的
        if camera is None:
            was_created = True
            # 创建摄像机数据
            camera_data = bpy.data.cameras.new(name="MIDI3D")

            # 创建摄像机对象
            camera = bpy.data.objects.new("MIDI3D", camera_data)

            # 添加到场景
            context.collection.objects.link(camera)

            # 设置摄像机位置和旋转
            camera.location = (5, -5, 5)
            camera.rotation_euler = (1.1, 0, 0.785)

            # 发送警告
            self.report({'WARNING'}, "Created new MIDI3D camera as none was found")

        return camera, was_created

    def capture_scene(self, context):
        """使用当前摄像机捕获场景图像"""
        # 获取临时文件路径
        temp_dir = tempfile.mkdtemp()
        filename = f"midi3d_capture_{int(time.time())}.png"
        filepath = os.path.join(temp_dir, filename)

        # 保存原始渲染设置
        render = context.scene.render
        image_settings = render.image_settings
        original_file_format = image_settings.file_format
        original_filepath = render.filepath

        # 设置渲染参数
        image_settings.file_format = 'PNG'
        render.filepath = filepath
        render.resolution_x = 1920
        render.resolution_y = 1080
        render.film_transparent = False

        # 确保使用材质和纹理
        context.scene.eevee.use_ssr = True
        context.scene.eevee.use_ssr_refraction = True

        # 渲染图像
        bpy.ops.render.render(write_still=True)

        # 恢复原始设置
        image_settings.file_format = original_file_format
        render.filepath = original_filepath

        return filepath

    def start_midi3d_process(self, context, image_path):
        """启动MIDI3D进程"""
        # 这里暂时使用绝对路径，实际使用时应该从环境变量获取
        midi3d_exe = "MIDI3D"  # 环境变量，暂时用绝对路径代替
        
        # 暂时使用固定的路径，实际应该从环境变量获取
        exe_path = r"F:\publish\blender_selecter.exe"  # 需要替换成实际路径

        if not os.path.exists(exe_path):
            self.report({'ERROR'}, f"MIDI3D executable not found at: {exe_path}")
            return

        # 构建命令
        cmd = [exe_path, image_path]

        try:
            # 启动MIDI3D程序
            process = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                encoding='utf-8',      # 指定解码方式为 UTF-8
                errors='replace'       # 遇到解码错误时，用替换字符代替，避免程序崩溃
            )
            # 创建新线程等待进程结束并处理输出
            thread = threading.Thread(target=self.handle_process_output, args=(process, context))
            thread.daemon = True
            thread.start()

            self.report({'INFO'}, "MIDI3D process started, waiting for task ID...")

        except Exception as e:
            self.report({'ERROR'}, f"Failed to start MIDI3D process: {str(e)}")

    def handle_process_output(self, process, context):
        """处理子进程的输出"""
        task_id = None
        model_path = None
        task_failed = False
        failure_reason = ""

        # 读取所有输出
        for line in process.stdout:
            print(f"MIDI3D Output: {line.strip()}")
            
            # 检查是否有任务ID
            task_id_match = re.search(r'MIDI3D_TASK_ID:(.*)', line.strip())
            if task_id_match:
                task_id = task_id_match.group(1)
                continue
                
            # 检查是否有模型路径
            model_path_match = re.search(r'MIDI3D_MODEL_PATH:(.*)', line.strip())
            if model_path_match:
                model_path = model_path_match.group(1)
                continue
                
            # 检查任务是否失败
            task_failed_match = re.search(r'MIDI3D_TASK_FAILED:(.*)', line.strip())
            if task_failed_match:
                task_failed = True
                failure_reason = task_failed_match.group(1)
                continue
                
            # 检查任务是否被取消
            if "CANCELLED" in line.strip() and "MIDI3D_TASK_ID:" not in line.strip():
                task_id = "CANCELLED"
                continue

        process.wait()

        # 在主线程中执行后续操作
        if model_path and os.path.exists(model_path):
            # 直接导入模型
            self.import_model_directly(model_path, context)
        elif task_failed:
            # 任务失败
            self.report({'ERROR'}, f"MIDI3D task failed: {failure_reason}")
            task_status["checking"] = False
        elif task_id and task_id != "CANCELLED":
            # 如果没有模型路径但有任务ID，则回退到旧的轮询方式
            task_status["task_id"] = task_id
            task_status["checking"] = True
            bpy.ops.midi3d.check_task_status('INVOKE_DEFAULT')
        elif task_id == "CANCELLED":
            self.report({'INFO'}, "Task was cancelled by user")
            task_status["checking"] = False
        else:
            # 其他情况
            task_status["checking"] = False

    def import_model_directly(self, model_path, context):
        """直接导入模型到Blender"""
        try:
            # 导入GLB模型到Blender
            bpy.ops.import_scene.gltf(filepath=model_path)
            self.report({'INFO'}, f"Model imported successfully from {model_path}")
        except Exception as e:
            self.report({'ERROR'}, f"Error importing model: {str(e)}")
        finally:
            # 清理任务状态
            task_status["checking"] = False


class MIDI3D_OT_CheckTaskStatus(Operator):
    """Check task status and import model when ready"""
    bl_idname = "midi3d.check_task_status"
    bl_label = "Check Task Status"
    bl_options = {'REGISTER'}

    def execute(self, context):
        if not task_status.get("checking", False):
            self.report({'ERROR'}, "No task is being checked")
            return {'CANCELLED'}

        # 在新线程中检查状态
        thread = threading.Thread(target=self.check_status_loop, args=(context,))
        thread.daemon = True
        thread.start()

        return {'FINISHED'}

    def check_status_loop(self, context):
        """循环检查任务状态"""
        # 根据fastapi_server.py中的定义，服务器运行在8000端口
        api_url = "http://127.0.0.1:8000"  # 默认URL
        task_id = task_status.get("task_id")

        if not task_id:
            self.report({'ERROR'}, "No task ID available")
            task_status["checking"] = False
            return

        max_attempts = 30  # 最多尝试30次
        attempt = 0

        while attempt < max_attempts and task_status.get("checking", False):
            try:
                # 请求任务状态
                response = requests.get(f"{api_url}/status/{task_id}")

                if response.status_code == 200:
                    result = response.json()
                    status = result.get("status", "")

                    if status == "completed":
                        # 下载并导入模型
                        # 根据API定义，model_url在status响应中
                        model_url = result.get("model_url", "")
                        if model_url:
                            # 构建完整的模型下载URL
                            full_model_url = f"{api_url}{model_url}"
                            self.download_and_import_model(full_model_url, context)
                        else:
                            self.report({'ERROR'}, "Task completed but no model URL provided")
                        break
                    elif status == "failed":
                        self.report({'ERROR'}, f"Task failed: {result.get('error', 'Unknown error')}")
                        break
                    else:
                        # 任务仍在进行中，继续等待
                        attempt += 1
                        time.sleep(2)  # 每2秒检查一次
                else:
                    self.report({'ERROR'}, f"Failed to get task status: {response.status_code}")
                    break
            except Exception as e:
                self.report({'ERROR'}, f"Error checking task status: {str(e)}")
                break

        task_status["checking"] = False

    def download_and_import_model(self, model_url, context):
        """下载并导入模型到Blender"""
        try:
            # 下载模型文件
            response = requests.get(model_url, stream=True)

            if response.status_code == 200:
                # 创建临时文件
                temp_dir = tempfile.mkdtemp()
                filename = f"midi3d_model_{int(time.time())}.glb"
                filepath = os.path.join(temp_dir, filename)

                # 保存模型文件
                with open(filepath, 'wb') as f:
                    for chunk in response.iter_content(1024):
                        f.write(chunk)

                # 导入模型到Blender
                bpy.ops.import_scene.gltf(filepath=filepath)

                # 清理临时文件
                shutil.rmtree(temp_dir)

                self.report({'INFO'}, "Model imported successfully")
            else:
                self.report({'ERROR'}, f"Failed to download model: {response.status_code}")

        except Exception as e:
            self.report({'ERROR'}, f"Error downloading/importing model: {str(e)}")

# --- 修改：保留Header，但主要功能已移至Panel ---
class MIDI3D_HT_Header(Header):
    """添加MIDI3D按钮到3D视图顶部工具栏"""
    bl_space_type = 'VIEW_3D'

    def draw(self, context):
        layout = self.layout

        # 添加分隔符
        layout.separator()

        # 添加MIDI3D按钮
        op = layout.operator("midi3d.execute", text="MIDI3D", icon='RENDER_STILL')

        # 如果有任务正在检查，显示状态
        if task_status.get("checking", False):
            layout.label(text="Checking task status...", icon='TIME')
# --- 修改结束 ---


# --- 修改：更新类注册列表 ---
classes = (
    MIDI3D_PT_Panel,        # 新增：注册面板
    MIDI3D_OT_CancelTask,    # 新增：注册取消任务操作符
    MIDI3D_OT_Execute,
    MIDI3D_OT_CheckTaskStatus,
    MIDI3D_HT_Header,
)
# --- 修改结束 ---

def register():
    for cls in classes:
        bpy.utils.register_class(cls)

def unregister():
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)

if __name__ == "__main__":
    register()
