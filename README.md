# Unity QA Client

本仓库包含 QA Test Framework 的 Unity 客户端脚本。QA 功能启用后，客户端会在 Play Mode 或 Player 启动时连接 QA register server，扫描当前 AppDomain 中带 `[QaTest]` 的方法并注册到服务端，随后可以接收 Web 控制台或 MCP 工具发来的执行命令。

## 目录结构

- `Runtime/QaTest/`: 客户端核心代码，包括 WebSocket 连接、方法扫描、参数转换、执行结果回传和协程返回值封装。
- `Samples~/Example/`: 示例测试方法，覆盖连通性检查、日志输出、面板状态和控件交互等 mock 场景。

## 接入方式

通过 Package Manager 的 Git URL 引入本仓库，或把本目录作为本地 package 添加到 Unity 工程。运行时代码位于 `Runtime/`，并通过 `QaTestFramework.UnityClient` 程序集定义编译。`QaTestBootstrap` 会在 QA 功能启用时于场景加载后自动创建 `[QaTestClient]` 对象，并通过 `DontDestroyOnLoad` 保持连接。

在 `Packages/manifest.json` 中直接引用 Git 仓库：

```json
{
  "dependencies": {
    "com.qatestframework.unityclient": "https://192.168.9.98:8010/game_automation/mcp-host.git"
  }
}
```

如果已经安装过旧结构的包，删除 `Packages/packages-lock.json` 中对应条目，或在 Package Manager 中 remove 后重新 add，让 Unity 拉取新的 commit。

默认连接地址：

```text
ws://localhost:3000/ws?role=unity
```

如果需要覆盖服务地址，可以使用命令行参数：

```powershell
Unity.exe -projectPath <projectPath> --qa-server-url ws://localhost:3000/ws?role=unity
```

也可以在场景中预置 `QaTestClient` 组件，并通过 Inspector 配置 `serverIP` 和 `serverPort`。客户端固定使用 `role=unity`。

## 启用开关

QA 功能默认策略：

- Editor 默认启用，便于本地 Play Mode 调试。
- Player 默认关闭，避免正式包默认暴露 QA WebSocket 能力。

启用状态按以下优先级解析：

1. 运行时 API：`QaTestClient.SetClientEnabled(...)` 或 `QaTestClient.SetGlobalEnabled(...)`。
2. 命令行参数：`--qa-enable`、`--qa-disable`、`--qa-enabled=true|false`、`--qa-test-enabled=true|false`。
3. 环境变量：`QA_TEST_ENABLED=1|0`。
4. PlayerPrefs：`QaTest.Enabled`。
5. Inspector 默认值：`enableInEditor`、`enableInPlayer`。

外部代码可以直接控制全局开关：

```csharp
using QaTestFramework;

QaTestClient.SetGlobalEnabled(true);   // 持久化启用；如果当前没有客户端，会自动创建并连接
QaTestClient.SetGlobalEnabled(false);  // 持久化关闭并断开连接
QaTestClient.ClearGlobalEnabled();     // 清除 PlayerPrefs 和运行时覆盖，回到命令行/环境变量/Inspector 默认值
```

`SetGlobalEnabled(false, persist: false)` 可以在客户端创建前临时阻止本次进程自动创建；`persist: true` 会写入 PlayerPrefs，影响后续启动。

Player 包可以由游戏上层在合适时机主动写入连接配置并启动：

```csharp
QaTestClient.SetIpAndPort("192.168.1.10", 3000);
QaTestClient.SetClientName("Phone-LoginSmoke");
QaTestClient.StartConnect();

// 或一次性保存配置并启动连接。
QaTestClient.StartConnect("192.168.1.10", 3000, "Phone-LoginSmoke");

// 仅 Player 生效；开启后下次游戏启动会自动调用 StartConnect()。
QaTestClient.SetAutoConnectOnStartup(true);
```

`SetIpAndPort`、`SetClientName` 和 `SetAutoConnectOnStartup` 总是写入 PlayerPrefs。`StartConnect` 会先读取 PlayerPrefs 中的 `serverIP`、`serverPort`、`clientName` 和 `clientId`，再创建并启用 `[QaTestClient]`，确保首次注册使用上层保存的名称。

也可以直接写入 PlayerPrefs：

```csharp
PlayerPrefs.SetInt(QaTestClient.EnabledPlayerPrefsKey, 1);
PlayerPrefs.Save();
```

命令行示例：

```powershell
Unity.exe -projectPath <projectPath> --qa-enable --qa-server-url ws://localhost:3000/ws
Game.exe --qa-enabled=true
Game.exe --qa-disable
```

## 定义测试方法

给静态方法或 `MonoBehaviour` 实例方法添加 `[QaTest]`：

```csharp
using QaTestFramework;

public static class LoginQaTests
{
    [QaTest("点击登录按钮", "模拟点击登录按钮并返回执行结果。")]
    private static string ClickLoginButton([QaParam("登录按钮对象名")] string objectName)
    {
        return "clicked: " + objectName;
    }
}
```

如果方法需要访问场景对象状态，也可以定义为 `MonoBehaviour` 实例方法。实例方法只有在场景中存在对应组件实例时才会注册：

```csharp
using QaTestFramework;
using UnityEngine;

public sealed class LoginQaPanel : MonoBehaviour
{
    [QaTest("读取登录面板名称", "返回当前挂载对象名称。")]
    private string GetPanelName()
    {
        return gameObject.name;
    }
}
```

支持的参数会从服务端传入的字符串数组转换，可直接使用 `string`、`bool`、`int`、`long`、`float`、`double`、枚举、`Vector2`、`Vector3`、`Vector4`、可空类型、可选参数，以及可由 `JsonUtility.FromJson` 解析的类型。参数可以添加 `[QaParam("参数说明")]`，说明文本会注册为 `methods[].parameters[].description` 并通过 WebSocket 透传给服务端和控制端。方法 ID 由声明类型、方法名和参数类型生成；实例方法会额外包含 Unity 对象实例 ID。

## 返回值

测试方法可以返回：

- 普通值：会用 invariant culture 转为字符串。
- `Task` 或 `Task<T>`：客户端等待完成后返回结果。
- `IEnumerator`：客户端作为协程执行，完成后返回 `QaTestCoroutineReturn` 携带的值。
- `QaTestCoroutineResult`: 用于协程完成后通过回调或闭包提供最终结果。

协程返回示例：

```csharp
[QaTest("等待后返回")]
private static System.Collections.IEnumerator WaitAndReturn(float seconds = 1f)
{
    yield return new UnityEngine.WaitForSeconds(seconds);
    yield return QaTestCoroutineReturn.From("done");
}
```

## 客户端名称

默认名称为 `clientId` 的前 8 位：

```text
3f0a9c12
```

可以通过 `QaTestClient.SetClientName(newName)`、`QaTestClientName.Set(newName, persist: true)` 或实例方法 `SetClientName(newName, persist: true)` 设置自定义名称，并在已连接时重新注册到服务端。也可以通过内置 QA 方法 `设置客户端名称` 远程修改；传空字符串会恢复为默认名称。

Editor 下如果用户完全不修改 `clientName`，并且 Unity 项目根目录没有 `qatest.config.txt`，客户端会继续使用由 `clientId` 推导出的默认名称，不会自动创建本地名称配置。通过 Inspector 修改 `clientName` 后，会立即创建或更新 `qatest.config.txt`；后续本项目内任意场景或任意位置启用的 `QaTestClient` 都会读取该文件中的名称，并同步显示到 Inspector。

Player 下外部配置只认 PlayerPrefs：`QaTest.ClientId`、`QaTest.ClientName`、`QaTest.ServerIP`、`QaTest.ServerPort` 和 `QaTest.AutoConnectOnStartup`。删除 `QaTest.ClientName` 会回到由 `clientId` 推导出的默认名称。

## 客户端 ID

Editor 下用户修改后的 `clientId` 和 `clientName` 会保存到 Unity 项目根目录的 `qatest.config.txt`。Player 下 `clientId` 和 `clientName` 保存到 PlayerPrefs，不读取也不写入 `Application.persistentDataPath/qatest.config.txt`。

首次启动时，如果没有有效 `clientId`，客户端会按当前平台生成种子并追加随机值，再计算 SHA-256 hash，并截取为 32 位小写 hex：

- Editor: 使用 Unity 项目根目录。
- Player: 使用 `Application.identifier` 和 `Application.platform`，并写入 PlayerPrefs `QaTest.ClientId`。

Editor 下如果 `qatest.config.txt` 不存在，客户端会在本次运行中生成 `clientId` 并用其前 8 位作为默认名称，但不会把默认名称写入本地文件。只要 `qatest.config.txt` 存在，启动时就会读取其中的 `clientName` 并更新到 Inspector；Inspector 修改会立即回写配置文件。旧的 64 位 `clientId` 会在读取后规范为前 32 位并回写。Player 下删除 PlayerPrefs 中的 `QaTest.ClientId` 会生成新的身份。

## 执行链路

1. 启动 QA register server。
2. Unity 进入 Play Mode 或 Player 启动，QA 功能启用时 `QaTestClient` 连接 `ws://localhost:3000/ws?role=unity`。
3. 客户端发送 `register` 消息，包含 `clientId`、名称、IP 地址、平台、Unity 版本、机器名、操作系统名、方法列表和本地 busy 状态。
4. Web 控制台或 MCP 工具发送 `execute` 命令。
5. Unity 主线程执行目标 `[QaTest]` 方法，并回传 `qa_result`。
6. 如果 Unity 本地已有请求在执行，新收到的 `execute` 会立即返回失败结果，不会并发执行第二个测试方法。

## 示例脚本

Package Manager 的 Samples 面板可以导入 `QaTest Examples`。`Samples~/Example/` 目录提供了以下示例方法；其中实例方法需要把对应 `MonoBehaviour` 挂到场景对象后才会注册：

- `QaTestStaticSample`: static class 中的静态方法注册示例。
- `QaTestUtilitySample`: 普通 class 中的静态方法注册示例。
- `QaTestSample`: MonoBehaviour 类型中的实例方法、静态方法、日志输出和等待后返回。
- `QaTestPanel`: 面板存在、显隐设置、显隐状态查询、等待显隐状态。
- `QaTestControl`: 控件点击、可交互状态设置和等待。

这些示例目前是 mock 行为，适合验证 register server、Web 控制台和 MCP 工具的完整链路。

## 注意事项

- Unity `.meta` 文件在当前 package 仓库中必须跟踪；新增脚本、asmdef、Samples 或资源时，需要同步提交对应 `.meta` 文件，避免 Unity GUID 在接入工程中漂移。
- QA 功能在 Player 中默认关闭；需要在测试包中启用时，请使用命令行、环境变量、PlayerPrefs、Inspector 或 `QaTestClient.SetGlobalEnabled(true)` 显式开启。
- `QaTestClient` 会自动重连，默认重连间隔为 2 秒。
- 心跳默认每 10 秒发送一次，并会上报 `busy`、`currentRequestId` 和 `currentMethodName`；服务端会清理长时间未心跳的客户端。
