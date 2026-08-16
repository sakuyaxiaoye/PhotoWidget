<div align="center">

# 🖼️ 桌面照片组件 (PhotoWidget)

**专为海量图库打造的 Windows 桌面极简照片轮播小组件**  
*零壁纸闪烁 · 300万机械盘毫秒级秒开 · 350ms丝滑淡入淡出 · 智能色彩自适应 · 硬盘寿命休眠保护*

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0--windows-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Database](https://img.shields.io/badge/Catalog-SQLite%20WAL%20%2B%20MMAP-003B57?logo=sqlite&logoColor=white)](https://sqlite.org/)
[![Rendering](https://img.shields.io/badge/Rendering-SkiaSharp%20%2B%20WPF-EC5975?logo=nuget&logoColor=white)](https://github.com/mono/SkiaSharp)

<p align="center">
  <img src="src/DesktopPicture.App/Resources/app.png" width="160" height="160" alt="PhotoWidget Logo" />
</p>

</div>

---

## ✨ 核心特性 (Features)

### ⚡ 1. 300万+ 本地机械硬盘极速引擎
* **Win32 `LARGE_FETCH` 物理连续深度扫描**：直接绕过传统慢速 .NET 递归与二次属性寻道，300 万张机械硬盘（HDD）图库启动后毫秒级展示首图，后台静默并行建库。
* **SQLite MMAP + WAL 索引**：单图查询响应时间 `< 0.1ms`，零磁盘反复寻道开销。
* **2.5秒提前预解码管道**：换图倒计时结束前 2.5 秒提前在内存预热解码下一张图与色彩主题，换图时刻 **0 延迟、0 磁盘寻道等待** 瞬间呈现。

### 🎨 2. 视觉动效与自适应磨砂质感
* **350ms 硬件加速淡入淡出（Cross-Fade）**：采用双层交替缓冲区与 `CubicEase` 缓动动画，换图丝滑优雅，消除生硬瞬切。
* **智能色彩提取（Adaptive Palette）**：实时计算底图感知明度与色调，悬浮条与控制按钮自动变幻为与底图高度契合的温润毛玻璃或深邃透光质感。
* **EXIF 方向自适应纠偏**：手机立绘与单反竖拍照片自动识别 90°/180°/270° 纠偏展示。
* **自由调节圆角（0 ~ 100px）**：支持在设置中随意调整卡片圆角大小，或一键切换极客无圆角直角。

### 🛡️ 3. 机械硬盘寿命与能耗管理 (HDD Protection)
* **会话与熄屏感知**：智能监听 Windows `Win + L` 锁屏与显示器休眠事件。
* **100% 暂停磁盘 I/O**：熄屏或离席时立即暂停轮播与预解码，避免机械硬盘磁头无谓寻道磨损与发热。

### ⏪ 4. 双向 100 步历史记录穿梭
* **自由回退与前进**：支持回看刚才播放过的图片（`‹` 按钮），再次前进时同样享受预热瞬切，到达末尾后才抽取新图。
* **鼠标手势**：鼠标停在组件上，**向上滚轮**（上一张）/ **向下滚轮**（下一张）极速翻阅。

### 🖥️ 5. Wallpaper Engine 零闪烁桌面嵌入
* **双层智能宿主架构**：采用 `BottomWindowHost` 与 `ExplorerDesktopHost` 双重 Win32 挂载机制；
* **动态壁纸安全互斥**：自动检测 Wallpaper Engine、Lively 等动态壁纸，彻底杜绝 `0x052C` 桌面重绘导致的闪烁问题。

### 📁 6. 全现代图片格式生态
* 原生支持：`.jpg`, `.jpeg`, `.png`, `.webp`, `.avif`, `.heic`, `.heif`, `.bmp`, `.tiff`, `.tif`, `.jfif` 以及 `.gif`（提取高清首帧），并对损坏文件提供零崩溃跳过保护。

---

## 🏗️ 架构设计 (Architecture)

```mermaid
graph TD
    subgraph StorageEngine [存储与扫描引擎]
        Disk[300万+ 本地图片 (机械盘 / SSD)] -->|Win32 LARGE_FETCH DFS| Scanner[FastDirectoryEnumerator]
        Scanner -->|批量事务写入| DB[(SQLite WAL + MMAP)]
        DB -->|无分配轻量迭代| Snapshot[CompactIdSnapshot 内存快照]
    end

    subgraph PlaybackEngine [播放调度与预解码]
        Snapshot --> Selector[RandomSelector 随机挑选器]
        Selector --> Pipeline[2.5秒提前预解码管道]
        Pipeline --> Skia[SkiaSharp EXIF纠偏与Cover缩放]
        Power[Win+L / 熄屏感知] -->|100%切断IO| Pipeline
    end

    subgraph UIRendering [桌面渲染与交互]
        Skia --> DoubleBuff[双层硬件加速 Image 缓冲区]
        DoubleBuff -->|350ms CubicEase| CrossFade[平滑淡入淡出]
        CrossFade --> Adaptive[AdaptivePalette 动态色彩采样]
        History[PlaybackHistory 100步双向历史栈] <-->|滚轮/按钮穿梭| DoubleBuff
    end
```

---

## 🚀 快速开始与编译 (Getting Started)

### 环境要求
* Windows 10 (1809+) 或 Windows 11
* [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本

### 1. 克隆仓库
```bash
git clone https://github.com/your-username/PhotoWidget.git
cd PhotoWidget
```

### 2. 运行单元测试
项目自带 37 项覆盖核心目录扫描、SQLite 优化、内存防泄漏与历史穿梭的自动化测试套件：
```bash
dotnet test
```

### 3. 本地发布 (Release Build)
```bash
dotnet publish src/DesktopPicture.App/DesktopPicture.App.csproj -c Release -r win-x64 --self-contained false -o publish
```
发布完成后，直接运行 `publish\PhotoWidget.exe` 即可使用！

---

## ⌨️ 常用交互手势 (Quick Controls)

| 操作 | 动作 | 说明 |
| :--- | :--- | :--- |
| **滚轮向下** | 切换下一张 | 在组件上滑动滚轮向下切换 |
| **滚轮向上** | 回看上一张 | 穿梭回退历史记录 |
| **拖拽四角/边缘** | 自由缩放 | 实时自由调节组件尺寸（支持 4K 超清自适应） |
| **右键组件** | 上下文菜单 | 打开设置、暂停轮播、打开原图或所在文件夹 |
| **右键托盘图标** | 系统管理 | 一键开/关开机自启动、新建多组件、打开配置目录 |

---

## 📄 开源许可证 (License)

本项目采用 [MIT License](LICENSE) 许可证开源，欢迎提交 Issue 与 Pull Request！
