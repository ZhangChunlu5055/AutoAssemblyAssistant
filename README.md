# AutoAssembly 装配助手 1.3.1

SolidWorks 右侧任务窗格插件，使用 C#、WinForms、.NET Framework 4.8 和 SolidWorks COM API。

## 功能

- 点击零件的面、边或顶点后，选中其所属顶层子装配体模组。
- 将选中的单个模组实例替换为其他 SLDASM，并检查配置、其他实例及位姿。
- 使用中文指令沿总装/自身坐标轴平移和旋转，支持计划确认及单步撤销。
- 处理轻化模组、导出位姿、从位姿 JSON 重建装配、重建及缩放。

不包含 1.4.0 的 Prompt/Excel 完整设备生成入口。不包含工程模型或 Excel 资料。

## 版本来源

本目录是 **1.3.1 功能源码整理版**。原工程没有对应版本的 Git 历史快照，因此依据开发记录，从后续源码中剥离了 1.4.0 新增功能，恢复了 1.3.1 的版本号、界面和测试；使用说明取自原 1.3.1 发布 ZIP。

整理时增加了跨机器依赖配置、仓库说明和忽略规则。此包不是原始历史提交的逐字节存档；也不承诺重编译 DLL 与原发布 DLL 的 SHA-256 相同。现成安装包应另放 GitHub Releases，不要混入源码树。

## 开发环境

- Windows x64；本机安装 SolidWorks（已有实机验证环境为 SolidWorks 2024）。
- 支持构建 SDK 风格 .NET Framework 项目的 .NET SDK，或 Visual Studio 2022 的“.NET 桌面开发”环境。
- 首次还原需要 NuGet 包 `Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3。
- SolidWorks API 的三个 interop DLL：`SolidWorks.Interop.sldworks.dll`、`SolidWorks.Interop.swconst.dll`、`SolidWorks.Interop.swpublished.dll`。

`Directory.Build.props` 会尝试仓库 `lib/` 和 C/D 盘的标准 SolidWorks 安装目录 `api/redist`。如果安装位置不同，使用下面命令中的 `SolidWorksInteropDir`。第三方 DLL 不包含在源码包中。

## 编译与测试

在源码根目录打开 PowerShell：

```powershell
dotnet build AutoAssemblyAddin.sln -c Release -p:Platform=x64
```

自定义依赖路径示例（替换为实际目录）：

```powershell
dotnet build AutoAssemblyAddin.sln -c Release -p:Platform=x64 '-p:SolidWorksInteropDir=E:\SOLIDWORKS\api\redist'
```

运行不修改 SolidWorks 文档的指令、矩阵和控件测试：

```powershell
.\tests\AutoAssemblyAddin.Tests\bin\x64\Release\net48\AutoAssemblyAddin.Tests.exe artifacts\ui-preview
```

可选的真实 SW 集成测试会启动独立 SW 实例并自行创建样例，结束后退出测试实例。普通测试不需要执行它；不要将其换成工作装配运行：

```powershell
.\tests\AutoAssemblyAddin.Tests\bin\x64\Release\net48\AutoAssemblyAddin.Tests.exe --motion-integration artifacts\motion-integration
```

集成测试需要本机有效的零件/装配体模板；默认模板不可用时，应根据安装位置调整测试中的模板回退路径。不同 SW 版本和复杂工程配合仍需实机验收。

## 生成安装包

```powershell
.\scripts\package-assistant.ps1 -SkipBuild
```

或同时编译并指定依赖路径：

```powershell
.\scripts\package-assistant.ps1 -SolidWorksInteropDir 'E:\SOLIDWORKS\api\redist'
```

安装包位于 `release-assistant-1.3.1/`。保存工作并退出所有 SolidWorks 窗口后，在此安装包目录运行 `install.bat`。重启 SW，在“工具 → 插件”中启用 **Auto Assembly Pose**。保留安装目录的位置；安装器不会强行关闭 SW。

## 使用

详细步骤见 [使用说明](docs/assistant-usage.md)。例如：

```text
替换该模组
沿 X 轴移动 20 毫米
绕自身 Z 轴旋转 -90 度
执行调整
撤销上次调整
```

当前采用明确指令识别，并未接入大模型。操作前核对目标和执行计划。旋转中心为模组自身原点；模组按总装下一层子装配体识别。替换及位姿调整不自动保存，配合约束可能使操作失败。不会把重建成功视为干涉或机械安装兼容性验收。

## 目录

- `src/AutoAssemblyAddin/`：插件、侧栏 UI 和业务服务。
- `tests/AutoAssemblyAddin.Tests/`：控制台自动检查及隔离 SW 集成测试。
- `scripts/`：打包和安装脚本。
- `docs/`：使用与整理验证说明。
- `lib/`：可选本机 interop 依赖位置（DLL 被 git 忽略）。

## 上传到 GitHub

将本目录内的文件和文件夹上传到仓库根目录，包括 `.gitignore` 和 `Directory.Build.props`。不要上传上一级开发工程、`bin/`、`obj/`、模型、日志或 `artifacts/`。网页上传不会自动遵守 `.gitignore`，请使用这个已经整理好的目录。

本包没有替作者添加开源许可证；对外公开前由项目所有者确定授权方式。给师兄协作可先使用私有仓库。
