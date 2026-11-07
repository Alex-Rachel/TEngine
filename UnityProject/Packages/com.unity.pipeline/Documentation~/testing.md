# Tests architecture

How the package's tests exercise commands. There are two complementary styles — **ViaClient** (over real HTTP, end-to-end) and **CommandDirect** (calling the static command method straight) — backed by a small set of shared test helpers.

## Two test styles

### ViaClient — exercise the full HTTP path

Spin up an isolated `PipelineTestServer` and call `server.Execute(command, params)`, naming the command through the `CommandNames` constants (`CommandNames.GameobjectSetTransform` names the `gameobject set_transform` command). This goes over HTTP through the same routing, parameter coercion, and threading the real server uses — it tests the command *as a client sees it*.

```csharp
[Test]
public void SetTransform_ViaClient_ArrayParams_Apply()
{
    var go = Track(new GameObject("Xform219_Arrays"));

    using (var server = new PipelineTestServer())
    {
        var response = server.Execute(CommandNames.GameobjectSetTransform, new
        {
            target   = new { instanceId = PipelineUtils.GetObjectId(go) },
            position = new[] { 1f, 2f, 3f },
            rotation = new[] { 0f, 90f, 0f },
            scale    = new[] { 2f, 2f, 2f }
        });

        Assert.IsTrue(response.IsSuccess, $"{CommandNames.GameobjectSetTransform} should succeed: {response.Error}");
        Assert.AreEqual(new Vector3(1, 2, 3), go.transform.localPosition);
    }
}
```

The test server is isolated: it uses the **test editor port range `7850`–`7899`** and **writes no descriptor**, so it never disturbs the live editor server (which uses `7800`–`7849`). `Execute` pumps the server's own dispatcher while the HTTP call is in flight, so `MainThreadRequired` commands complete even from a plain `[Test]` that blocks the main thread — no deadlock.

### CommandDirect — call the static method directly

Call the command's static method with typed arguments. This is synchronous, with **no server, HTTP, or dispatcher** involved — fast and ideal for unit-testing command logic and parameter handling.

```csharp
[Test]
public void SetTransform_Direct_TypedArrays_Apply()
{
    var go = Track(new GameObject("Xform219_Direct"));

    GameObjectCommands.SetTransform(RefTo(go),
        position: new[] { 1f, 2f, 3f },
        rotation: new[] { 0f, 90f, 0f },
        scale:    new[] { 2f, 2f, 2f });

    Assert.AreEqual(new Vector3(1, 2, 3), go.transform.localPosition);
    Assert.AreEqual(new Vector3(2, 2, 2), go.transform.localScale);
}
```

Use **CommandDirect** to verify a command's behavior with strongly-typed inputs; use **ViaClient** to additionally verify wire-level concerns (JSON coercion, required-parameter validation, threading).

## Shared helpers

| Helper | Role |
|--------|------|
| `PipelineTestServer` | Disposable wrapper that starts an isolated `TestEditorPipelineServer`, wires up a `PipelineClient`, and exposes `Execute(command, parameters, timeout = null, requestTimeout = null)` (both `TimeSpan?`; `timeout` defaults to 30 s); `requestTimeout` is sent as the request's `timeout`. Pumps the server dispatcher during each call to avoid main-thread deadlock. |
| `TestEditorPipelineServer` | `EditorPipelineServer` subclass for tests. Overrides `AdvertisesEndpoint => false`, `GetPortRange() => (7850, 7899)`, and `GetToken()` (from `SecurityTokenManager`) so it is fully isolated from the live server. |
| `PipelineClient` | Test-side HTTP client. Constructed from a URL+token or a server/manager instance. Key methods: `ExecuteCommandAsync` (`/api/exec`), `GetStatusAsync` (`/api/status`), `PostJsonAsync`. Returns a `PipelineResponse` (`IsSuccess`, `StatusCode`, `Error`, `JsonResponse`, `IsCommandSuccess`). |
| `EditorTestUtilities` | Async helpers for editor state, e.g. `WaitFor(Func<bool> condition, TimeSpan? timeout = null)` to poll a condition without busy-waiting. |
| `LiveServerGuard` | Assembly-level `ITestAction` that asserts the **live editor server survives the test run**. |

### `PipelineTestServer` setup

```csharp
public PipelineTestServer()
{
    m_Server = new TestEditorPipelineServer();
    m_Server.Start(); // auto-assigns a port in 7850-7899; writes no descriptor
    m_Client = new PipelineClient($"http://127.0.0.1:{m_Server.Port}", SecurityTokenManager.GetOrCreateToken());
}
```

Wrap it in a `using` (it is disposable) so the isolated server is stopped at the end of the test.

### `LiveServerGuard`

Applied once at assembly scope (`[assembly: LiveServerGuard]`), it runs before and after **every** test in the editor suite. If a live editor server was advertising its descriptor before a test, the guard asserts afterwards that the test did not disturb it — the descriptor still exists, the port is unchanged, and the server is still listening:

```csharp
Assert.IsNotNull(after, $"'{test.Name}' deleted the live pipeline server descriptor");
Assert.AreEqual(m_Before.Port, after.Port, $"'{test.Name}' changed the port");
Assert.IsTrue(IsListening(after.Port, after.EvalToken), $"'{test.Name}' left the server not responding");
```

This is what makes it safe to "dogfood" — run the test suite against the very editor you are driving — without a test clobbering the live server. Tests that intentionally start/stop the live server are marked `[Explicit]` and must hand it back intact.

## See also

- [Creating commands](creating-commands.md) — the command API these tests exercise.
- [Connectivity](connectivity.md) — ports, descriptor, and auth (the test server deliberately skips the descriptor).
