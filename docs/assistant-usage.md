# AutoAssembly 右侧装配助手 1.3.1

1.3.1 支持直接点击图形区零件的面、边或顶点，识别所属顶层模组，并对该模组替换、移动、旋转。助手可见且处于总装普通选择状态时，点击面、边或顶点约 0.7 秒后自动选中整个顶层模组；配合等属性管理器打开或正在编辑零件/子装配体时不接管选择。保留语言位姿调整、单步撤销和导出功能。

## 移动与旋转

在图形区点击模组内零件的面、边或顶点（也可以在树中选择组件），在助手中每次发送一条，例如：

```text
沿 X 轴移动 100 毫米
沿 Y 轴移动 -20 毫米
绕 Z 轴旋转 90 度
绕自身 X 轴旋转 -90 度
```

每条指令先生成计划，核对实例、方向和数值后点击“执行调整”，或发送“执行调整”。需要恢复刚才一次调整时，发送“撤销上次调整”，核对后再执行；不是多步撤销。

默认使用总装坐标轴；“自身/局部/本地”使用模组自身坐标轴。旋转中心是**模组自身原点**，不是几何中心，也不是总装原点；正角度遵循右手定则。移动支持毫米/mm、厘米/cm、米/m，旋转支持度/deg/°。负数表示反方向。当前不支持任意选定旋转中心、屏幕左右方向或多条混合指令。

调整只作用于当前总装配置，保留原固定/浮动状态。选择或位姿改变会使待执行计划失效。插件保留现有配合；若无法精确到位或联动其他顶层模组，会尝试恢复并反馈校验结果。不会自动解除配合、自动保存或执行干涉检查。复杂柔性子装配体仍需工程验收。

## 安装与打开

1. 保存工作，完全退出 SolidWorks（插件 DLL 不能在运行期间可靠热更新）。安装器会检查并拒绝在 SW 运行时安装，不会强行关闭 SW。
2. 将整个 `release-assistant-1.3.1` 目录放在固定位置，双击其中的 `install.bat`，允许管理员注册。
3. 重新启动 SW，在“工具 → 插件”中启用 **Auto Assembly Pose**（沿用旧插件名称和 GUID）。
4. 右侧出现 AutoAssembly 对话图标。也可通过 **AutoAssembly → 装配助手** 工具栏按钮或“工具 → Auto Assembly → 装配助手”打开。
5. 如果右侧任务窗格收起，请展开并固定任务窗格，建议宽度至少 280 px。

新版安装会把原插件注册位置改为这个目录；不需要同时加载两个版本。需要回到旧版时，退出 SW 后运行原 `release/install.bat`。保留两个文件夹的位置。

## 已接入的操作

| 输入/快捷按钮 | 实际行为 |
| --- | --- |
| 查看选中模组 | 读取当前 SW 装配、选择实例、引用配置、文件路径 |
| 替换该模组 | 捕获选中实例，选择本地 SLDASM，展示替换计划和候选配置 |
| 执行替换（计划按钮） | 校验目标，调用 SW 替换单个实例，尝试重接配合并重建 |
| 导出位姿 | 复用原位姿导出服务，将路径反馈到对话记录 |
| 重建当前装配 | 对当前装配执行重建，不创建新装配、不自动保存 |
| 缩放至全图 | 在 SW 中缩放至整个装配体 |
| 取消 / 取消替换 | 丢弃尚未执行的替换计划 |
| 帮助 | 显示支持的指令 |

Enter 发送，Shift+Enter 换行。当前为明确指令识别模式，尚未接大模型或自动模组推荐；未知句子、多条混合指令和否定句不会触发替换。

## 第一次替换

在测试总装图形区点击模组内零件的面，确认助手显示的**所属顶层模组实例** → 输入“替换该模组” → 选择另一个 `.SLDASM` → 查看原实例和目标路径 → 选择目标配置 → 点击“执行替换”。

图形区选中的面、边、顶点和内部组件统一向上识别为总装下一层的子装配体模组。多层嵌套时取最外层模组，不按名称猜测；助手显示点击来源和实际目标，请执行前核对。轻化模组在发出操作指令时自动解析，并保留目标选择；真正被抑制的模组需先解除抑制，虚拟模组需先另存为外部 SLDASM。不处理多选和总装直属零件。源/目标同文件名的情况受 SW 替换接口限制，会直接提示。

选择摘要每约 0.7 秒在 SW UI 线程刷新。切换文档、配置、所属模组实例或清空选择后，待执行计划失效；执行时还会重新校验。只替换单个实例，不替换所有同源实例。

替换前请处理其他已打开模型的未保存修改，因为 SW 替换可能关闭组件文档。插件不会静默保存这些文档。替换后反馈文件/配置、其他实例校验、重建、顶层配合特征诊断和原点位移，**不自动保存装配体**。

“重建通过”和“未报告配合错误”不代表完成了安装兼容或干涉检查。形状和接口不同的模组仍可能需要配合映射。异常时可能已有部分修改，程序不会声称已回滚；请检查 SW 当前状态。

## 开发与验证

- `src/AutoAssemblyAddin/UI/AssistantPane.cs`：窗口布局与交互事件。
- `Services/AssistantController.cs`：指令、选择刷新、替换计划生命周期。
- `Services/SelectionContextService.cs`：选择与持久实例引用。
- `Services/ModuleReplacementService.cs`：实际 SW 替换与结果检查。
- `Services/AssistantCommandRouter.cs`：可扩展指令入口。
- `scripts/package-assistant.ps1`：构建并生成独立安装包，可指定版本目录，避免覆盖当前加载的 DLL。

编译依赖优先使用默认 SW 安装目录，不存在时使用项目 `release` 中自带的 interop DLL；也可传 `-p:SolidWorksInteropDir=实际目录`。

```powershell
dotnet build tests/AutoAssemblyAddin.Tests/AutoAssemblyAddin.Tests.csproj -c Release -p:Platform=x64
./tests/AutoAssemblyAddin.Tests/bin/x64/Release/net48/AutoAssemblyAddin.Tests.exe artifacts/ui-preview
./tests/AutoAssemblyAddin.Tests/bin/x64/Release/net48/AutoAssemblyAddin.Tests.exe --live-inspect
./scripts/package-assistant.ps1 -PackageDirectoryName release-assistant-1.3.1
```

前两项检查指令分流、窗口布局、配置传递和忙碌状态；`--live-inspect` 只读连接正在运行的 SW，不修改装配。COM 连接要求与 SW 在可互访的 Windows 会话/权限环境中运行。

最终实机验收：加载/卸载窗格、窗口缩放和输入、切换/关闭文档、同源双实例只替换一个、已有配合的替换、错误候选/失效计划。自动测试和控件渲染不能代替这些 SW 场景。

日志：`%LOCALAPPDATA%\AutoAssemblyAddin\addin.log`。安装失败原因：安装目录的 `install-error.txt`。

独立 SW 集成测试（另启测试进程，只生成和操作临时样例）：

```powershell
./tests/AutoAssemblyAddin.Tests/bin/x64/Release/net48/AutoAssemblyAddin.Tests.exe --replacement-integration artifacts/replacement-integration
```

已验证无配合测试装配的真实替换、目标配置、同源实例隔离和改选对象拦截。实际安装配合、交互式“编辑子装配体”状态仍需人工验收。



