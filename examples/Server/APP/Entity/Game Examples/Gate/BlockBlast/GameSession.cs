using System.Collections.Generic;
using Fantasy.Entitas;
using Fantasy.Network;
using GameLogic.BlockBlast;
using GameLogic.BlockBlast.Algorithms;
using GameLogic.BlockBlast.Core;

namespace Fantasy;

/// <summary>
/// Block Blast 单局权威会话实例(挂 Gate Scene,逐局一个)。
/// 服务端是发牌唯一事实源:持有本局棋盘 / 分数 / 步号 / 候选队列 / 逐局发牌器实例,
/// 客户端的 place 只携带输入(候选槽位 + 落点),形状由本实例权威生成、客户端无从指定。
///
/// 数据层(本类只放字段):发牌器实例 <see cref="Generator"/> 与候选队列由 Hotfix 侧
/// GameSessionSystem 在 Awake 时构造,落子/补牌/计分逻辑在 GameSessionHelper。
/// 发牌器构造注入 portable PRNG(seed 服务端签发)+ persistence=null(逐局态不落客户端存储)。
/// </summary>
public sealed class GameSession : Entity
{
    /// <summary>玩家身份(登录签发的权威 PlayerId,从会话 Account 取)。</summary>
    public string PlayerId = string.Empty;

    /// <summary>本局唯一 id(服务端签发)。</summary>
    public long GameId;

    /// <summary>本局发牌种子(服务端签发,回带客户端供发牌预测)。</summary>
    public long Seed;

    /// <summary>权威步号(每次成功落子 +1;开局 = 0)。</summary>
    public int Step;

    /// <summary>当前权威分数。</summary>
    public int Score;

    /// <summary>权威棋盘(8 行位掩码,与 BinaryBoard 同构)。</summary>
    public BinaryBoard Board;

    /// <summary>逐局发牌器实例(无单例;各局各持各的 PRNG 与调度态)。</summary>
    public DynamicWeightDiff Generator;

    /// <summary>当前候选批对应的发牌算法(整批消耗后 AddWeight 反馈用)。逐局存放,不走进程级静态态。</summary>
    public AlgorithmKind LastTrioAlgo = AlgorithmKind.RandomNoDie;

    /// <summary>
    /// 当前候选队列(shapeId)。队首 = 下一个待用候选,落子消耗对应候选置 0,整批消耗后续发下一批。
    /// readonly + 复用清空(与 AccountManageComponent.Accounts 同范式):对象池重用本实体时实例不变、Init 清空续用。
    /// </summary>
    public readonly List<int> CandidateQueue = new List<int>();

    /// <summary>客户端会话引用(推送 / 寿命联动)。</summary>
    public EntityReference<Session> Session;
}
