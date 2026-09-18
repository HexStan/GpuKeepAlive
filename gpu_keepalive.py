#!/usr/bin/env python3
# -*- coding: utf-8 -*-  # noqa: UP009
"""
GPU KeepAlive - 轻量级 GPU 3D 引擎防休眠保活脚本 (纯 Python + Windows OpenGL，零第三方依赖)
用途:
  通过向 GPU 持续提交微量 3D 渲染帧，保持显卡处于活跃时钟频率，
  防止因深度休眠/节能调度引起的画面静止后唤醒卡顿 (0.x秒延迟)。

运行方式:
  1. 在 Windows 设置 -> 系统 -> 屏幕 -> 显示卡 (图形) 中，将 python.exe 添加并设置为“节能 (核显)”。
  2. 运行: python gpu_keepalive.py
"""

import argparse
import ctypes
import math
import sys
import time
from ctypes import wintypes

# 加载 Windows 系统 DLL
user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32
opengl32 = ctypes.windll.opengl32
kernel32 = ctypes.windll.kernel32

# 修复 64位 Win32 消息处理函数签名
user32.DefWindowProcW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.DefWindowProcW.restype = ctypes.c_int64

WNDPROC = ctypes.WINFUNCTYPE(ctypes.c_int64, wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM)

class WNDCLASSEXW(ctypes.Structure):
    _fields_ = [
        ('cbSize', wintypes.UINT),
        ('style', wintypes.UINT),
        ('lpfnWndProc', WNDPROC),
        ('cbClsExtra', ctypes.c_int),
        ('cbWndExtra', ctypes.c_int),
        ('hInstance', wintypes.HINSTANCE),
        ('hIcon', wintypes.HICON),
        ('hCursor', wintypes.HCURSOR),
        ('hbrBackground', wintypes.HBRUSH),
        ('lpszMenuName', wintypes.LPCWSTR),
        ('lpszClassName', wintypes.LPCWSTR),
        ('hIconSm', wintypes.HICON)
    ]

class PIXELFORMATDESCRIPTOR(ctypes.Structure):
    _fields_ = [
        ('nSize', wintypes.WORD),
        ('nVersion', wintypes.WORD),
        ('dwFlags', wintypes.DWORD),
        ('iPixelType', ctypes.c_byte),
        ('cColorBits', ctypes.c_byte),
        ('cRedBits', ctypes.c_byte),
        ('cRedShift', ctypes.c_byte),
        ('cGreenBits', ctypes.c_byte),
        ('cGreenShift', ctypes.c_byte),
        ('cBlueBits', ctypes.c_byte),
        ('cBlueShift', ctypes.c_byte),
        ('cAlphaBits', ctypes.c_byte),
        ('cAlphaShift', ctypes.c_byte),
        ('cAccumBits', ctypes.c_byte),
        ('cAccumRedBits', ctypes.c_byte),
        ('cAccumGreenBits', ctypes.c_byte),
        ('cAccumBlueBits', ctypes.c_byte),
        ('cAccumAlphaBits', ctypes.c_byte),
        ('cDepthBits', ctypes.c_byte),
        ('cStencilBits', ctypes.c_byte),
        ('cAuxBuffers', ctypes.c_byte),
        ('iLayerType', ctypes.c_byte),
        ('bReserved', ctypes.c_byte),
        ('dwLayerMask', wintypes.DWORD),
        ('dwVisibleMask', wintypes.DWORD),
        ('dwDamageMask', wintypes.DWORD)
    ]

def main():
    parser = argparse.ArgumentParser(
        description="GPU KeepAlive (Python版) - 持续轻微 3D 负载防止 GPU 深度休眠",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
提示:
  请在 Windows 设置 -> 系统 -> 屏幕 -> 图形 (显示卡) 中，
  添加 python.exe (或虚拟环境中的 python.exe)，并将其首选项设为“节能 (核显)”。
"""
    )
    parser.add_argument("-f", "--fps", type=int, default=30, help="渲染保活帧率 (默认: 30 FPS)")
    parser.add_argument("-i", "--intensity", type=int, choices=[1, 2], default=1,
                        help="负载等级: 1=轻微(glClear，约0.2%%~0.5%%)，2=中等(画旋转几何体，约1%%~2%%)")
    parser.add_argument("--hide", action="store_true", help="启动后自动隐藏控制台窗口")
    args = parser.parse_args()

    if args.hide:
        hwnd_con = kernel32.GetConsoleWindow()
        if hwnd_con:
            user32.ShowWindow(hwnd_con, 0) # SW_HIDE

    # 注册隐藏离屏窗口类
    def def_wnd_proc(hWnd, msg, wParam, lParam):
        return user32.DefWindowProcW(hWnd, msg, wParam, lParam)

    wndproc = WNDPROC(def_wnd_proc)
    class_name = f"GpuKeepAlive_Py_{kernel32.GetCurrentProcessId()}"
    h_inst = kernel32.GetModuleHandleW(None)

    wnd_class = WNDCLASSEXW()
    wnd_class.cbSize = ctypes.sizeof(WNDCLASSEXW)
    wnd_class.style = 0x0020 # CS_OWNDC
    wnd_class.lpfnWndProc = wndproc
    wnd_class.hInstance = h_inst
    wnd_class.lpszClassName = class_name

    user32.RegisterClassExW(ctypes.byref(wnd_class))

    # 创建一个隐藏的 64x64 离屏窗口
    WS_POPUP = 0x80000000
    hwnd = user32.CreateWindowExW(0, class_name, "GpuKeepAlive_Hidden", WS_POPUP, -200, -200, 64, 64, None, None, h_inst, None)
    hdc = user32.GetDC(hwnd)

    pfd = PIXELFORMATDESCRIPTOR()
    pfd.nSize = ctypes.sizeof(PIXELFORMATDESCRIPTOR)
    pfd.nVersion = 1
    pfd.dwFlags = 0x00000004 | 0x00000020 | 0x00000001 # DRAW_TO_WINDOW | SUPPORT_OPENGL | DOUBLEBUFFER
    pfd.iPixelType = 0
    pfd.cColorBits = 32
    pfd.cDepthBits = 16

    pf = gdi32.ChoosePixelFormat(hdc, ctypes.byref(pfd))
    gdi32.SetPixelFormat(hdc, pf, ctypes.byref(pfd))

    hrc = opengl32.wglCreateContext(hdc)
    opengl32.wglMakeCurrent(hdc, hrc)

    glGetString = opengl32.glGetString
    glGetString.restype = ctypes.c_char_p
    renderer_name = glGetString(0x1F01).decode('utf-8', errors='ignore')
    vendor_name = glGetString(0x1F00).decode('utf-8', errors='ignore')

    print("=" * 68)
    print(" GPU KeepAlive (Python版) - 运行中")
    print("=" * 68)
    print(f"当前驱动渲染器: {renderer_name} ({vendor_name})")
    print(f"保活运行帧率  : {args.fps} FPS")
    print(f"负载强度等级  : 等级 {args.intensity} ({'轻微 Clear' if args.intensity == 1 else '中等 旋转几何图元'})")
    print(f"进程 PID      : {kernel32.GetCurrentProcessId()}")
    print("-" * 68)
    print("提示: 按 Ctrl+C 停止保活程序。")
    print("=" * 68 + "\n")

    glClearColor = opengl32.glClearColor
    glClearColor.argtypes = [ctypes.c_float, ctypes.c_float, ctypes.c_float, ctypes.c_float]
    glClear = opengl32.glClear
    SwapBuffers = gdi32.SwapBuffers

    glBegin = opengl32.glBegin
    glEnd = opengl32.glEnd
    glColor3f = opengl32.glColor3f
    glColor3f.argtypes = [ctypes.c_float, ctypes.c_float, ctypes.c_float]
    glVertex2f = opengl32.glVertex2f
    glVertex2f.argtypes = [ctypes.c_float, ctypes.c_float]
    glRotatef = opengl32.glRotatef
    glRotatef.argtypes = [ctypes.c_float, ctypes.c_float, ctypes.c_float, ctypes.c_float]

    frame_interval = 1.0 / max(1, args.fps)
    sim_time = 0.0
    frame_count = 0
    t_start = time.perf_counter()
    last_stat_time = t_start
    last_stat_frames = 0

    try:
        while True:
            t0 = time.perf_counter()
            sim_time += frame_interval
            frame_count += 1

            r = 0.5 + 0.5 * math.sin(sim_time)
            g = 0.5 + 0.5 * math.cos(sim_time * 0.7)
            b = 0.5

            glClearColor(r, g, b, 1.0)
            glClear(0x4000) # GL_COLOR_BUFFER_BIT

            if args.intensity == 2:
                glRotatef(1.0, 0.0, 0.0, 1.0)
                glBegin(7) # GL_QUADS
                glColor3f(1.0 - r, g, 0.8)
                glVertex2f(-0.5, -0.5)
                glVertex2f(0.5, -0.5)
                glVertex2f(0.5, 0.5)
                glVertex2f(-0.5, 0.5)
                glEnd()

            SwapBuffers(hdc)

            t_now = time.perf_counter()
            if not args.hide and (t_now - last_stat_time) >= 3.0:
                elapsed_period = t_now - last_stat_time
                actual_fps = (frame_count - last_stat_frames) / elapsed_period
                last_stat_time = t_now
                last_stat_frames = frame_count
                total_sec = int(t_now - t_start)
                hh, mm, ss = total_sec // 3600, (total_sec % 3600) // 60, total_sec % 60
                sys.stdout.write(f"\r[运行状态] 运行时间: {hh:02d}:{mm:02d}:{ss:02d} | 提交帧数: {frame_count} | 实时帧率: {actual_fps:.1f} FPS   ")
                sys.stdout.flush()

            t_elapsed = time.perf_counter() - t0
            sleep_needed = frame_interval - t_elapsed
            if sleep_needed > 0.001:
                time.sleep(sleep_needed)

    except KeyboardInterrupt:
        print("\n\n正在退出...")
    finally:
        opengl32.wglMakeCurrent(None, None)
        opengl32.wglDeleteContext(hrc)
        user32.ReleaseDC(hwnd, hdc)
        user32.DestroyWindow(hwnd)
        print("保活已安全停止。")

if __name__ == "__main__":
    main()
