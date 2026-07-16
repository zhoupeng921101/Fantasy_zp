using System;
using System.Linq;
using Fantasy.Async;
using Fantasy.Network.HTTP;
using Fantasy.Network.Interface;
using Fantasy.Platform.Net;
using Microsoft.AspNetCore.Mvc;

namespace Fantasy;

/// <summary>
/// GM 运维 HTTP 接口(运行在 HttpGift 场景,内网 127.0.0.1:20010)。鉴权由 GmHttpAuthHandler 中间件按 X-GM-Token 请求头统一拦。
/// 各动作把操作转发到 Gate 场景(玩家权威组件所在)执行,复用现有权威 helper 写库并触发在线推送:
///   转发目标优先 WebSocket Gate(编辑器 / WebGL 客户端连它,推送能实时到),无则取首个 Gate;
///   写库经 MongoDB 共享,任一 Gate 均权威;在线推送仅达路由到的那个 Gate 上的会话(玩家在其它 Gate 则下次登录 / 刷新可见)。
/// 返回 JSON:业务响应 { code, message, ... }(code=0 → 200,非0 → 400);转发到 Gate 的传输层失败(RPC 超时 / Gate 侧异常)→ 502。
/// </summary>
[ApiController]
[Route("gm")]
[ServiceFilter(typeof(SceneContextFilter))]
public sealed class GmController : ControllerBase
{
    private readonly Scene _scene;

    public GmController(Scene scene)
    {
        _scene = scene;
    }

    /// <summary>发道具:给 AccountId 发 Count 个 ItemId(限时 / 可堆叠由服务端按配置自动分轨)。</summary>
    [HttpPost("grantItem")]
    public async FTask<IActionResult> GrantItem([FromBody] GmGrantItemDto dto)
    {
        if (dto == null)
        {
            return BadRequest(new { code = -1, message = "empty body" });
        }
        return await Forward<Http2G_GmGrantItemRequest, G2Http_GmGrantItemResponse>(
            new Http2G_GmGrantItemRequest
            {
                AccountId = dto.AccountId ?? string.Empty,
                ItemId = dto.ItemId,
                Count = dto.Count,
            },
            resp => ToResult(resp.Code, new { code = resp.Code, message = resp.Message }));
    }

    /// <summary>改货币:给 AccountId 的 Type 货币加减 Delta(高信任,单次幅度上限由 Gate 侧校验)。</summary>
    [HttpPost("changeCurrency")]
    public async FTask<IActionResult> ChangeCurrency([FromBody] GmChangeCurrencyDto dto)
    {
        if (dto == null)
        {
            return BadRequest(new { code = -1, message = "empty body" });
        }
        return await Forward<Http2G_GmChangeCurrencyRequest, G2Http_GmChangeCurrencyResponse>(
            new Http2G_GmChangeCurrencyRequest
            {
                AccountId = dto.AccountId ?? string.Empty,
                Type = dto.Type,
                Delta = dto.Delta,
                Reason = dto.Reason ?? string.Empty,
            },
            resp => ToResult(resp.Code, new { code = resp.Code, message = resp.Message, newAmount = resp.NewAmount }));
    }

    /// <summary>发邮件:给 AccountId 投一封带奖励的定向邮件。奖励走紧凑字符串 Rewards(格式 `Currency,4,5|Item,30001,2`,空串=无奖励)。</summary>
    [HttpPost("sendMail")]
    public async FTask<IActionResult> SendMail([FromBody] GmSendMailDto dto)
    {
        if (dto == null)
        {
            return BadRequest(new { code = -1, message = "empty body" });
        }
        return await Forward<Http2G_GmSendMailRequest, G2Http_GmSendMailResponse>(
            new Http2G_GmSendMailRequest
            {
                AccountId = dto.AccountId ?? string.Empty,
                Sender = dto.Sender,
                Title = dto.Title ?? string.Empty,
                Content = dto.Content ?? string.Empty,
                ExpireDays = dto.ExpireDays,
                Rewards = dto.Rewards ?? string.Empty,
            },
            resp => ToResult(resp.Code, new { code = resp.Code, message = resp.Message, mailId = resp.MailId }));
    }

    /// <summary>清号:把 AccountId 完整重置为新手态(玩家文档重置 + 删对局/活动/邮件/榜/兑换/流水,保留账号身份)。在线玩家需重登生效。</summary>
    [HttpPost("resetPlayer")]
    public async FTask<IActionResult> ResetPlayer([FromBody] GmResetPlayerDto dto)
    {
        if (dto == null)
        {
            return BadRequest(new { code = -1, message = "empty body" });
        }
        return await Forward<Http2G_GmResetPlayerRequest, G2Http_GmResetPlayerResponse>(
            new Http2G_GmResetPlayerRequest
            {
                AccountId = dto.AccountId ?? string.Empty,
            },
            resp => ToResult(resp.Code, new { code = resp.Code, message = resp.Message }));
    }

    /// <summary>
    /// 统一转发:取 Gate 地址 → Inner RPC 到 Gate → 业务响应交 onOk 映射。
    /// 传输层失败(RPC 超时框架抛异常 / Gate 侧未捕获异常框架回 ErrorCode!=0)统一返 502 干净 JSON,
    /// 不把传输失败误报为业务成功(code=0),也不外泄 500 堆栈页。
    /// </summary>
    private async FTask<IActionResult> Forward<TReq, TResp>(TReq request, Func<TResp, IActionResult> onOk)
        where TReq : class, IAddressRequest
        where TResp : class, IResponse
    {
        var error = TryGetGateAddress(out var gateAddress);
        if (error != null)
        {
            return error;
        }

        try
        {
            var raw = await _scene.Call(gateAddress, request);
            if (raw is not TResp resp)
            {
                return StatusCode(502, new { code = -1, message = "gate rpc returned null or unexpected response" });
            }
            if (resp.ErrorCode != 0)
            {
                return StatusCode(502, new { code = -1, message = $"gate rpc transport error (errorCode={resp.ErrorCode})" });
            }
            return onOk(resp);
        }
        catch (Exception e)
        {
            return StatusCode(502, new { code = -1, message = $"gate rpc failed: {e.Message}" });
        }
    }

    /// <summary>
    /// 取转发目标 Gate 场景入口地址:优先 WebSocket Gate(编辑器 / WebGL 客户端连它,在线推送实时可达),无则取首个 Gate。
    /// 前提:单 world 部署(取全部 Gate 不按 world 过滤);若未来引入多 world,须按玩家所属 world 选 Gate,否则会跨 world 路由到错误的库。
    /// 无任何 Gate 场景时返 503。
    /// </summary>
    private IActionResult? TryGetGateAddress(out long address)
    {
        address = 0;
        var gates = SceneConfigData.Instance.GetSceneBySceneType(SceneType.Gate);
        if (gates == null || gates.Count == 0)
        {
            return StatusCode(503, new { code = -1, message = "no gate scene available" });
        }
        var gate = gates.FirstOrDefault(g => g.NetworkProtocol == "WebSocket") ?? gates[0];
        address = gate.Address;
        return null;
    }

    /// <summary>code=0 → 200 OK,非0 → 400 BadRequest,响应体同为业务 JSON。</summary>
    private IActionResult ToResult(int code, object body)
    {
        return code == 0 ? Ok(body) : BadRequest(body);
    }
}

/// <summary>GM 发道具入参。</summary>
public sealed class GmGrantItemDto
{
    public string? AccountId { get; set; }
    public int ItemId { get; set; }
    public long Count { get; set; }
}

/// <summary>GM 改货币入参。Type = PropertyType 枚举值,Delta 可正可负。</summary>
public sealed class GmChangeCurrencyDto
{
    public string? AccountId { get; set; }
    public int Type { get; set; }
    public long Delta { get; set; }
    public string? Reason { get; set; }
}

/// <summary>GM 发邮件入参(标题/正文直传文本,发件人走 MailSenderType 枚举;奖励走紧凑字符串 Rewards,格式 `Currency,4,5|Item,30001,2`,空串=无奖励)。</summary>
public sealed class GmSendMailDto
{
    public string? AccountId { get; set; }
    public MailSenderType Sender { get; set; }
    public string? Title { get; set; }
    public string? Content { get; set; }
    public int ExpireDays { get; set; }
    public string? Rewards { get; set; }
}

/// <summary>GM 重置玩家入参。</summary>
public sealed class GmResetPlayerDto
{
    public string? AccountId { get; set; }
}
