# Desktop Picture 技术规格

- 状态：Phase 0 Ready；完整实现受桌面宿主技术门约束
- 版本：0.1.0
- 日期：2026-08-16
- 目标平台：Windows 10/11，首发 `win-x64`
- 文档语言：中文

## 1. 目标

构建一个独立、轻量、高性能的 Windows 桌面程序，通过最多四个可拖动图片组件，随机循环展示本地文件夹及其子文件夹中的图片。

程序必须能处理单个根目录中 30 万张以上的图片，目录扫描、索引、图片解码和缩放不得阻塞托盘或设置界面。

## 2. 产品边界

### 2.1 范围内

- 独立 Windows 桌面程序，不依附 Rainmeter、Wallpaper Engine 或其他宿主。
- 最多同时创建四个图片组件。
- 每个组件独立配置根文件夹、窗口位置、窗口尺寸、切换间隔和播放状态。
- 递归读取根文件夹的全部普通子目录。
- 支持 JPG、JPEG、PNG、WebP 和动态 GIF。
- 完全随机选图；当至少有两张健康图片时，相邻两次不得显示同一路径。
- 图片保持宽高比并尽可能铺满组件；比例不一致时居中裁剪。
- GIF 循环播放，到统一切换间隔后立即换图，不等待动画完整播放。
- 组件始终可以直接拖动，但只能在设置界面修改尺寸。
- 支持暂停、继续、立即换图和错误记录查看。
- 通过 Windows 通知区域图标管理组件。
- 记住所有设置和窗口状态。
- 支持多显示器和显示器断开恢复。
- 提供可选的登录自启动，默认关闭。

### 2.2 范围外

- 视频、音频、网络图片、网络共享目录的性能保证、在线相册和云同步。
- 根据图片内容去重；“不重复”仅按规范化文件路径判断。
- 多台电脑之间同步配置。
- 图片编辑、标签、收藏和搜索。
- 桌面点击穿透。
- ARM64、x86 和非 Windows 平台的首发构建。
- 自动更新、Microsoft Store 发布和便携版。
- 组件不保证位于桌面图标下方；它位于桌面交互层，覆盖区域内的图标会被遮住并且不能接收点击。

## 3. 平台与发布基线

### 3.1 技术栈

- 语言：C# 14。
- 运行时：.NET 10 LTS，目标框架 `net10.0-windows`。
- UI：WPF。
- Win32 互操作：窗口层级、通知区域、Explorer 重启检测、显示器与 DPI 处理。
- 图像解码：SkiaSharp 的确定版本，首个实现基线为 `3.119.4`。
- 索引：`Microsoft.Data.Sqlite`，直接使用 ADO.NET，不引入 ORM。
- 测试：xUnit；性能测试为独立 Release 构建的 benchmark/soak test 程序。

选择 WPF 的原因是它是 Windows 专用、成熟的 HWND 桌面框架，能以较小互操作面完成无边框多窗口、设置窗口和通知区域集成。.NET 10 是当前活跃的 LTS，支持期至 2028-11-14。WebP 不得依赖用户是否安装 Windows WebP 扩展，因此所有受支持格式统一走随应用发布的确定性解码路径。

### 3.2 操作系统支持

- 完整支持并作为发布门：Windows 11 x64 的仍受支持版本。
- 完整支持并作为发布门：仍处于微软支持期的 Windows 10 x64 LTSC/Enterprise 版本。
- Windows 10 Home/Pro 已退出上游支持的版本可以做兼容性测试，但不得宣称获得微软或 .NET 的官方支持。
- 要求 Per-Monitor V2 DPI awareness。

### 3.3 发布形式

- 首发为 WiX 构建的签名 per-user MSI，安装到 `%LocalAppData%\Programs\DesktopPicture`，不要求管理员权限。
- 应用以 self-contained `win-x64` 形式发布，目标机器无需预装 .NET Desktop Runtime。
- Release 构建启用 ReadyToRun；WPF 与原生解码依赖未证明可安全裁剪前，禁止启用 trimming。
- MSI 必须包含 SkiaSharp 的 `win-x64` 原生依赖，并使用稳定 UpgradeCode 支持同用户就地升级。
- 登录自启动使用当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`；值为带双引号的绝对 exe 路径加 `--background`。升级后重写，卸载或关闭选项时删除。
- 开发构建允许未签名，正式发布构建必须签名。

## 4. 关键技术门：桌面宿主

### 4.1 平台限制

Windows 10/11 没有公开、稳定的 API，可以把任意第三方窗口承诺为“位于壁纸上方、桌面图标附近、所有普通应用下方”的桌面组件。传统 Windows Desktop Gadgets 已被移除；`IDesktopWallpaper` 只管理壁纸，不承载任意交互窗口。

常见的 Explorer `Progman`/`WorkerW` 桌面依附方法依赖未文档化的 Shell 窗口拓扑。它可以作为本产品的实现手段，但不得被视为 Windows 平台合同。

### 4.2 宿主抽象

定义内部接口 `IDesktopHost`：

```text
Attach(widgetHwnd) -> AttachResult
Detach(widgetHwnd)
ReattachAll(reason)
GetDesktopBounds()
Health -> Healthy | Degraded | Unavailable
```

实现两个适配器：

1. `ExplorerDesktopHost`
   - 默认路径。
   - 定位适用的 Explorer 桌面宿主 HWND，并通过受控 Win32 互操作依附组件窗口。
   - 使用专用 Win32 `WS_CHILD`/`HwndSource` 宿主承载 WPF visual，不直接把普通 WPF 顶层 `Window` 粗暴 reparent 到 Explorer。
   - 负责同步 `WS_CHILD`/`WS_POPUP` 等窗口样式和 DPI awareness，并在 detach/销毁时释放宿主资源。
   - Explorer 重启、Shell 窗口重建、显示器拓扑改变后重新发现并依附。
2. `BottomWindowHost`
   - 降级路径。
   - 使用非 Topmost、`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` 的普通顶层窗口，并维护低 Z 序。
   - 只能保证尽量不遮挡普通应用，不能保证与桌面图标的严格层级。

降级时，通知区域必须显示“桌面层已降级”，日志记录原因。降级模式不算桌面宿主验收通过。

### 4.3 Phase 0 原型门

在完整功能开发前，必须先完成最小宿主原型。只有下列项目全部通过，才能继续主实现：

- Windows 11 和受支持 Windows 10 测试矩阵上，组件位于普通应用下方且壁纸上方。
- 组件可以直接拖动，不抢占前台焦点。
- Explorer 正常重启和被强制结束后，组件能在 5 秒内重新依附。
- “显示桌面”、锁屏/解锁、睡眠/恢复后层级正确。
- 多显示器、负坐标显示器和不同 DPI 缩放下位置正确。
- 显示器断开后组件回到主屏工作区内。
- 桌面图标显示、隐藏和刷新不会使组件永久丢失。
- 组件显示在壁纸和桌面图标之上；其矩形内图标被遮挡且不能点击，移开组件后图标恢复可见和可点击。
- 普通应用启动、激活、最大化、最小化、全屏或设置 Always-on-top 后，组件均不得盖到该应用上方。
- 原型连续运行 8 小时无 HWND 泄漏和持续 CPU 活动。

若该门失败，当前规格下不得发布，必须停止主实现并由用户重新确认是否接受降级目标；不得静默把普通置底窗口当成等价实现。

## 5. 用户界面与行为

### 5.1 通知区域

应用是单进程、单实例的通知区域程序。通知区域菜单至少包含：

- 新建组件。
- 每个组件的显示/隐藏、暂停/继续、立即换图、设置和删除。
- 查看错误记录。
- 打开全局设置。
- 退出。

第二次启动程序时，不创建第二个进程；它通过命名互斥量和本地 IPC 通知现有进程打开设置窗口。

应用注册 `TaskbarCreated` 消息；Explorer 或任务栏重建后，必须重新添加通知区域图标并触发桌面宿主健康检查。

### 5.2 组件窗口

- 无标题栏、无边框、不显示在任务栏或 Alt+Tab。
- 不设 Topmost，不激活前台应用。
- 左键在组件任意可见区域按下并移动即可拖动。
- 组件接收鼠标操作，因此其覆盖区域不能操作下方的桌面图标。
- 鼠标拖动只改变位置，不改变尺寸。
- 图片使用等比 `cover` 行为：填满组件、居中、裁剪溢出部分。
- 文件切换使用已解码帧的即时原子替换，不保留旧帧做交叉淡化。
- 暂停只停止换图计时；当前 GIF 继续循环播放。
- 隐藏组件时停止 GIF 帧调度并释放其预取项。

### 5.3 设置

每个组件设置项：

- 名称。
- 根文件夹。
- 宽度和高度。
- 切换间隔。
- 暂停状态。
- 显示/隐藏状态。

技术默认值：

- 默认名称：`图片组件 N`。
- 默认尺寸：480 × 270 DIP。
- 最小尺寸：160 × 90 DIP。
- 设置界面最大值：1920 × 1080 DIP；实际解码仍受物理像素预算限制。
- 默认切换间隔：60 秒。
- 允许切换间隔：5 秒至 24 小时。

修改尺寸后，窗口以中心点为锚调整；若越出当前显示器工作区，则自动夹回可见区域。

### 5.4 多显示器

- 持久化显示器设备标识、DIP 坐标和窗口 DIP 尺寸。
- Win32 边界和拖动运算统一使用物理像素；WPF 设置统一使用 DIP；转换集中在 `DisplayCoordinateService`。
- “断开恢复”定义为安全回主屏：显示器消失时，将组件移动到主显示器工作区内，并持久化新位置。
- 显示器重新出现时不自动跳回旧位置，避免用户无法预测的窗口移动。

## 6. 配置与持久化

### 6.1 配置文件

配置保存在 `%LocalAppData%\DesktopPicture\settings.json`，写入流程为：

1. 序列化到同目录临时文件。
2. Flush。
3. 原子替换正式文件。
4. 保留一个 `settings.json.bak`。

配置模型：

```json
{
  "schemaVersion": 1,
  "startWithWindows": false,
  "widgets": [
    {
      "id": "uuid",
      "name": "图片组件 1",
      "rootPath": "D:\\Pictures",
      "widthDip": 480,
      "heightDip": 270,
      "intervalSeconds": 60,
      "paused": false,
      "visible": true,
      "monitorId": "display-device-id",
      "leftDip": 40,
      "topDip": 40,
      "lastShownCatalogId": 123
    }
  ]
}
```

路径是用户本地隐私数据，不得发送到网络。应用不包含遥测和联网功能。

`lastShownCatalogId` 是随机算法唯一使用的上次成功图片字段。新帧完成视觉提交后更新内存值，并在 250ms 内调度原子配置写入；相邻不重复保证覆盖连续运行和正常退出后的重启，不承诺覆盖提交后 250ms 内的断电或进程崩溃。

### 6.2 索引数据库

索引保存在 `%LocalAppData%\DesktopPicture\catalog.db`。核心表：

```sql
CREATE TABLE roots (
  id INTEGER PRIMARY KEY,
  canonical_path TEXT NOT NULL UNIQUE,
  scan_version INTEGER NOT NULL,
  last_full_scan_utc TEXT,
  health INTEGER NOT NULL
);

CREATE TABLE images (
  id INTEGER PRIMARY KEY,
  root_id INTEGER NOT NULL,
  relative_path TEXT COLLATE WIN_ORDINAL_NOCASE NOT NULL,
  extension INTEGER NOT NULL,
  length INTEGER NOT NULL,
  last_write_utc_ticks INTEGER NOT NULL,
  state INTEGER NOT NULL,
  retry_after_utc_ticks INTEGER,
  seen_scan_version INTEGER NOT NULL,
  UNIQUE(root_id, relative_path),
  FOREIGN KEY(root_id) REFERENCES roots(id) ON DELETE CASCADE
);
```

- 每次连接先执行 `PRAGMA foreign_keys=ON`，再访问 schema。
- 应用在连接初始化时注册 `WIN_ORDINAL_NOCASE` 自定义 collation，以 .NET `StringComparer.OrdinalIgnoreCase` 语义比较规范化相对路径；数据库唯一约束和内存字典使用同一语义。
- 不计算内容哈希，不把整个文件读入索引。
- 同一个规范化根目录由多个组件使用时，共享一个 catalog 和 watcher。
- 内存快照只保存健康候选的紧凑整数 ID；30 万个 32 位 ID 约 1.2 MiB。
- 路径按 ID 从 SQLite 查询，并允许小型热路径缓存；不为每个组件复制全部路径字符串。

## 7. 目录发现与一致性

### 7.1 首次与启动扫描

- 使用流式目录枚举，禁止先通过 `GetFiles` 构建完整字符串数组。
- 首次选择根目录时，枚举器找到第一个候选后立即送解码流水线，不等待全量索引。
- 有历史索引时，先验证并尝试最后成功图片，同时在后台进行增量校验。
- 默认跳过 reparse point，避免目录 junction/symlink 环和跨根目录扫描。
- 无权限目录被跳过并记录汇总，不弹窗打断。
- 扩展名只用于生成候选；文件签名和可解码性在真正解码时验证。

### 7.2 增删监听

每个共享根目录使用一个 `FileSystemWatcher`：

- `IncludeSubdirectories = true`。
- `Filter = "*"`。
- `NotifyFilter = FileName | DirectoryName | LastWrite`。
- 初始 `InternalBufferSize = 32 KiB`，只能通过性能测试调整，不能依靠无限增大缓冲解决可靠性。
- 事件处理器只规范化路径并写入有界队列，不执行文件 I/O、数据库操作或 UI 操作。
- 同一路径的重复事件在 500ms 窗口内合并。
- 正在复制的文件必须通过稳定性检查或共享读成功后才标记健康。

事件有界队列满、事件写入被丢弃、`FileSystemWatcher` 发生 `Error` 或内部缓冲溢出时：

1. 将该根目录标为 `Untrusted`。
2. 保持或重建 watcher，并记录一个单调递增事件序号。
3. 记录扫描起始序号，排队执行带 `scan_version` 的完整 reconciliation；扫描期间事件继续进入有界 journal。
4. 在单个数据库事务中 upsert 本轮已见项，并将未见项标记失效。
5. 回放扫描起始序号之后的 journal 事件；若 journal 再次丢事件，则放弃本轮健康结论并重新扫描。
6. 提交事务后从同一版本构建并原子发布内存快照。
7. reconciliation 完成前继续使用旧快照，但每次解码前重新验证文件元数据。
8. 成功后恢复 `Healthy`。

此外，在应用启动和每 30 分钟空闲窗口执行一次低优先级 reconciliation，以修复静默漏报。扫描不得与同一根目录的另一次完整扫描并发。

### 7.3 一致性目标

- 普通单文件增删：95% 在 2 秒内反映到候选集合。
- watcher 溢出后：在基准 30 万文件语料上，95% 在 60 秒内完成集合修复。
- 修复期间 UI 和已有图片播放继续工作。

## 8. 随机选择

随机保证的作用域是单个组件、相邻两次、规范化路径级别。

算法：

1. 共享快照同时提供紧凑 ID 数组和 ID 到数组下标的紧凑映射。
2. 若 `lastShownCatalogId` 不在快照中，从全部 `n` 个下标均匀抽取。
3. 若 `lastShownCatalogId` 在快照中且 `n >= 2`，从 `[0, n-2]` 均匀抽取一个整数，并跳过上次图片所在下标；结果确定性地不等于上次图片。
4. 读取路径并验证文件仍存在且元数据匹配。
5. 解码失败时，把条目标记为暂时失败并从本次选择集合排除，再继续抽取。
6. 单次切换最多尝试 32 个不同候选；全部失败时保持当前图并显示非打断式错误状态。

特殊情况：

- 0 张健康图片：组件显示“未找到可用图片”占位状态，并等待索引更新。
- 1 张健康图片：允许重复显示，因为“不相邻重复”在数学上无法满足。
- 不要求不同组件之间互斥；两个组件可以同时显示同一路径。

## 9. 解码、缩放与 GIF

### 9.1 解码流水线

全进程使用有界流水线：

```text
WidgetScheduler
  -> RandomSelector
  -> bounded DecodeRequest channel
  -> 2 shared decode workers
  -> frozen/render-ready frame
  -> UI generation check
  -> atomic visual swap
```

- 每个组件拥有一个当前/手动请求槽和一个预取槽；全局总容量为 8。
- 调度优先级为当前/手动请求高于到期切换，高于预取；同优先级在组件之间 round-robin，四个同时到期的组件不得彼此饿死。
- 过期预取使用 latest-wins 丢弃，但不得占用或替换其他组件的保留槽。
- 全局最多两个并发解码 worker，禁止每次切图创建无界 `Task.Run`。
- 每次切换增加组件 `generation` 并取消旧请求。
- 解码结果必须同时匹配 widget ID、generation 和 catalog version，才能提交 UI。
- UI 线程只执行已完成帧的引用交换，不进行文件读取、解码或大图缩放。
- 文件以允许读取期间删除/替换的共享模式短暂打开，解码完成立即关闭。

### 9.2 像素与内存限制

- 输出统一为 32bpp premultiplied BGRA。
- 每个渲染帧最多 2,073,600 物理像素，约等于 1920 × 1080、7.9 MiB。
- 组件物理像素超过上限时，按比例降低解码分辨率，再由 WPF 合成器放大。
- 读取头部后发现源图超过 200,000,000 像素或文件超过 1 GiB 时，按资源保护错误跳过并记录。
- 静态图片只解码至最接近组件实际物理尺寸的层级，不缓存原始全分辨率像素。

进程级缓存预算：

| 项目 | 上限 |
|---|---:|
| 4 个当前静态帧 | 32 MiB |
| 最多 4 个下一帧预取 | 32 MiB |
| 2 个 worker 的输出、转换副本、临时内存和流 | 48 MiB |
| 最多 4 个根、合计 120 万候选的 SQLite、ID 映射和路径热缓存 | 56 MiB |
| WPF、SkiaSharp、窗口、日志和其他已分配预算 | 80 MiB |
| 未分配峰值余量 | 32 MiB |
| 计划峰值 | 280 MiB |
| 硬上限 | 300 MiB |

缓存是按实际字节计费的进程级 LRU；当前显示帧被 pin，预取项可淘汰。图片数量增长不得导致缓存无界增长。

### 9.3 GIF

- 使用统一解码器读取帧、帧延时、合成方式和循环信息。
- 每个组件最多保留当前帧、下一帧和合成所需画布，不缓存完整 GIF 的所有帧。
- GIF 输出同样受 2,073,600 物理像素上限约束。
- 动画渲染最高 30 FPS；更短的源帧延迟通过跳帧追赶时间线，不能用忙循环补帧。
- 组件隐藏或换图时立即停止旧 GIF 计时器、取消解码并释放帧。
- 四个 GIF 同时播放不承诺满足静态场景的 `<1% CPU`，但 UI 响应和有界内存约束仍必须满足。
- GIF 场景单独报告 native/private bytes；若预计超过 384 MiB，必须停止预取并降低动画帧率，禁止无界增长。

## 10. 并发、生命周期与恢复

- `ApplicationLifetime`、每个组件切换、每个根目录扫描分别使用独立 cancellation token。
- 索引写入通过单写者队列批处理；每 500 项或 250ms 提交一次，以先到者为准。
- 组件状态使用不可变快照发布；锁内禁止文件 I/O、解码和数据库查询。
- 关闭顺序：停止计时器 → 停止接收 watcher 事件 → 取消扫描 → 完成 channels → 最多等待 5 秒排空并提交 index writer；超时则回滚未完成事务 → 等待解码 worker → dispose watcher/图像/托盘 → flush 设置。
- Explorer 重启不重启整个应用；桌面宿主服务负责重附着全部可见组件。
- 设置文件损坏时先尝试备份；仍失败则以默认配置启动，并保留损坏文件供诊断。
- 数据库损坏时移走损坏数据库并后台重建，不阻止托盘启动。

## 11. 错误处理与日志

用户可恢复的图片错误全部静默跳过并记录，不弹出模态对话框：

- 文件不存在或被删除。
- 无权限。
- 文件仍在写入或被独占。
- 扩展名与内容不符。
- 解码失败。
- 超过资源上限。

失败条目采用退避：第一次 10 分钟、第二次 1 小时、之后 24 小时；文件大小或修改时间改变后立即清除退避。

日志写到 `%LocalAppData%\DesktopPicture\logs`：

- 结构化文本，不记录图片内容。
- 单文件最大 2 MiB，最多保留 5 个。
- 同一路径的重复错误聚合计数，防止日志风暴。
- 设置界面显示最近错误摘要、根目录健康状态、索引进度和桌面宿主状态。

为保证暖启动首图，应用为每个组件保存一张本地 startup preview，最多每 30 分钟更新一次，并受每张 8 MiB、总计 32 MiB 的磁盘上限约束。preview 只保存在 `%LocalAppData%\DesktopPicture\cache\startup`，删除组件或卸载时删除。

## 12. 性能合同

### 12.1 基准环境

除特别说明，性能指标在以下环境验证：

- Windows 11 x64，Release self-contained 构建。
- 4 核或以上现代 x64 CPU，16 GiB RAM。
- 本地 NTFS SSD。
- 30 万张候选图片；JPG/PNG/WebP 混合，源图不超过 24MP。
- 四个 800 × 600 DIP 静态组件，100%/150% DPI 各测一次。
- 不包含网络盘、正在进行的全量扫描、动画 GIF 和第三方实时杀毒扫描造成的不可控延迟。

### 12.2 硬指标

| 指标 | 要求 | 测量方式 |
|---|---:|---|
| 暖启动首图 | `p95 <= 2s` | 有历史组件时，进程创建至 startup preview 或已验证源图的首个像素提交，100 次 |
| 冷索引首图 | `p95 <= 2s` | 在基准语料上，从选择根目录确认至首个健康图片像素提交；同时完整报告 max |
| 定时静态切图 | `p95 <= 200ms`、`p99 <= 500ms` | 四组件同步到期也包含在至少 1000 次端到端样本中 |
| 下一帧预取命中率 | `>=99%` | 定时切换样本；手动连续换图单独报告 |
| UI 操作响应 | `p95 <= 100ms` | 托盘/设置输入至 UI 反馈 |
| 空闲 CPU | 平均 `<1%` | 无扫描、无 GIF、5 分钟；进程 CPU 时间除以墙钟和逻辑处理器数 |
| 四组件私有内存 | 峰值 `<=300 MiB` | current+next 充满后读取 Private Bytes |
| 内存增长 | 2 小时后无持续正斜率 | 30 万目录 soak test |
| Explorer 恢复 | `<=5s` | Explorer 重启至全部组件重新依附 |

定时切换的端到端合同包含预取未命中；`>=99%` 的命中率是满足合同的设计手段，不是排除失败样本的理由。冷解码和手动快速连点单独记录 p50/p95/p99；任意超大或损坏文件不纳入静态健康语料。

### 12.3 可观测性

使用 `System.Diagnostics.Metrics` 暴露开发期指标：

- `startup.first_image_ms`
- `switch.latency_ms`
- `decode.duration_ms`
- `decode.failures`
- `decode.cancelled`
- `cache.bytes`
- `cache.hit`
- `cache.miss`
- `catalog.entries`
- `catalog.scan_duration_ms`
- `watch.events`
- `watch.overflows`
- `watch.reconcile_duration_ms`
- `queue.depth`
- `gif.frames_decoded`
- `desktop_host.reattach_ms`

正式发布默认不对外导出这些指标；诊断模式允许 `dotnet-counters` 或本地诊断页读取。

## 13. 验收与测试

### 13.1 单元测试

- 路径规范化和大小写语义。
- 0、1、2、30 万候选下的相邻不重复规则。
- 设置迁移、原子保存和备份恢复。
- cover 裁剪矩形在不同 DPI 和比例下正确。
- generation/cancellation 阻止过期解码覆盖新图片。
- 缓存按字节上限淘汰并保留当前帧。
- 失败退避和文件变更后恢复。

### 13.2 集成测试

- 递归目录含长路径、无权限目录、损坏图片、伪扩展名、超大图和正在复制的文件。
- 真实 create/delete/rename 风暴与 watcher overflow；最终索引必须与磁盘重新枚举结果一致。
- 数据库和设置文件被截断后的恢复。
- JPG、PNG、WebP、透明图片和不同 GIF disposal method 的视觉基准。
- 同一根目录被多个组件共享时只创建一个 catalog/watcher。
- 退出过程中仍有扫描、解码和 GIF 帧任务时无死锁、无残留进程。

### 13.3 手工兼容性测试

- Windows 11 和受支持 Windows 10 测试矩阵。
- 100%、125%、150%、200% DPI。
- 单显示器、双显示器、负坐标和拔插显示器。
- Explorer 重启、显示桌面、锁屏、睡眠、RDP 和软件渲染。
- Explorer/任务栏重建后，通知区域图标在 5 秒内恢复、可点击且菜单状态保持。
- 普通应用的激活、全屏和 Always-on-top 不被组件遮挡。
- 多显示器断开时安全回主屏；重连不跳回，并在负坐标与 DPI 变化下保持在 2 个物理像素容差内。
- 在干净 Windows 10/11 x64 虚拟机验证 MSI 安装、SkiaSharp 原生解码、带空格安装路径、自启动、升级、修复和卸载清理。
- 登录自启动的启用、禁用、任务管理器禁用、启动失败日志和升级后 Run 值重写。

### 13.4 性能测试

- 冷索引、暖索引、冷 OS 文件缓存分别报告。
- 30 万文件和 4 个组件至少切换 1000 次，报告 p50/p95/p99/max 和预取命中率。
- 记录 Private Bytes、Working Set、GC heap、句柄数、GDI 对象和 USER 对象。
- GIF 语料包含零延迟帧、长动画、透明合成和大画布。
- 发布前至少采集一次 WPR/WPA trace，确认 UI 线程无扫描、同步 I/O 或大图解码。

## 14. 交付阶段与停止条件

### 阶段 A：桌面宿主原型

只实现通知区域、一个色块窗口、拖动、桌面依附、Explorer 恢复和多显示器。通过第 4.3 节后才进入阶段 B。

### 阶段 B：单组件闭环

实现设置、单根目录流式枚举、静态图片解码、随机切换、持久化和错误跳过。

### 阶段 C：大型目录与性能

实现 SQLite catalog、共享 ID 快照、watcher/reconciliation、有界预取、指标和 30 万语料测试。

### 阶段 D：多组件与 GIF

扩展到四组件、动态 GIF、多显示器恢复、资源预算和完整 soak test。

### 阶段 E：发布

完成 per-user installer、自启动、签名、卸载清理和发布验收。

任一阶段出现以下情况必须停止扩展功能并先解决：

- 桌面宿主在支持矩阵上不可恢复地失效。
- UI 线程出现目录扫描或图片解码。
- 图片数量导致内存线性增长。
- 四静态组件超过 300 MiB 且无法通过目标尺寸解码和有界缓存修正。
- watcher overflow 后索引无法通过完整 reconciliation 恢复一致。

## 15. 已接受风险与后续事项

- Explorer 桌面宿主依赖未公开的 Shell 行为，是首要兼容性风险；以 Phase 0 技术门控制。
- Windows 10 普通消费版本已脱离上游支持；兼容运行不等于官方支持。
- GIF 的 CPU 消耗取决于帧率、画布和压缩复杂度，不纳入静态场景 `<1% CPU` 指标，但必须满足 UI 响应和内存上限。
- 300 MiB 约束要求输出帧不超过约 1080p；超大组件会用较低分辨率渲染，优先保证性能和稳定性。
- 文件系统通知不是可靠日志；完整 reconciliation 是最终一致性的来源。

## 16. 参考资料

- [.NET 官方支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Windows 上的 .NET 支持版本](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- [WPF 概览](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [WPF 窗口概览](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/)
- [SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)
- [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)
- [Win32 扩展窗口样式](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [Windows 通知区域](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area)
- [IDesktopWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper)
- [Windows Desktop Gadgets 已移除](https://learn.microsoft.com/en-us/windows/compatibility/desktop-gadgets-removed)
- [WPF Imaging Overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/imaging-overview)
- [WIC 原生编解码器](https://learn.microsoft.com/en-us/windows/win32/wic/native-wic-codecs)
- [SkiaSharp 官方仓库](https://github.com/mono/SkiaSharp)
- [Microsoft.Data.Sqlite 概览](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)
- [Directory.EnumerateFiles](https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.enumeratefiles?view=net-10.0)
- [FileSystemWatcher](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher?view=net-10.0)
- [有界 Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
- [System.Diagnostics.Metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation)
- [.NET self-contained 与单文件发布](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Run 与 RunOnce 注册表项](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)
