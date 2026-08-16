<div align="center">

# PhotoWidget (桌面照片组件)

一个轻量、高性能的 Windows 桌面照片轮播小组件。

专为拥有海量本地图片收藏的用户设计，在保持极低系统资源占用的同时，提供平滑自然的视觉过渡与出色的桌面交互体验。

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0--windows-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)

<br/>
<img src="src/DesktopPicture.App/Resources/app.png" width="128" height="128" alt="PhotoWidget Logo" />
<br/>

</div>

---

## 为什么做这个项目？

市面上的桌面相框或壁纸小工具在日常使用中常遇到几个痛点：
1. **大图库扫描卡顿**：本地图片数万甚至数十万张时，常规软件扫描缓慢，甚至导致界面卡死；
2. **频繁读盘与能耗**：每次换图临时读取磁盘容易产生延迟，且在电脑锁屏离席时仍持续空转；
3. **动态壁纸冲突**：很多桌面挂载工具会导致 Wallpaper Engine 或 Lively Wallpaper 频繁闪烁、重绘失效；
4. **视觉生硬**：图片瞬切缺乏过渡，界面风格与现代 Windows 桌面不协调。

PhotoWidget 针对这些问题做了底层优化，旨在提供一个**安静、流畅、不打扰且低能耗**的桌面照片挂件。

---

## 主要功能与技术实现

### 1. 海量图库秒级响应
- **Win32 底层遍历**：采用 Windows 原生 `FindFirstFileExW`（开启 `FIND_FIRST_EX_LARGE_FETCH`），绕过标准库反射开销，直接读取物理目录流；
- **SQLite 索引与紧凑快照**：扫描结果持久化存储于 SQLite（开启 WAL 模式与 MMAP 内存映射），运行时将百万级图片 ID 映射为紧凑内存结构，单次选图耗时小于 0.1ms；
- **增量同步**：只在首次启动或文件夹有变动时进行后台增量扫描，不影响前台操作。

### 2. 后台预解码与低功耗模式
- **提前预热缓冲**：在设定轮播间隔到达前 2.5 秒，后台线程提前完成下一张图片的解码和缩放，轮播触发时直接从内存呈现，换图 0 延迟；
- **熄屏与锁屏感知**：智能监听系统锁屏（`Win + L`）与显示器休眠广播，自动切断磁盘读取并暂停定时器，降低系统能耗。

### 3. 视觉与动效细节
- **平滑渐变动效**：基于 WPF 硬件加速的双图层交替渲染，每次切换伴随 350ms 淡入淡出过渡（Cross-Fade）；
- **自适应配色**：根据当前展示图片的色彩与明度，自动调整悬浮控制栏的背景毛玻璃饱和度与图标明暗；
- **EXIF 角度校正**：基于 SkiaSharp 解码，自动识别手机竖拍或相机旋转标记，确保画面方向正常；
- **Windows 11 原生圆角**：支持一键开启与 Windows 11 风格一致的标准圆角，窗体支持任意边缘与四角自由缩放。

### 4. 交互与历史记录
- **双向历史穿梭**：内置 100 步历史栈，点击悬浮栏 `‹` 或向上滚轮可随时回退查看刚播过的照片；继续向下滚动会正向重播，到达末尾后才随机抽取新图；
- **全屏与壁纸兼容**：通过 Win32 `SetWindowLongPtr` 将窗口安全挂载于桌面底层，自动兼容 Wallpaper Engine 等动态壁纸，不产生重绘闪烁；右键菜单支持在点击桌面任意区域时自动失焦收起。

### 5. 常见图片格式全支持
支持 `.jpg`, `.jpeg`, `.png`, `.webp`, `.avif`, `.heic`, `.heif`, `.bmp`, `.tiff`, `.jfif` 以及 `.gif`（提取高清静态首帧），遇到损坏文件自动跳过，保证播放不中断。

---

## 操作说明

| 方式 | 操作 | 功能说明 |
| :--- | :--- | :--- |
| **滚轮向下** | 鼠标悬停在组件上向下滚动 | 切换下一张图片 |
| **滚轮向上** | 鼠标悬停在组件上向上滚动 | 回看上一张图片（历史记录） |
| **鼠标拖拽** | 拖动窗口边缘或右下角手柄 | 自由调整组件尺寸 |
| **悬浮控制条** | 鼠标移入底部区域 | 显示当前图片路径、一键打开原图、打开所在目录 |
| **右键菜单** | 右击组件 | 打开偏好设置、暂停/继续轮播、新建组件 |
| **托盘图标** | 右击右下角通知区域图标 | 开机自启动开关、管理所有组件、退出程序 |

---

## 编译与构建

### 准备环境
- Windows 10 (1809+) / Windows 11
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本

### 常用命令
```powershell
# 1. 克隆代码
git clone https://github.com/sakuyaxiaoye/PhotoWidget.git
cd PhotoWidget

# 2. 运行自动化测试（共 37 项单元与性能测试）
dotnet test

# 3. 编译并发布 Release 版本
dotnet publish src/DesktopPicture.App/DesktopPicture.App.csproj -c Release -r win-x64 --self-contained false -o publish
```

编译输出位于 `publish\` 目录，双击 `PhotoWidget.exe` 即可运行。

---

## 开源协议

本项目基于 [MIT License](LICENSE) 协议开源。
