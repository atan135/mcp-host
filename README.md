# Unity QA Client

本仓库包含 QA Test Framework 的 Unity 客户端脚本。进入 Play Mode 后，客户端会自动连接 QA register server，扫描当前 AppDomain 中带 `[QaTest]` 的方法并注册到服务端，随后可以接收 Web 控制台或 MCP 工具发来的执行命令。

## 目录结构

- `Runtime/QaTest/`: 客户端核心代码，包括 WebSocket 连接、方法扫描、参数转换、执行结果回传和协程返回值封装。
- `Samples~/Example/`: 示例测试方法，覆盖连通性检查、日志输出、面板状态和控件交互等 mock 场景。

## 接入方式

通过 Package Manager 的 Git URL 引入本仓库，或把本目录作为本地 package 添加到 Unity 工程。运行时代码位于 `Runtime/`，并通过 `QaTestFramework.UnityClient` 程序集定义编译。`QaTestBootstrap` 会在场景加载后自动创建 `[QaTestClient]` 对象，并通过 `DontDestroyOnLoad` 保持连接。

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

也可以在场景中预置 `QaTestClient` 组件，并通过 Inspector 配置 `serverUrl` 序列化字段。客户端会自动补齐 `role=unity` 参数。

## 定义测试方法

给静态方法或 `MonoBehaviour` 实例方法添加 `[QaTest]`：

```csharp
using QaTestFramework;

public static class LoginQaTests
{
    [QaTest("点击登录按钮", "模拟点击登录按钮并返回执行结果。")]
    private static string ClickLoginButton(string objectName)
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

支持的参数会从服务端传入的字符串数组转换，可直接使用 `string`、`bool`、`int`、`long`、`float`、`double`、枚举、`Vector2`、`Vector3`、`Vector4`、可空类型、可选参数，以及可由 `JsonUtility.FromJson` 解析的类型。方法 ID 由声明类型、方法名和参数类型生成；实例方法会额外包含 Unity 对象实例 ID。

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

默认名称为：

```text
<Application.productName>@<SystemInfo.deviceName>
```

可以通过 `QaTestClient.SetClientName(newName, persist: true)` 设置自定义名称。自定义名称会保存到 PlayerPrefs，并在重新注册时同步到服务端。

## 执行链路

1. 启动 QA register server。
2. Unity 进入 Play Mode，`QaTestClient` 连接 `ws://localhost:3000/ws?role=unity`。
3. 客户端发送 `register` 消息，包含 `clientId`、名称、平台、Unity 版本、方法列表和本地 busy 状态。
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
- `QaTestClient` 会自动重连，默认重连间隔为 2 秒。
- 心跳默认每 10 秒发送一次，并会上报 `busy`、`currentRequestId` 和 `currentMethodName`；服务端会清理长时间未心跳的客户端。
