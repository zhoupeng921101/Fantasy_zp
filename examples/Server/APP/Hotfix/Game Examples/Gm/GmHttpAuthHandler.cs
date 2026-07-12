using System;
using Fantasy.Async;
using Fantasy.Event;
using Fantasy.Network.HTTP;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Fantasy;

/// <summary>
/// HttpGift 场景 HTTP 中间件:对 /gm/* 路径校验 X-GM-Token 请求头,缺失或不匹配拒 401。
/// 该接口仅绑内网 127.0.0.1:20010(Fantasy.config outerBindIP),token 为第二道闸。
/// </summary>
public sealed class GmHttpAuthHandler : AsyncEventSystem<OnConfigureHttpApplication>
{
    protected override async FTask Handler(OnConfigureHttpApplication self)
    {
        var expected = GmTokenProvider.Token;
        self.Application.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/gm"))
            {
                var provided = context.Request.Headers["X-GM-Token"].ToString();
                if (string.IsNullOrEmpty(provided) || provided != expected)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { code = -1, message = "invalid or missing X-GM-Token" });
                    return;
                }
            }
            await next.Invoke();
        });
        await FTask.CompletedTask;
    }
}

/// <summary>
/// GM HTTP 鉴权 token 提供者:环境变量 GM_HTTP_TOKEN 优先;未设时用开发默认并告警一次
/// (本接口仅绑内网,开发默认可接受;正式部署须设环境变量)。首次读取后缓存。
/// </summary>
public static class GmTokenProvider
{
    private const string DevDefaultToken = "dev-gm-token";
    private static string? _token;

    public static string Token
    {
        get
        {
            if (_token != null)
            {
                return _token;
            }
            var env = Environment.GetEnvironmentVariable("GM_HTTP_TOKEN");
            if (string.IsNullOrEmpty(env))
            {
                Log.Warning("GM_HTTP_TOKEN 未设置,GM HTTP 接口使用开发默认 token(仅内网调试可接受,正式部署须设环境变量)。");
                _token = DevDefaultToken;
            }
            else
            {
                _token = env;
            }
            return _token;
        }
    }
}
