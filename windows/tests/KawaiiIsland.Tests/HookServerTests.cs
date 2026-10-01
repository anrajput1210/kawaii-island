using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using KawaiiIsland.Services.ClaudeCode;

namespace KawaiiIsland.Tests;

public sealed class HookServerTests
{
    [Fact]
    public async Task Permission_request_is_held_until_answered_and_also_reported_as_a_hook()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var answer = new TaskCompletionSource<string>();
        string? hookEvent = null;
        using var server = new HookServer(port) { Permission = _ => answer.Task };
        server.Hook += e => hookEvent = e.GetProperty("hook_event_name").GetString();
        server.Start();

        using var http = new HttpClient();
        var post = http.PostAsync($"http://127.0.0.1:{port}/kawaii/permission",
            new StringContent("""{ "hook_event_name": "PermissionRequest", "tool_name": "Bash" }""", Encoding.UTF8, "application/json"));

        await Task.Delay(3500); // longer than the 3 s read budget: the reply must still arrive
        Assert.False(post.IsCompleted);
        answer.SetResult(ClaudeSettings.PermissionReply("allow"));

        var reply = await (await post).Content.ReadAsStringAsync();
        Assert.Contains("\"allow\"", reply);
        Assert.Equal("PermissionRequest", hookEvent);
    }
}
