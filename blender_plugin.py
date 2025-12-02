import bpy
import subprocess
import os
import sys
import json
import time
import threading
import re
from bpy.props import StringProperty, BoolProperty
from bpy.types import Operator, Panel

bl_info = {
    "name": "MIDI-3D Reconstruction Selector",
    "author": "Your Name",
    "version": (1, 0),
    "blender": (2, 80, 0),
    "location": "View3D > Sidebar > MIDI-3D Tab",
    "description": "Capture images, select region, and send to reconstruction server",
    "category": "3D View",
}

class MIDI3D_OT_SelectRegion(Operator):
    """Run the selector application to choose region"""
    bl_idname = "midi3d.select_region"
    bl_label = "Select Region"
    bl_options = {'REGISTER'}

    filepath: StringProperty(subtype="FILE_PATH")
    filter_glob: StringProperty(default="*.png", options={'HIDDEN'})
    
    def execute(self, context):
        # 获取MAUI应用的路径
        addon_prefs = context.preferences.addons[__name__].preferences
        selector_app = addon_prefs.selector_app_path
        
        if not selector_app or not os.path.exists(selector_app):
            self.report({'ERROR'}, "Selector application not found. Please check the path in addon preferences.")
            return {'CANCELLED'}
            
        # 构建命令
        cmd = [selector_app, self.filepath]
        
        try:
            # 启动MAUI应用
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
            
        except Exception as e:
            self.report({'ERROR'}, f"Failed to start selector application: {str(e)}")
            return {'CANCELLED'}
            
        return {'FINISHED'}
        
    def handle_process_output(self, process, context):
        """处理子进程的输出"""
        task_id = None
        
        # 读取所有输出
        for line in process.stdout:
            match = re.search(r'TASK_ID:(.*)', line.strip())
            if match:
                task_id = match.group(1)
                break
                
        process.wait()
        
        # 在主线程中执行后续操作
        if task_id and task_id != "CANCELLED":
            bpy.ops.midi3d.check_task_status(task_id=task_id)
        elif task_id == "CANCELLED":
            print("Task was cancelled by user")

    def invoke(self, context, event):
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}

class MIDI3D_OT_CheckTaskStatus(Operator):
    """Check task status and import model when ready"""
    bl_idname = "midi3d.check_task_status"
    bl_label = "Check Task Status"
    bl_options = {'REGISTER'}
    
    task_id: StringProperty(name="Task ID")
    checking: BoolProperty(default=False)
    
    def execute(self, context):
        if not self.checking:
            self.checking = True
            # 在新线程中检查状态
            thread = threading.Thread(target=self.check_status_loop)
            thread.daemon = True
            thread.start()
        return {'FINISHED'}
        
    def check_status_loop(self):
        """循环检查任务状态"""
        # 这里应该是实际的服务器API调用
        # 模拟检查过程
        for i in range(10):  # 最多检查10次
            time.sleep(2)  # 每2秒检查一次
            
            # 模拟API响应
            # 在实际应用中，这里会是一个HTTP请求到你的FastAPI服务器
            if i >= 4:  # 模拟第5次检查时完成
                # 模拟下载模型文件
                model_url = f"https://example.com/models/{self.task_id}.glb"
                self.import_model(model_url)
                break
                
    def import_model(self, model_url):
        """导入模型到场景"""
        # 在实际应用中，你需要从model_url下载文件然后导入
        # 这里只是示例
        print(f"Importing model from {model_url}")
        bpy.ops.import_scene.gltf(filepath="/path/to/downloaded/model.glb")

class MIDI3D_OT_CaptureViewport(Operator):
    """Capture current viewport as PNG"""
    bl_idname = "midi3d.capture_viewport"
    bl_label = "Capture Viewport"
    bl_options = {'REGISTER'}
    
    def execute(self, context):
        # 获取临时文件路径
        temp_dir = context.preferences.filepaths.temporary_directory
        if not temp_dir:
            temp_dir = "/tmp" if sys.platform != "win32" else os.environ.get("TEMP", "C:\\temp")
            
        filename = f"viewport_capture_{int(time.time())}.png"
        filepath = os.path.join(temp_dir, filename)
        
        # 设置渲染参数
        render = bpy.context.scene.render
        image_settings = render.image_settings
        original_file_format = image_settings.file_format
        original_filepath = render.filepath
        
        # 更改为PNG格式并保存
        image_settings.file_format = 'PNG'
        render.filepath = filepath
        bpy.ops.render.opengl(write_still=True)
        
        # 恢复原始设置
        image_settings.file_format = original_file_format
        render.filepath = original_filepath
        
        # 打开文件选择器以选择区域
        bpy.ops.midi3d.select_region('INVOKE_DEFAULT', filepath=filepath)
        
        return {'FINISHED'}

class MIDI3D_PT_Panel(Panel):
    """Creates a Panel in the 3D View sidebar"""
    bl_label = "MIDI-3D Reconstruction"
    bl_idname = "MIDI3D_PT_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'MIDI-3D'

    def draw(self, context):
        layout = self.layout
        
        row = layout.row()
        row.operator("midi3d.capture_viewport", text="Capture Viewport", icon='RENDER_STILL')
        
        row = layout.row()
        row.operator("midi3d.select_region", text="Select from Image", icon='FILE_IMAGE')

class MIDI3D_AddonPreferences(bpy.types.AddonPreferences):
    bl_idname = __name__
    
    selector_app_path: StringProperty(
        name="Selector App Path",
        description="Path to the MAUI selector application",
        subtype='FILE_PATH',
        default=""
    )
    
    def draw(self, context):
        layout = self.layout
        layout.label(text="MIDI-3D Reconstruction Settings:")
        layout.prop(self, "selector_app_path")

classes = (
    MIDI3D_OT_CaptureViewport,
    MIDI3D_OT_SelectRegion,
    MIDI3D_OT_CheckTaskStatus,
    MIDI3D_PT_Panel,
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