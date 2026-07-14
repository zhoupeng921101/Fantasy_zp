# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 本仓库是什么

这是 **Fantasy** 框架的源码仓库 —— 一个零反射、高性能的 C# 分布式游戏服务器框架（基于 ECS、对 Native AOT 友好），同时包含其配套工具、Unity 客户端包、示例和文档。它**不是**一个游戏项目，而是用于构建游戏的框架本身。包版本号规则为 `YYYY.0.NNNN`（例如 `2026.0.1022`）。

大部分文档和代码注释为**中文**。编辑注释/文档时请与周围语言保持一致。

> **使用 `fantasy-net` skill。** `Skills/fantasy-net/SKILL.md` 是编写或审查 Fantasy 代码（ECS、协议、Handler、Roaming/Address/SphereEvent、配置、数据库、HTTP、Unity）的权威指南。它包含一张庞大的参考导航表 —— 处理任何非平凡的 Fantasy 任务时，应先读 `SKILL.md`，再顺着它跳转到具体的 `references/*.md` 文件，而不是凭第一性原理推断。

## 仓库结构

- `Fantasy.Packages/` —— 所有发布的包：
  - `Fantasy.Net/` —— 核心运行时（`Runtime/Core/`：Entitas、Scene、Network、FTask、DataBase、Serialize、Pool、IdFactory 等）。构建出 `Fantasy-Net` NuGet 包。
  - `Fantasy.SourceGenerator/` —— Roslyn 源生成器 + 分析器；编译期注册（见下文）。
  - `Fantasy.Cli/` —— `fantasy` dotnet 工具（项目脚手架：`fantasy init`、`fantasy add`）。
  - `Fantasy.ProtocolExportTool/` —— 把 `.proto` 文件转换为生成的 C# 代码的命令行工具。
  - `Fantasy.ProtocolEditor/` —— 编辑协议的桌面 GUI。
  - `Fantasy.Unity/` —— Unity 客户端包（`Runtime/`、`Editor/`、`RoslynAnalyzers/`）。
  - `Fantasy.NLog/` —— NLog 日志适配器。
- `examples/` —— 参考项目：`Server/APP`（完整的三层服务器）、`Console`（最小客户端）、`Client/Unity`（Unity 客户端）、`Config`（proto 及导出的配置）。
- `Tools/` —— `ProtocolExportTool/`（已编译，通过 `Run.bat` 运行）、`Update-Unity-Source-Generator/`、`NetworkProtocol/`（RouteType/RoamingType/OpCode 配置）、`Package-CLI-Resources/`。
- `Docs/` —— 完整的框架文档（从 `Docs/README.md` 开始）。
- `Fantasy.Benchmark/` —— 网络基准测试控制台程序。
- `Fantasy.sln` —— **只包含各个包**的解决方案（Fantasy.Net、Fantasy.Cli、Fantasy.SourceGenerator、ProtocolEditor、ProtocolExportTool）。示例不在此解决方案中。

## 常用命令

```bash
# 构建框架 + 工具
dotnet build Fantasy.sln

# 构建并运行示例服务器（--m <RuntimeMode> 参数是必需的；缺少它会报 "Command line format error!"）
dotnet run --project examples/Server/APP/Main/Main.csproj -- --m Develop

# 运行基准测试
dotnet run -c Release --project Fantasy.Benchmark/Fantasy.Benchmark.csproj

# 打包 NuGet 包（输出到 ./nupkg/）
dotnet pack Fantasy.Packages/Fantasy.Net/Fantasy.Net.csproj -c Release

# 导出网络协议（ExporterSettings.json 已入库为相对路径配置，锚定工具目录=进程 cwd，
# 在任意 Fantasy worktree 内导出即直写该 worktree 的双端；改导出目标才需编辑它）：
Tools/ProtocolExportTool/Run.bat        # Windows（自切入工具目录）
# 或：  cd Tools/ProtocolExportTool && dotnet Fantasy.ProtocolExportTool.dll export --silent
# 勿在别的目录直跑 DLL：相对配置按 cwd 解析，找不到 ExporterSettings.json 会直接失败

# 重新构建 Unity 兼容的源生成器并复制进 Unity 包
# （以 "Unity" 配置构建 SG = Roslyn 4.3.0，复制到 Fantasy.Unity/RoslynAnalyzers）
Tools/Update-Unity-Source-Generator/update-unity-source-generator.bat
```

本仓库**没有单元测试项目**。验证手段是：`dotnet build` 干净通过（核心项目在 Debug 下启用了 `TreatWarningsAsErrors`）、源生成器产出预期的注册代码、示例服务器/控制台能正常运行。请针对每个任务定义可验证的成功标准（例如“协议导出重新生成了 `.g.cs`”、“构建无错误”、“Log.Debug 确认消息往返成功”）。

## 关键构建要求（不显眼，缺失时会静默失败）

任何使用 Fantasy 类型的项目都必须：
1. 定义 **`FANTASY_NET`** 编译常量（`<DefineConstants>TRACE;FANTASY_NET</DefineConstants>`）。缺少它源生成器不会产出任何注册代码，框架会在运行时失败。（Unity 使用它自己的宏定义。）
2. 设置 **`AllowUnsafeBlocks=true`** —— 运行时为了性能使用了 `unsafe`。
3. 将 **`Fantasy.SourceGenerator`** 作为分析器引用：
   ```xml
   <ProjectReference Include="...\Fantasy.SourceGenerator.csproj"
                     OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
   ```
4. 将 `Fantasy.config` 注册为 **`AdditionalFiles`** 条目*并*复制到输出目录：
   ```xml
   <AdditionalFiles Include="Fantasy.config" />          <!-- 让 SG 能在编译期读取它 -->
   <None Update="Fantasy.config"><CopyToOutputDirectory>Always</CopyToOutputDirectory></None>
   ```
   SG 会读取 `Fantasy.config` 来生成 `SceneType` 常量、数据库名常量等。`Fantasy.config` 必须放在*直接*引用 Fantasy 的那个项目的根目录下。

目标框架：核心为 `net8.0;net9.0;net10.0`；示例/大多数项目为 `net8.0`。

## 架构：全局视角

### 三层服务器项目结构
一个 Fantasy 服务器（见 `examples/Server/APP/`）被拆为三个项目，且只有一个直接引用 Fantasy：

- **Entity**（`Entity.csproj`）—— 只放数据：`Entity`/`Component` 定义、`Fantasy.config`，以及 `Generate/` 文件夹（导出的协议、`SceneType`）。**这是唯一直接引用 `Fantasy.Net` + 源生成器的项目。**
- **Hotfix**（`Hotfix.csproj`）—— 逻辑：消息 Handler 和 ECS System。引用 Entity；设计为可热重载。同样引用源生成器。
- **Main**（`Main.csproj`）—— 可执行入口点。引用 Entity + Hotfix。

引用链：`Main → {Entity, Hotfix}`、`Hotfix → Entity`、`Entity → Fantasy`。数据/逻辑分离正是热重载能成立的原因 —— **保持 Entity（数据）与 Hotfix（逻辑）分离；不要把 Handler/System 逻辑放进 Entity。**

### 启动流程
`Main/Program.cs` 只做三件事：
1. `AssemblyHelper.Initialize()` —— 强制加载被引用的程序集，使其 `ModuleInitializer`（由 SG 生成）得以运行。.NET 是延迟加载程序集的，所以这一步对触发注册是必需的（在 Native AOT 下是空操作）。
2. 构造一个 logger（`new Fantasy.NLog("Server")`，或传 null 使用控制台日志）。
3. `await Fantasy.Platform.Net.Entry.Start(logger)`。

运行模式来自 `--m` 命令行参数（`Develop`/`Release`/...）。开发时在 `Properties/launchSettings.json` 里设置（`"commandLineArgs": "--m Develop"`）。

### 源生成器 = 零反射（不要与之对抗）
所有注册 —— 消息 Handler、ECS System、场景类型、协议 OpCode、数据库名 —— 都在**编译期**由 `Fantasy.SourceGenerator` 生成。这是框架的核心设计：没有运行时反射扫描，对 AOT 友好。

- **绝不手改生成的代码**（`*.g.cs`、`obj/.../generated/` 下的文件，或 `Generate/` 里被注释的产物）。要改变生成结果，应修改源头（entity/handler/proto/config）再重新构建。
- **绝不手动注册** Handler/System —— 定义类本身就足够了。

### ECS / Scene
- Entity、Component、Handler 都是 `sealed class`。非 struct 对象通过 Entity/Scene 工厂创建（带对象池），而不是 `new`。
- **Scene** 是所有 Entity 的容器与生命周期边界 —— 销毁一个 Scene 会级联销毁其子级。通过 `self.Scene` 访问框架组件（如 `TimerComponent`、`EventComponent`、网络/消息相关组件）。
- 场景类型来自 `Fantasy.config` 的 `sceneTypeString`（如 `Gate`、`Map`、`Chat`、`Addressable`、`HttpGift`）→ SG 生成 `SceneType` 常量。Scene 运行在调度器上（`MultiThread`/`ThreadPool`/`Main`），通过每个场景的 `sceneRuntimeMode` 设置。
- 生命周期 System：`AwakeSystem`、`UpdateSystem`、`DestroySystem`、`DeserializeSystem`，以及 `TransferOutSystem`/`TransferInSystem`（仅用于跨服传送）。`SubScene`（`Scene.CreateSubScene()`）提供轻量的运行时子场景，用于副本/房间。

### 网络模型
- **Outer** 协议 = 客户端↔服务器（`IMessage`、`IRequest`/`IResponse`）。**Inner** 协议 = 服务器↔服务器（`IAddressMessage`、`IAddressRequest`/`IAddressResponse`）。`.proto` 文件按 `NetworkProtocol/Outer` 与 `/Inner` 分目录存放。
- 按消息类型划分的 Handler 基类：`Message<Session,T>` / `MessageRPC<Session,TReq,TRes>`（outer）、`Address<Scene,T>`（inner address 消息）、`Roaming<Session,TReq,TRes>`（roaming）。源生成器在编译期把消息路由到 Handler；协议导出工具还会生成 `session.C2X_...()` 扩展辅助方法。
- **Address** 消息：通过 `Entity.Address`（RuntimeId）实现服务器间的实体直达消息。
- **Roaming**：分布式实体路由 —— Gate 透明地把强类型消息转发到拥有该实体的后端服务器；`Terminus` 有自己的生命周期（`OnCreateTerminus`/`OnDisposeTerminus`、`StartTransfer`）。`RoamingType.Config`/`RouteType.Config` 在 `Tools/NetworkProtocol/` 下。
- **SphereEvent**：跨服域的发布/订阅（`SphereEventComponent`）。
- 协议：TCP、KCP（低延迟 UDP）、WebSocket（H5/WebGL）、HTTP。通过每个场景的 `networkProtocol` 配置。

### 配置（`Fantasy.config`）
XML（schema 为 `Fantasy.xsd`）描述拓扑：`<machines>`（IP）→ `<processes>`（位于哪台机器、启动组）→ `<worlds>`（游戏世界 + `<database>` MongoDB 配置）→ `<scenes>`（核心单元：场景类型、world、协议、面向客户端的 `outerPort` / 面向服务器间的 `innerPort`）。`examples/Server/APP/Entity/Fantasy.config` 是一个真实的多 world/多 scene 示例。

### 异步、序列化、数据库
- 所有异步**用 `FTask`，绝不用 `Task`** —— 它是框架的零 GC awaitable。
- 序列化：MemoryPack + MongoDB.Bson（以及基于 proto 的网络协议）。
- 数据库：MongoDB，运行时通过 `scene.World.Database` 访问；连接在 `Fantasy.config` 的 `<world><database>` 中配置。`[SeparateTable]` 用于拆分较大的聚合子数据。

## 约定（来自 fantasy-net skill —— 请遵循）

- 所有异步用 `FTask`，不用 `Task`。
- 文件作用域命名空间；框架命名空间为 `Fantasy`（`namespace Fantasy;`）。
- Entity、Component、Handler 用 `sealed class`。
- 用 `Log.Debug/Info/Error` 记录日志。业务逻辑通过 `response.ErrorCode` 返回错误，而非抛异常。
- 优先用 Event 系统解耦（Struct 事件零 GC；复杂载荷用 Entity 事件；`EventSystem` 同步 / `AsyncEventSystem` 异步）。等待结果的流程用 `EventAwaiter`。
- 外科手术式改动：不要重构或“改善”无关/生成的代码；每一行改动都应能追溯到需求本身。

## 备注

- `CLAUDE.md` 和 `.claude/` 在 `.gitignore` 中 —— 本文件是本地指引，不会被提交。
- `fantasy` CLI（`Fantasy.Cli`）用于脚手架*新*项目（`fantasy init`、`fantasy add -t <component>`）；注意 `fantasy add` 只能在全新/空目录下使用，不能用于已有项目。
