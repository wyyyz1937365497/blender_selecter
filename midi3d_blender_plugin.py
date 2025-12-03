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
    "location": "View3D > Header",
    "description": "Capture scene with MIDI3D camera and import generated model",
    "category": "3D View",
}

# 全局变量存储任务状态
task_status = {"checking": False, "task_id": None}

class MIDI3D_OT_Execute(Operator):
    """Execute MIDI3D reconstruction"""
    bl_idname = "midi3d.execute"
    bl_label = "MIDI3D Reconstruction"
    bl_options = {'REGISTER', 'UNDO'}

    midi3d_path: StringProperty(
        name="MIDI3D Path",
        description="Path to MIDI3D executable",
        default="C:\MIDI3D\midi3d.exe"  # 默认路径，用户可在设置中修改
    )

    api_url: StringProperty(
        name="API URL",
        description="URL of the backend API",
        default="http://127.0.0.1:8000"
    )

    def execute(self, context):
        # 获取或创建MIDI3D摄像机
        camera = self.get_or_create_midi3d_camera(context)

        # 设置为活动摄像机
        context.scene.camera = camera

        # 捕获场景图像
        image_path = self.capture_scene(context)

        # 启动MIDI3D程序
        self.start_midi3d_process(context, image_path)

        return {'FINISHED'}

    def get_or_create_midi3d_camera(self, context):
        """获取或创建名为MIDI3D的摄像机"""
        # 查找名为MIDI3D的摄像机
        camera = None
        for obj in bpy.data.objects:
            if obj.name == "MIDI3D" and obj.type == 'CAMERA':
                camera = obj
                break

        # 如果没找到，创建一个新的
        if camera is None:
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

        return camera

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
        addon_prefs = context.preferences.addons[__name__].preferences
        midi3d_exe = addon_prefs.midi3d_path

        if not midi3d_exe or not os.path.exists(midi3d_exe):
            self.report({'ERROR'}, f"MIDI3D executable not found at: {midi3d_exe}")
            return

        # 构建命令
        cmd = [midi3d_exe, image_path]

        try:
            # 启动MIDI3D程序
            process = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                universal_newlines=True
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

        # 读取所有输出
        for line in process.stdout:
            print(f"MIDI3D Output: {line.strip()}")
            match = re.search(r'TASK_ID:(.*)', line.strip())
            if match:
                task_id = match.group(1)
                break

        process.wait()

        # 在主线程中执行后续操作
        if task_id and task_id != "CANCELLED":
            task_status["task_id"] = task_id
            task_status["checking"] = True
            bpy.ops.midi3d.check_task_status('INVOKE_DEFAULT')
        elif task_id == "CANCELLED":
            self.report({'INFO'}, "Task was cancelled by user")

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
        addon_prefs = context.preferences.addons[__name__].preferences
        api_url = addon_prefs.api_url
        task_id = task_status.get("task_id")

        if not task_id:
            self.report({'ERROR'}, "No task ID available")
            task_status["checking"] = False
            return

        max_attempts = 30  # 最多尝试30次
        attempt = 0

        while attempt < max_attempts:
            try:
                # 请求任务状态
                response = requests.get(f"{api_url}/task_status/{task_id}")

                if response.status_code == 200:
                    result = response.json()
                    status = result.get("status", "")

                    if status == "completed":
                        # 下载并导入模型
                        model_url = result.get("model_url", "")
                        if model_url:
                            self.download_and_import_model(model_url, context)
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
                    self.report({'ERROR'}, f"Failed to check task status: {response.status_code}")
                    break

            except Exception as e:
                self.report({'ERROR'}, f"Error checking task status: {str(e)}")
                break

        # 重置检查状态
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

class MIDI3D_AddonPreferences(bpy.types.AddonPreferences):
    """插件设置"""
    bl_idname = __name__

    midi3d_path: StringProperty(
        name="MIDI3D Path",
        description="Path to MIDI3D executable",
        subtype='FILE_PATH',
        default="C:\MIDI3D\midi3d.exe"
    )

    api_url: StringProperty(
        name="API URL",
        description="URL of the backend API",
        default="http://127.0.0.1:8000"
    )

    def draw(self, context):
        layout = self.layout
        layout.label(text="MIDI3D Settings:")
        layout.prop(self, "midi3d_path")
        layout.prop(self, "api_url")

classes = (
    MIDI3D_OT_Execute,
    MIDI3D_OT_CheckTaskStatus,
    MIDI3D_HT_Header,
    MIDI3D_AddonPreferences,
)

def register():
    for cls in classes:
        bpy.utils.register_class(cls)

def unregister():
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)

if __name__ == "__main__":
    register()
