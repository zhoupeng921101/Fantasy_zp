using LightProto;
using System;
using MemoryPack;
using System.Collections.Generic;
using Fantasy;
using Fantasy.Pool;
using Fantasy.Network.Interface;
using Fantasy.Serialize;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618
// ReSharper disable InconsistentNaming
// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable RedundantTypeArgumentsOfMethod
// ReSharper disable PartialTypeWithSinglePart
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable PreferConcreteValueOverDefault
// ReSharper disable RedundantNameQualifier
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable CheckNamespace
// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable RedundantUsingDirective
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
namespace Fantasy
{
    /// <summary>
    /// 客户端业务方推累计进度请求(§3.2)
    /// 身份从会话取,**不**携带账号字段(协议层即已不预留;即使协议被改坏夹带,handler 也只信会话身份)。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_ActivityIncrement : AMessage, IRequest
    {
        public static C2G_ActivityIncrement Create(bool autoReturn = true)
        {
            var c2G_ActivityIncrement = MessageObjectPool<C2G_ActivityIncrement>.Rent();
            c2G_ActivityIncrement.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_ActivityIncrement.SetIsPool(false);
            }
            
            return c2G_ActivityIncrement;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ActivityId = default;
            Delta = default;
            MessageObjectPool<C2G_ActivityIncrement>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_ActivityIncrement; } 
        [ProtoIgnore]
        public G2C_ActivityIncrementResponse ResponseType { get; set; }
        /// <summary>
        /// 目标活动 id(沿设计 39 §3.1 activity.xlsx.activity_id;不存在返 InvalidRequest)
        /// </summary>
        [ProtoMember(1)]
        public int ActivityId { get; set; }
        /// <summary>
        /// 本次累计增量(必须 > 0;≤ 0 返 InvalidRequest;服务端钳到 ≤ 10000 不报错,客户端从 CurrentCounter 自查实际写入值)
        /// </summary>
        [ProtoMember(2)]
        public int Delta { get; set; }
    }
    /// <summary>
    /// 服务端 Cumulative 累加裁决响应(§3.2)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_ActivityIncrementResponse : AMessage, IResponse
    {
        public static G2C_ActivityIncrementResponse Create(bool autoReturn = true)
        {
            var g2C_ActivityIncrementResponse = MessageObjectPool<G2C_ActivityIncrementResponse>.Rent();
            g2C_ActivityIncrementResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_ActivityIncrementResponse.SetIsPool(false);
            }
            
            return g2C_ActivityIncrementResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            CurrentCounter = default;
            TargetReached = default;
            MessageObjectPool<G2C_ActivityIncrementResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_ActivityIncrementResponse; } 
        [ProtoMember(4)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public ActivityIncrementResultCode ResultCode { get; set; }
        /// <summary>
        /// 写后 counter 值(供客户端 UI 显示当前进度);仅 ResultCode=Success 时有意义,其它码取 0
        /// </summary>
        [ProtoMember(2)]
        public long CurrentCounter { get; set; }
        /// <summary>
        /// 本次 Increment 后是否首次达标 + 抢占成功 + 已投奖;仅 Success 时有意义;true ↔ 服务端已投出活动邮件
        /// </summary>
        [ProtoMember(3)]
        public bool TargetReached { get; set; }
    }
    /// <summary>
    /// 客户端请求看广告领体力(身份从会话取,不带账号 / 次数——服务端按 AdEnergyConfigServer 派生 + 按 PlayerDoc 判每日闸)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_ClaimAdEnergyRequest : AMessage, IRequest
    {
        public static C2G_ClaimAdEnergyRequest Create(bool autoReturn = true)
        {
            var c2G_ClaimAdEnergyRequest = MessageObjectPool<C2G_ClaimAdEnergyRequest>.Rent();
            c2G_ClaimAdEnergyRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_ClaimAdEnergyRequest.SetIsPool(false);
            }
            
            return c2G_ClaimAdEnergyRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_ClaimAdEnergyRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_ClaimAdEnergyRequest; } 
        [ProtoIgnore]
        public G2C_ClaimAdEnergyResponse ResponseType { get; set; }
    }
    /// <summary>
    /// 服务端看广告领体力裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_ClaimAdEnergyResponse : AMessage, IResponse
    {
        public static G2C_ClaimAdEnergyResponse Create(bool autoReturn = true)
        {
            var g2C_ClaimAdEnergyResponse = MessageObjectPool<G2C_ClaimAdEnergyResponse>.Rent();
            g2C_ClaimAdEnergyResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_ClaimAdEnergyResponse.SetIsPool(false);
            }
            
            return g2C_ClaimAdEnergyResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            Energy = default;
            AdEnergyUsedToday = default;
            AdEnergyDailyLimit = default;
            MessageObjectPool<G2C_ClaimAdEnergyResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_ClaimAdEnergyResponse; } 
        [ProtoMember(5)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码(ClaimAdEnergyResultCode)
        /// </summary>
        [ProtoMember(1)]
        public int ResultCode { get; set; }
        /// <summary>
        /// 服务端当前权威体力(成功 = 发后夹软上限;失败 = 当前值;读取失败为 0)
        /// </summary>
        [ProtoMember(2)]
        public long Energy { get; set; }
        /// <summary>
        /// 今日已用看广告领体力次数(懒重置 + 本次成功 +1 后的权威值;失败 = 懒重置后当前值)
        /// </summary>
        [ProtoMember(3)]
        public int AdEnergyUsedToday { get; set; }
        /// <summary>
        /// 每日看广告领体力次数上限(= AdEnergyConfigServer.DailyMax,供客户端算今日剩余次数)
        /// </summary>
        [ProtoMember(4)]
        public int AdEnergyDailyLimit { get; set; }
    }
    /// <summary>
    /// 发牌调度器完整状态向量:候选队列 + 跨手累积调度态 + PRNG 游标。
    /// 供重连恢复与客户端发牌预测对账消费;权威态回带,不被客户端反向写入。
    /// 含 PRNG 游标(RngS0/RngS1)后,客户端可在 snapshot / 对账时完全复位预测发牌器游标,
    /// 即使曾真发散也能从此点逐位接续(不再退化为只靠服务端候选队列覆盖)。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class BlockGenState : AMessage, IDisposable
    {
        public static BlockGenState Create(bool autoReturn = true)
        {
            var blockGenState = MessageObjectPool<BlockGenState>.Rent();
            blockGenState.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                blockGenState.SetIsPool(false);
            }
            
            return blockGenState;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            CandidateQueue.Clear();
            DynamicWeight = default;
            PreDynamicWeight = default;
            RefillIndex = default;
            BcInWindow = default;
            BcCooldown = default;
            RngS0 = default;
            RngS1 = default;
            LastAlgo = default;
            LastTierId = default;
            MessageObjectPool<BlockGenState>.Return(this);
        }
        /// <summary>
        /// 当前候选队列 shapeId(队首=下一个待用候选;长度通常为 3)
        /// </summary>
        [ProtoMember(1)]
        public List<int> CandidateQueue { get; set; } = new List<int>();
        /// <summary>
        /// 动态权重(跨手累积)
        /// </summary>
        [ProtoMember(2)]
        public int DynamicWeight { get; set; }
        /// <summary>
        /// 上一手权重增量(同向/换向判定用)
        /// </summary>
        [ProtoMember(3)]
        public int PreDynamicWeight { get; set; }
        /// <summary>
        /// 本局已发过几次 trio(FirstRound 触发判定用)
        /// </summary>
        [ProtoMember(4)]
        public int RefillIndex { get; set; }
        /// <summary>
        /// 清屏窗口是否激活
        /// </summary>
        [ProtoMember(5)]
        public bool BcInWindow { get; set; }
        /// <summary>
        /// 清屏冷却剩余回合
        /// </summary>
        [ProtoMember(6)]
        public int BcCooldown { get; set; }
        /// <summary>
        /// xorshift128+ 内部状态字 s0(发牌游标;以 int64 承载 ulong 位型)
        /// </summary>
        [ProtoMember(7)]
        public long RngS0 { get; set; }
        /// <summary>
        /// xorshift128+ 内部状态字 s1(发牌游标;以 int64 承载 ulong 位型)
        /// </summary>
        [ProtoMember(8)]
        public long RngS1 { get; set; }
        /// <summary>
        /// 当前候选批所用算法序号(-1 = 未激活/未发牌;AddWeight 反馈用)
        /// </summary>
        [ProtoMember(9)]
        public int LastAlgo { get; set; }
        /// <summary>
        /// 当前候选批所在 tier id(-2147483648 = null)
        /// </summary>
        [ProtoMember(10)]
        public int LastTierId { get; set; }
    }
    /// <summary>
    /// 客户端请求进入对局(身份从会话取,不携带账号 / 不上传 seed)。
    /// 续局语义:服务端有在局(内存活实例或持久 Doc)则恢复、无则新建;消息名沿用 GameStart。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_GameStartRequest : AMessage, IRequest
    {
        public static C2G_GameStartRequest Create(bool autoReturn = true)
        {
            var c2G_GameStartRequest = MessageObjectPool<C2G_GameStartRequest>.Rent();
            c2G_GameStartRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_GameStartRequest.SetIsPool(false);
            }
            
            return c2G_GameStartRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_GameStartRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_GameStartRequest; } 
        [ProtoIgnore]
        public G2C_GameStartResponse ResponseType { get; set; }
    }
    /// <summary>
    /// 服务端进入对局回带:gameId + seed + 当前候选(新建=首发 / 续局=恢复) + step + score + 完整生成器状态 + 续局标志。
    /// 续局时 Step/Score/InitialTrio/Board/GeneratorState 全是恢复出的中断前权威态(非开局 0 态)。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_GameStartResponse : AMessage, IResponse
    {
        public static G2C_GameStartResponse Create(bool autoReturn = true)
        {
            var g2C_GameStartResponse = MessageObjectPool<G2C_GameStartResponse>.Rent();
            g2C_GameStartResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_GameStartResponse.SetIsPool(false);
            }
            
            return g2C_GameStartResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            GameId = default;
            Seed = default;
            InitialTrio.Clear();
            Step = default;
            if (GeneratorState != null)
            {
                GeneratorState.Dispose();
                GeneratorState = null;
            }
            Resumed = default;
            Score = default;
            Board.Clear();
            SliceJson = default;
            MessageObjectPool<G2C_GameStartResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_GameStartResponse; } 
        [ProtoMember(10)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 本局唯一 id(服务端签发)
        /// </summary>
        [ProtoMember(1)]
        public long GameId { get; set; }
        /// <summary>
        /// 服务端签发的本局发牌种子(客户端发牌预测用,非用于服务端复算来源)
        /// </summary>
        [ProtoMember(2)]
        public long Seed { get; set; }
        /// <summary>
        /// 当前候选队列 shapeId(新建=首发;续局=恢复出的当前候选)
        /// </summary>
        [ProtoMember(3)]
        public List<int> InitialTrio { get; set; } = new List<int>();
        /// <summary>
        /// 当前权威步号(新建 = 0;续局 = 恢复值)
        /// </summary>
        [ProtoMember(4)]
        public int Step { get; set; }
        /// <summary>
        /// 完整生成器状态向量(含 PRNG 游标)
        /// </summary>
        [ProtoMember(5)]
        public BlockGenState GeneratorState { get; set; }
        /// <summary>
        /// 是否为续局恢复(true=恢复已有对局;false=新建)
        /// </summary>
        [ProtoMember(6)]
        public bool Resumed { get; set; }
        /// <summary>
        /// 当前权威分数(新建 = 0;续局 = 恢复值)
        /// </summary>
        [ProtoMember(7)]
        public int Score { get; set; }
        /// <summary>
        /// 当前权威棋盘 8 行位掩码(新建=空盘;续局=恢复盘面)
        /// </summary>
        [ProtoMember(8)]
        public List<int> Board { get; set; } = new List<int>();
        /// <summary>
        /// 局内 cosmetic + 合成经济叠加层不透明切片(续局=恢复出的切片原文;新建=空串)。服务端只搬运不解析,客户端 import
        /// </summary>
        [ProtoMember(9)]
        public string SliceJson { get; set; }
    }
    /// <summary>
    /// 客户端落子请求:只传输入(候选槽位 + 落点),形状服务端权威、不携带 shapeId(反作弊红线)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_PlaceRequest : AMessage, IRequest
    {
        public static C2G_PlaceRequest Create(bool autoReturn = true)
        {
            var c2G_PlaceRequest = MessageObjectPool<C2G_PlaceRequest>.Rent();
            c2G_PlaceRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_PlaceRequest.SetIsPool(false);
            }
            
            return c2G_PlaceRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            GameId = default;
            BaseStep = default;
            CandidateIndex = default;
            PosX = default;
            PosY = default;
            SliceJson = default;
            MessageObjectPool<C2G_PlaceRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_PlaceRequest; } 
        [ProtoIgnore]
        public G2C_PlaceResponse ResponseType { get; set; }
        /// <summary>
        /// 目标对局 id
        /// </summary>
        [ProtoMember(1)]
        public long GameId { get; set; }
        /// <summary>
        /// 客户端认为的当前步号(幂等基准:==执行 / <幂等回当前态 / >拒绝回快照)
        /// </summary>
        [ProtoMember(2)]
        public int BaseStep { get; set; }
        /// <summary>
        /// 落哪个候选槽(0..2)
        /// </summary>
        [ProtoMember(3)]
        public int CandidateIndex { get; set; }
        /// <summary>
        /// 落点列(BinaryBoard 坐标 X)
        /// </summary>
        [ProtoMember(4)]
        public int PosX { get; set; }
        /// <summary>
        /// 落点行(BinaryBoard 坐标 Y)
        /// </summary>
        [ProtoMember(5)]
        public int PosY { get; set; }
        /// <summary>
        /// 客户端当前局内叠加层不透明切片(MergeIngameSave JSON),搭车每步落子。服务端只搬运不解析,成功推进时随 Doc 存盘
        /// </summary>
        [ProtoMember(6)]
        public string SliceJson { get; set; }
    }
    /// <summary>
    /// 服务端落子裁决 + 最新权威态
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_PlaceResponse : AMessage, IResponse
    {
        public static G2C_PlaceResponse Create(bool autoReturn = true)
        {
            var g2C_PlaceResponse = MessageObjectPool<G2C_PlaceResponse>.Rent();
            g2C_PlaceResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_PlaceResponse.SetIsPool(false);
            }
            
            return g2C_PlaceResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            Step = default;
            Score = default;
            EliminatedLines = default;
            NewCandidate = default;
            Board.Clear();
            if (GeneratorState != null)
            {
                GeneratorState.Dispose();
                GeneratorState = null;
            }
            GameOver = default;
            FinalScore = default;
            BestScore = default;
            NewEnergy = default;
            MessageObjectPool<G2C_PlaceResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_PlaceResponse; } 
        [ProtoMember(12)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 落子裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public PlaceResultCode ResultCode { get; set; }
        /// <summary>
        /// 执行后(或当前)权威步号
        /// </summary>
        [ProtoMember(2)]
        public int Step { get; set; }
        /// <summary>
        /// 当前权威分数
        /// </summary>
        [ProtoMember(3)]
        public int Score { get; set; }
        /// <summary>
        /// 本次消除的行列数(StepAdvanced 时有效)
        /// </summary>
        [ProtoMember(4)]
        public int EliminatedLines { get; set; }
        /// <summary>
        /// 补入队尾的新候选 shapeId(本步补牌时有效;未补为 -1)
        /// </summary>
        [ProtoMember(5)]
        public int NewCandidate { get; set; }
        /// <summary>
        /// 最新权威棋盘(8 行位掩码)
        /// </summary>
        [ProtoMember(6)]
        public List<int> Board { get; set; } = new List<int>();
        /// <summary>
        /// 最新完整生成器状态向量
        /// </summary>
        [ProtoMember(7)]
        public BlockGenState GeneratorState { get; set; }
        /// <summary>
        /// 终局信号:服务端判定当前候选无任一放置顺序可放(jam),本局结束
        /// </summary>
        [ProtoMember(8)]
        public bool GameOver { get; set; }
        /// <summary>
        /// 终局权威最终分(GameOver=true 时有效;= 终局时 Score)
        /// </summary>
        [ProtoMember(9)]
        public int FinalScore { get; set; }
        /// <summary>
        /// 终局入榜后该榜当前最佳分(GameOver=true 且入榜服务可用时有效;否则 0)
        /// </summary>
        [ProtoMember(10)]
        public long BestScore { get; set; }
        /// <summary>
        /// 落子后玩家权威体力绝对值(StepAdvanced 时服务端派生落账后余额;客户端用它对账体力,先扣 PlaceCost 再消行返还夹 EnergyCap)。非 StepAdvanced 分支或体力服务不可用时回带当前权威余额供对齐
        /// </summary>
        [ProtoMember(11)]
        public long NewEnergy { get; set; }
    }
    /// <summary>
    /// 客户端消除道具请求:清目标格所在整行整列(脱困道具,设计 49 §3.1)。
    /// 只传输入(目标格 + 幂等基准步号),清哪些格 / 消耗多少体力全由服务端权威算,不携带体力值、不携带被清格。
    /// 消除道具作为一次 board-mutating 动作推进 Step(与落子同一步号轴),但不消耗候选、不推进发牌调度、不续发新批。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_ClearToolRequest : AMessage, IRequest
    {
        public static C2G_ClearToolRequest Create(bool autoReturn = true)
        {
            var c2G_ClearToolRequest = MessageObjectPool<C2G_ClearToolRequest>.Rent();
            c2G_ClearToolRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_ClearToolRequest.SetIsPool(false);
            }
            
            return c2G_ClearToolRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            GameId = default;
            BaseStep = default;
            PosX = default;
            PosY = default;
            SliceJson = default;
            MessageObjectPool<C2G_ClearToolRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_ClearToolRequest; } 
        [ProtoIgnore]
        public G2C_ClearToolResponse ResponseType { get; set; }
        /// <summary>
        /// 目标对局 id
        /// </summary>
        [ProtoMember(1)]
        public long GameId { get; set; }
        /// <summary>
        /// 客户端认为的当前步号(幂等基准:==执行 / <幂等回当前态 / >拒绝回快照)
        /// </summary>
        [ProtoMember(2)]
        public int BaseStep { get; set; }
        /// <summary>
        /// 目标格列(BinaryBoard 坐标 X)
        /// </summary>
        [ProtoMember(3)]
        public int PosX { get; set; }
        /// <summary>
        /// 目标格行(BinaryBoard 坐标 Y)
        /// </summary>
        [ProtoMember(4)]
        public int PosY { get; set; }
        /// <summary>
        /// 客户端当前局内叠加层不透明切片(MergeIngameSave JSON),搭车每次消除道具。服务端只搬运不解析,成功清行列时随 Doc 存盘
        /// </summary>
        [ProtoMember(5)]
        public string SliceJson { get; set; }
    }
    /// <summary>
    /// 服务端消除道具裁决 + 最新权威态
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_ClearToolResponse : AMessage, IResponse
    {
        public static G2C_ClearToolResponse Create(bool autoReturn = true)
        {
            var g2C_ClearToolResponse = MessageObjectPool<G2C_ClearToolResponse>.Rent();
            g2C_ClearToolResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_ClearToolResponse.SetIsPool(false);
            }
            
            return g2C_ClearToolResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            Step = default;
            Score = default;
            ClearedCells = default;
            Board.Clear();
            if (GeneratorState != null)
            {
                GeneratorState.Dispose();
                GeneratorState = null;
            }
            NewEnergy = default;
            MessageObjectPool<G2C_ClearToolResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_ClearToolResponse; } 
        [ProtoMember(8)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 消除道具裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public ClearToolResultCode ResultCode { get; set; }
        /// <summary>
        /// 执行后(或当前)权威步号
        /// </summary>
        [ProtoMember(2)]
        public int Step { get; set; }
        /// <summary>
        /// 当前权威分数(消除道具不计分,回带当前值供对账)
        /// </summary>
        [ProtoMember(3)]
        public int Score { get; set; }
        /// <summary>
        /// 本次清掉的格数(Cleared 时有效)
        /// </summary>
        [ProtoMember(4)]
        public int ClearedCells { get; set; }
        /// <summary>
        /// 最新权威棋盘(8 行位掩码)
        /// </summary>
        [ProtoMember(5)]
        public List<int> Board { get; set; } = new List<int>();
        /// <summary>
        /// 最新完整生成器状态向量(消除道具不推进发牌,回带当前态供对账)
        /// </summary>
        [ProtoMember(6)]
        public BlockGenState GeneratorState { get; set; }
        /// <summary>
        /// 玩家体力绝对值:Cleared=扣费后余额 / NotEnoughEnergy=当前余额(供回滚乐观清);其余不改体力的分支为 0(客户端忽略,靠属性推送/快照对齐)
        /// </summary>
        [ProtoMember(7)]
        public long NewEnergy { get; set; }
    }
    /// <summary>
    /// 客户端请求当前对局完整快照(重连 / 恢复)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_GameSnapshotRequest : AMessage, IRequest
    {
        public static C2G_GameSnapshotRequest Create(bool autoReturn = true)
        {
            var c2G_GameSnapshotRequest = MessageObjectPool<C2G_GameSnapshotRequest>.Rent();
            c2G_GameSnapshotRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_GameSnapshotRequest.SetIsPool(false);
            }
            
            return c2G_GameSnapshotRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            GameId = default;
            MessageObjectPool<C2G_GameSnapshotRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_GameSnapshotRequest; } 
        [ProtoIgnore]
        public G2C_GameSnapshotResponse ResponseType { get; set; }
        /// <summary>
        /// 目标对局 id
        /// </summary>
        [ProtoMember(1)]
        public long GameId { get; set; }
    }
    /// <summary>
    /// 服务端回带完整权威态
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_GameSnapshotResponse : AMessage, IResponse
    {
        public static G2C_GameSnapshotResponse Create(bool autoReturn = true)
        {
            var g2C_GameSnapshotResponse = MessageObjectPool<G2C_GameSnapshotResponse>.Rent();
            g2C_GameSnapshotResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_GameSnapshotResponse.SetIsPool(false);
            }
            
            return g2C_GameSnapshotResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            Board.Clear();
            Score = default;
            Step = default;
            CandidateQueue.Clear();
            if (GeneratorState != null)
            {
                GeneratorState.Dispose();
                GeneratorState = null;
            }
            SliceJson = default;
            MessageObjectPool<G2C_GameSnapshotResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_GameSnapshotResponse; } 
        [ProtoMember(8)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 结果码
        /// </summary>
        [ProtoMember(1)]
        public GameSnapshotResultCode ResultCode { get; set; }
        /// <summary>
        /// 棋盘(8 行位掩码)
        /// </summary>
        [ProtoMember(2)]
        public List<int> Board { get; set; } = new List<int>();
        /// <summary>
        /// 当前权威分数
        /// </summary>
        [ProtoMember(3)]
        public int Score { get; set; }
        /// <summary>
        /// 当前权威步号
        /// </summary>
        [ProtoMember(4)]
        public int Step { get; set; }
        /// <summary>
        /// 当前候选队列 shapeId
        /// </summary>
        [ProtoMember(5)]
        public List<int> CandidateQueue { get; set; } = new List<int>();
        /// <summary>
        /// 完整生成器状态向量
        /// </summary>
        [ProtoMember(6)]
        public BlockGenState GeneratorState { get; set; }
        /// <summary>
        /// 局内 cosmetic + 合成经济叠加层不透明切片(恢复出的切片原文;无切片=空串)。服务端只搬运不解析,客户端 import
        /// </summary>
        [ProtoMember(7)]
        public string SliceJson { get; set; }
    }
    /// <summary>
    /// 客户端请求购买体力(身份从会话取,不带账号 / 不带单价 / 不带发放量——服务端自己派生)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_BuyEnergyRequest : AMessage, IRequest
    {
        public static C2G_BuyEnergyRequest Create(bool autoReturn = true)
        {
            var c2G_BuyEnergyRequest = MessageObjectPool<C2G_BuyEnergyRequest>.Rent();
            c2G_BuyEnergyRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_BuyEnergyRequest.SetIsPool(false);
            }
            
            return c2G_BuyEnergyRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_BuyEnergyRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_BuyEnergyRequest; } 
        [ProtoIgnore]
        public G2C_BuyEnergyResponse ResponseType { get; set; }
    }
    /// <summary>
    /// 服务端购买体力裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_BuyEnergyResponse : AMessage, IResponse
    {
        public static G2C_BuyEnergyResponse Create(bool autoReturn = true)
        {
            var g2C_BuyEnergyResponse = MessageObjectPool<G2C_BuyEnergyResponse>.Rent();
            g2C_BuyEnergyResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_BuyEnergyResponse.SetIsPool(false);
            }
            
            return g2C_BuyEnergyResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            Diamond = default;
            Energy = default;
            BuyEnergyUsedToday = default;
            BuyEnergyDailyLimit = default;
            MessageObjectPool<G2C_BuyEnergyResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_BuyEnergyResponse; } 
        [ProtoMember(6)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public BuyEnergyResultCode ResultCode { get; set; }
        /// <summary>
        /// 成功 = 扣后钻石余额;NotEnoughDiamond / 其它失败 = 当前钻石余额;读失败为 0
        /// </summary>
        [ProtoMember(2)]
        public long Diamond { get; set; }
        /// <summary>
        /// 成功 = 发后体力余额;OverLimit / 其它失败 = 当前体力余额;读失败为 0
        /// </summary>
        [ProtoMember(3)]
        public long Energy { get; set; }
        /// <summary>
        /// 裁决后今日已用购买次数(服务端权威;成功 = +1 后当日值,拒绝 = 懒重置后当日值),客户端据此算剩余
        /// </summary>
        [ProtoMember(4)]
        public int BuyEnergyUsedToday { get; set; }
        /// <summary>
        /// 每日购买次数上限(= BuyEnergyConfigServer.DailyLimit),客户端据此算剩余
        /// </summary>
        [ProtoMember(5)]
        public int BuyEnergyDailyLimit { get; set; }
    }
    /// <summary>
    /// 客户端请求换装(身份从会话取,不带账号;服务端校验目标已解锁才切换)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_EquipCosmeticRequest : AMessage, IRequest
    {
        public static C2G_EquipCosmeticRequest Create(bool autoReturn = true)
        {
            var c2G_EquipCosmeticRequest = MessageObjectPool<C2G_EquipCosmeticRequest>.Rent();
            c2G_EquipCosmeticRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_EquipCosmeticRequest.SetIsPool(false);
            }
            
            return c2G_EquipCosmeticRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Kind = default;
            Id = default;
            MessageObjectPool<C2G_EquipCosmeticRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_EquipCosmeticRequest; } 
        [ProtoIgnore]
        public G2C_EquipCosmeticResponse ResponseType { get; set; }
        /// <summary>
        /// 修饰种类(CosmeticKind:1=头像 / 2=头像框)
        /// </summary>
        [ProtoMember(1)]
        public int Kind { get; set; }
        /// <summary>
        /// 目标佩戴 id
        /// </summary>
        [ProtoMember(2)]
        public int Id { get; set; }
    }
    /// <summary>
    /// 服务端换装裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_EquipCosmeticResponse : AMessage, IResponse
    {
        public static G2C_EquipCosmeticResponse Create(bool autoReturn = true)
        {
            var g2C_EquipCosmeticResponse = MessageObjectPool<G2C_EquipCosmeticResponse>.Rent();
            g2C_EquipCosmeticResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_EquipCosmeticResponse.SetIsPool(false);
            }
            
            return g2C_EquipCosmeticResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            CurrentAvatarId = default;
            CurrentFrameId = default;
            MessageObjectPool<G2C_EquipCosmeticResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_EquipCosmeticResponse; } 
        [ProtoMember(4)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码(EquipCosmeticResultCode)
        /// </summary>
        [ProtoMember(1)]
        public int ResultCode { get; set; }
        /// <summary>
        /// 服务端当前权威佩戴头像 id(成功 = 切换后;失败 = 当前值供客户端回退)
        /// </summary>
        [ProtoMember(2)]
        public int CurrentAvatarId { get; set; }
        /// <summary>
        /// 服务端当前权威佩戴头像框 id(同上)
        /// </summary>
        [ProtoMember(3)]
        public int CurrentFrameId { get; set; }
    }
    /// <summary>
    /// 客户端上报解锁(client-report:客户端按等级配置算出解锁、上报 id,服务端 sanity 后幂等加入集合)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_UnlockCosmeticRequest : AMessage, IRequest
    {
        public static C2G_UnlockCosmeticRequest Create(bool autoReturn = true)
        {
            var c2G_UnlockCosmeticRequest = MessageObjectPool<C2G_UnlockCosmeticRequest>.Rent();
            c2G_UnlockCosmeticRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_UnlockCosmeticRequest.SetIsPool(false);
            }
            
            return c2G_UnlockCosmeticRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Kind = default;
            Id = default;
            MessageObjectPool<C2G_UnlockCosmeticRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_UnlockCosmeticRequest; } 
        [ProtoIgnore]
        public G2C_UnlockCosmeticResponse ResponseType { get; set; }
        /// <summary>
        /// 修饰种类(CosmeticKind:1=头像 / 2=头像框)
        /// </summary>
        [ProtoMember(1)]
        public int Kind { get; set; }
        /// <summary>
        /// 待解锁 id
        /// </summary>
        [ProtoMember(2)]
        public int Id { get; set; }
    }
    /// <summary>
    /// 服务端解锁上报裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_UnlockCosmeticResponse : AMessage, IResponse
    {
        public static G2C_UnlockCosmeticResponse Create(bool autoReturn = true)
        {
            var g2C_UnlockCosmeticResponse = MessageObjectPool<G2C_UnlockCosmeticResponse>.Rent();
            g2C_UnlockCosmeticResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_UnlockCosmeticResponse.SetIsPool(false);
            }
            
            return g2C_UnlockCosmeticResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            UnlockedIds.Clear();
            MessageObjectPool<G2C_UnlockCosmeticResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_UnlockCosmeticResponse; } 
        [ProtoMember(3)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码(UnlockCosmeticResultCode)
        /// </summary>
        [ProtoMember(1)]
        public int ResultCode { get; set; }
        /// <summary>
        /// 更新后的对应已解锁集合(成功回带,供客户端对账;失败为当前集合或空)
        /// </summary>
        [ProtoMember(2)]
        public List<int> UnlockedIds { get; set; } = new List<int>();
    }
    /// <summary>
    /// 单条批量解锁项
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class UnlockCosmeticItem : AMessage, IDisposable
    {
        public static UnlockCosmeticItem Create(bool autoReturn = true)
        {
            var unlockCosmeticItem = MessageObjectPool<UnlockCosmeticItem>.Rent();
            unlockCosmeticItem.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                unlockCosmeticItem.SetIsPool(false);
            }
            
            return unlockCosmeticItem;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Kind = default;
            Id = default;
            MessageObjectPool<UnlockCosmeticItem>.Return(this);
        }
        /// <summary>
        /// 修饰种类(CosmeticKind:1=头像 / 2=头像框)
        /// </summary>
        [ProtoMember(1)]
        public int Kind { get; set; }
        /// <summary>
        /// 待解锁 id
        /// </summary>
        [ProtoMember(2)]
        public int Id { get; set; }
    }
    /// <summary>
    /// 客户端批量上报解锁(一次携带 N 项,身份从会话取)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_UnlockCosmeticBatchRequest : AMessage, IRequest
    {
        public static C2G_UnlockCosmeticBatchRequest Create(bool autoReturn = true)
        {
            var c2G_UnlockCosmeticBatchRequest = MessageObjectPool<C2G_UnlockCosmeticBatchRequest>.Rent();
            c2G_UnlockCosmeticBatchRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_UnlockCosmeticBatchRequest.SetIsPool(false);
            }
            
            return c2G_UnlockCosmeticBatchRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            foreach (var __t in Items) __t.Dispose();
            Items.Clear();
            MessageObjectPool<C2G_UnlockCosmeticBatchRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_UnlockCosmeticBatchRequest; } 
        [ProtoIgnore]
        public G2C_UnlockCosmeticBatchResponse ResponseType { get; set; }
        /// <summary>
        /// 待解锁项列表(空 → no-op,回带当前两集合)
        /// </summary>
        [ProtoMember(1)]
        public List<UnlockCosmeticItem> Items { get; set; } = new List<UnlockCosmeticItem>();
    }
    /// <summary>
    /// 服务端批量解锁裁决响应(回带处理后两个 kind 的最终解锁集)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_UnlockCosmeticBatchResponse : AMessage, IResponse
    {
        public static G2C_UnlockCosmeticBatchResponse Create(bool autoReturn = true)
        {
            var g2C_UnlockCosmeticBatchResponse = MessageObjectPool<G2C_UnlockCosmeticBatchResponse>.Rent();
            g2C_UnlockCosmeticBatchResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_UnlockCosmeticBatchResponse.SetIsPool(false);
            }
            
            return g2C_UnlockCosmeticBatchResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            UnlockedAvatarIds.Clear();
            UnlockedFrameIds.Clear();
            MessageObjectPool<G2C_UnlockCosmeticBatchResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_UnlockCosmeticBatchResponse; } 
        [ProtoMember(4)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 整体结果码(UnlockCosmeticResultCode:Success / NotLoggedIn / RateLimited / ServiceUnavailable)
        /// </summary>
        [ProtoMember(1)]
        public int ResultCode { get; set; }
        /// <summary>
        /// 处理后最终头像解锁集(权威)
        /// </summary>
        [ProtoMember(2)]
        public List<int> UnlockedAvatarIds { get; set; } = new List<int>();
        /// <summary>
        /// 处理后最终头像框解锁集(权威)
        /// </summary>
        [ProtoMember(3)]
        public List<int> UnlockedFrameIds { get; set; } = new List<int>();
    }
    /// <summary>
    /// 客户端进入主游戏(登录后、主游戏可交互前发起;每次进入主游戏阶段调用一次)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_EnterMainGameRequest : AMessage, IRequest
    {
        public static C2G_EnterMainGameRequest Create(bool autoReturn = true)
        {
            var c2G_EnterMainGameRequest = MessageObjectPool<C2G_EnterMainGameRequest>.Rent();
            c2G_EnterMainGameRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_EnterMainGameRequest.SetIsPool(false);
            }
            
            return c2G_EnterMainGameRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_EnterMainGameRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_EnterMainGameRequest; } 
        [ProtoIgnore]
        public G2C_EnterMainGameResponse ResponseType { get; set; }
    }
    /// <summary>
    /// 服务端对进入主游戏请求的响应:订单快照 + 道具持有 + 塔罗进度
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_EnterMainGameResponse : AMessage, IResponse
    {
        public static G2C_EnterMainGameResponse Create(bool autoReturn = true)
        {
            var g2C_EnterMainGameResponse = MessageObjectPool<G2C_EnterMainGameResponse>.Rent();
            g2C_EnterMainGameResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_EnterMainGameResponse.SetIsPool(false);
            }
            
            return g2C_EnterMainGameResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            if (OrderSnapshot != null)
            {
                OrderSnapshot.Dispose();
                OrderSnapshot = null;
            }
            foreach (var __t in ItemHoldings) __t.Dispose();
            ItemHoldings.Clear();
            foreach (var __t in TarotProgress) __t.Dispose();
            TarotProgress.Clear();
            ItemDataLoaded = default;
            MessageObjectPool<G2C_EnterMainGameResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_EnterMainGameResponse; } 
        [ProtoMember(5)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 订单系统当前激活快照(派生自 PlayerDoc 的 OrderCursor / DeliveredMask;
        /// 若服务不可用 / 玩家文档读不到 → 空 ActiveOrders 占位,客户端按"无订单可交付"降级显示)
        /// </summary>
        [ProtoMember(1)]
        public MergeOrderSnapshot OrderSnapshot { get; set; }
        /// <summary>
        /// 道具持有整份快照(PlayerDoc.ItemHoldings 投影,当前业务方 = 背包堆叠道具;仅 ItemDataLoaded=true 时可信)
        /// </summary>
        [ProtoMember(2)]
        public List<ItemHolding> ItemHoldings { get; set; } = new List<ItemHolding>();
        /// <summary>
        /// 塔罗牌进度全集(PlayerDoc.TarotProgress 投影,cardId → 已购步数;仅 ItemDataLoaded=true 时可信。
        /// 未列出的牌视为 0 步;步数 == 该牌 UnlockCosts.Length 即已激活)
        /// </summary>
        [ProtoMember(3)]
        public List<TarotProgressEntry> TarotProgress { get; set; } = new List<TarotProgressEntry>();
        /// <summary>
        /// 道具持有 + 塔罗进度两段是否真取到玩家数据:true = 上两字段为权威值(空 = 权威空集,客户端整份覆盖);
        /// false = 服务端降级(未登录 / 服务不可用 / 读库失败),客户端保留既有投影不清空。
        /// proto3 repeated 无法区分「空集」与「缺失」,靠本标志承载该区分。
        /// </summary>
        [ProtoMember(4)]
        public bool ItemDataLoaded { get; set; }
    }
    /// <summary>
    /// 客户端登陆到Gate服务器
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_LoginGameRequest : AMessage, IRequest
    {
        public static C2G_LoginGameRequest Create(bool autoReturn = true)
        {
            var c2G_LoginGameRequest = MessageObjectPool<C2G_LoginGameRequest>.Rent();
            c2G_LoginGameRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_LoginGameRequest.SetIsPool(false);
            }
            
            return c2G_LoginGameRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            AccountName = default;
            LocalPlayerId = default;
            MessageObjectPool<C2G_LoginGameRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_LoginGameRequest; } 
        [ProtoIgnore]
        public G2C_LoginGameResponse ResponseType { get; set; }
        [ProtoMember(1)]
        public string AccountName { get; set; }
        /// <summary>
        /// 客户端本地已有的 playerId(老档迁移用,首次走新登录时上交认领);新装/无本地值传空串。
        /// </summary>
        [ProtoMember(2)]
        public string LocalPlayerId { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_LoginGameResponse : AMessage, IResponse
    {
        public static G2C_LoginGameResponse Create(bool autoReturn = true)
        {
            var g2C_LoginGameResponse = MessageObjectPool<G2C_LoginGameResponse>.Rent();
            g2C_LoginGameResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_LoginGameResponse.SetIsPool(false);
            }
            
            return g2C_LoginGameResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            PlayerId = default;
            ErrorMessage = default;
            MessageObjectPool<G2C_LoginGameResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_LoginGameResponse; } 
        [ProtoMember(3)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 服务端签发/认领后的权威 playerId(账号级稳定唯一,后续登录恒返同一值)。
        /// </summary>
        [ProtoMember(1)]
        public string PlayerId { get; set; }
        /// <summary>
        /// 登录失败时的人类可读原因(面向排障,中文);成功时为空串。ErrorCode != 0 时据此说明具体失败点。
        /// </summary>
        [ProtoMember(2)]
        public string ErrorMessage { get; set; }
    }
    /// <summary>
    /// 单档奖励项(= block.TbGoddessReward 一行:元素等级 + 发放数量;元素类型由响应 ElementType 统一给出)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class GoddessRewardItem : AMessage, IDisposable
    {
        public static GoddessRewardItem Create(bool autoReturn = true)
        {
            var goddessRewardItem = MessageObjectPool<GoddessRewardItem>.Rent();
            goddessRewardItem.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                goddessRewardItem.SetIsPool(false);
            }
            
            return goddessRewardItem;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Level = default;
            Count = default;
            MessageObjectPool<GoddessRewardItem>.Return(this);
        }
        /// <summary>
        /// 元素等级(1..5,MergeOrderConfig.MaxLevel)
        /// </summary>
        [ProtoMember(1)]
        public int Level { get; set; }
        /// <summary>
        /// 发放数量
        /// </summary>
        [ProtoMember(2)]
        public int Count { get; set; }
    }
    /// <summary>
    /// 客户端请求领取女神满档奖励(身份从会话取,不带账号 / 不带奖励内容——服务端按订单态 + 表自定)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_GoddessClaimRequest : AMessage, IRequest
    {
        public static C2G_GoddessClaimRequest Create(bool autoReturn = true)
        {
            var c2G_GoddessClaimRequest = MessageObjectPool<C2G_GoddessClaimRequest>.Rent();
            c2G_GoddessClaimRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_GoddessClaimRequest.SetIsPool(false);
            }
            
            return c2G_GoddessClaimRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Reserved = default;
            MessageObjectPool<C2G_GoddessClaimRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_GoddessClaimRequest; } 
        [ProtoIgnore]
        public G2C_GoddessClaimResponse ResponseType { get; set; }
        /// <summary>
        /// 占位(领取内容全由服务端裁定,当前无入参;proto3 空消息占位,客户端恒填 0)
        /// </summary>
        [ProtoMember(1)]
        public int Reserved { get; set; }
    }
    /// <summary>
    /// 服务端女神领取裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_GoddessClaimResponse : AMessage, IResponse
    {
        public static G2C_GoddessClaimResponse Create(bool autoReturn = true)
        {
            var g2C_GoddessClaimResponse = MessageObjectPool<G2C_GoddessClaimResponse>.Rent();
            g2C_GoddessClaimResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_GoddessClaimResponse.SetIsPool(false);
            }
            
            return g2C_GoddessClaimResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            ElementType = default;
            foreach (var __t in Rewards) __t.Dispose();
            Rewards.Clear();
            NewRating = default;
            OverflowNonce = default;
            MessageObjectPool<G2C_GoddessClaimResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_GoddessClaimResponse; } 
        [ProtoMember(6)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public GoddessClaimResultCode ResultCode { get; set; }
        /// <summary>
        /// 发放元素类型(= 客户端 MergeElement:1剑2杯3杖4星币):取当前未交付订单所需类型;无未交付订单回退当前批槽0池行类型;失败 = 0
        /// </summary>
        [ProtoMember(2)]
        public int ElementType { get; set; }
        /// <summary>
        /// 奖励列表(= TbGoddessReward 各行 等级+数量,客户端逐项 AddDirect(ElementType, Level, Count) 入合成区);失败为空
        /// </summary>
        [ProtoMember(3)]
        public List<GoddessRewardItem> Rewards { get; set; } = new List<GoddessRewardItem>();
        /// <summary>
        /// 领取后 GoddessRating 权威值(CAS 清零故恒 0),客户端据此对账清零本地计数;失败 = 0
        /// </summary>
        [ProtoMember(4)]
        public long NewRating { get; set; }
        /// <summary>
        /// 溢出预算令牌 id(= 本次领取时刻);客户端把盘面装不下的溢出量经 C2G_GoddessOverflow 带此 nonce 上报;失败 = 0
        /// </summary>
        [ProtoMember(5)]
        public long OverflowNonce { get; set; }
    }
    /// <summary>
    /// 客户端上报女神奖励盘面溢出、请求入库(元素道具按数量落 ItemBag;身份从会话取,不带账号——反作弊红线)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_GoddessOverflow : AMessage, IRequest
    {
        public static C2G_GoddessOverflow Create(bool autoReturn = true)
        {
            var c2G_GoddessOverflow = MessageObjectPool<C2G_GoddessOverflow>.Rent();
            c2G_GoddessOverflow.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_GoddessOverflow.SetIsPool(false);
            }
            
            return c2G_GoddessOverflow;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ItemId = default;
            Count = default;
            ReqSeq = default;
            Nonce = default;
            MessageObjectPool<C2G_GoddessOverflow>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_GoddessOverflow; } 
        [ProtoIgnore]
        public G2C_GoddessOverflowResponse ResponseType { get; set; }
        /// <summary>
        /// 元素道具 id(= 32000 + 花色×100 + 等级;服务端校验属元素道具集)
        /// </summary>
        [ProtoMember(1)]
        public int ItemId { get; set; }
        /// <summary>
        /// 溢出数量(客户端算;服务端按令牌剩余预算校验)
        /// </summary>
        [ProtoMember(2)]
        public int Count { get; set; }
        /// <summary>
        /// 请求序号(单调递增,幂等去重防弱网重发双发)
        /// </summary>
        [ProtoMember(3)]
        public long ReqSeq { get; set; }
        /// <summary>
        /// 溢出预算令牌(= 领取响应回带的 OverflowNonce);服务端校验 nonce 匹配 + 剩余预算 >= Count 才落账,把溢出绑死到真实领取
        /// </summary>
        [ProtoMember(4)]
        public long Nonce { get; set; }
    }
    /// <summary>
    /// 溢出入库裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_GoddessOverflowResponse : AMessage, IResponse
    {
        public static G2C_GoddessOverflowResponse Create(bool autoReturn = true)
        {
            var g2C_GoddessOverflowResponse = MessageObjectPool<G2C_GoddessOverflowResponse>.Rent();
            g2C_GoddessOverflowResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_GoddessOverflowResponse.SetIsPool(false);
            }
            
            return g2C_GoddessOverflowResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            ItemId = default;
            NewCount = default;
            MessageObjectPool<G2C_GoddessOverflowResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_GoddessOverflowResponse; } 
        [ProtoMember(4)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public GoddessBagResultCode ResultCode { get; set; }
        /// <summary>
        /// 回带道具 id(= 请求)
        /// </summary>
        [ProtoMember(2)]
        public int ItemId { get; set; }
        /// <summary>
        /// 落账后该道具权威持有量;失败 = 0
        /// </summary>
        [ProtoMember(3)]
        public long NewCount { get; set; }
    }
    /// <summary>
    /// 客户端玩法界面双击背包元素道具、请求取回一个到盘面(服务端权威 -1;身份从会话取)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_RetrieveElement : AMessage, IRequest
    {
        public static C2G_RetrieveElement Create(bool autoReturn = true)
        {
            var c2G_RetrieveElement = MessageObjectPool<C2G_RetrieveElement>.Rent();
            c2G_RetrieveElement.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_RetrieveElement.SetIsPool(false);
            }
            
            return c2G_RetrieveElement;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ItemId = default;
            ReqSeq = default;
            MessageObjectPool<C2G_RetrieveElement>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_RetrieveElement; } 
        [ProtoIgnore]
        public G2C_RetrieveElementResponse ResponseType { get; set; }
        /// <summary>
        /// 元素道具 id(服务端校验属元素道具集)
        /// </summary>
        [ProtoMember(1)]
        public int ItemId { get; set; }
        /// <summary>
        /// 请求序号(单调递增,幂等去重防弱网重发双扣)
        /// </summary>
        [ProtoMember(2)]
        public long ReqSeq { get; set; }
    }
    /// <summary>
    /// 取回裁决响应(成功后客户端再把元素飞回盘面)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_RetrieveElementResponse : AMessage, IResponse
    {
        public static G2C_RetrieveElementResponse Create(bool autoReturn = true)
        {
            var g2C_RetrieveElementResponse = MessageObjectPool<G2C_RetrieveElementResponse>.Rent();
            g2C_RetrieveElementResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_RetrieveElementResponse.SetIsPool(false);
            }
            
            return g2C_RetrieveElementResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            ItemId = default;
            NewCount = default;
            MessageObjectPool<G2C_RetrieveElementResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_RetrieveElementResponse; } 
        [ProtoMember(4)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public GoddessBagResultCode ResultCode { get; set; }
        /// <summary>
        /// 回带道具 id(= 请求)
        /// </summary>
        [ProtoMember(2)]
        public int ItemId { get; set; }
        /// <summary>
        /// 扣减后该道具权威持有量;失败/不足 = 当前持有
        /// </summary>
        [ProtoMember(3)]
        public long NewCount { get; set; }
    }
    /// <summary>
    /// 堆叠轨单项(无独立状态的可堆叠道具:itemId → 持有数量)。快照 / 推送共用。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class InventoryHolding : AMessage, IDisposable
    {
        public static InventoryHolding Create(bool autoReturn = true)
        {
            var inventoryHolding = MessageObjectPool<InventoryHolding>.Rent();
            inventoryHolding.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                inventoryHolding.SetIsPool(false);
            }
            
            return inventoryHolding;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ItemId = default;
            Count = default;
            MessageObjectPool<InventoryHolding>.Return(this);
        }
        /// <summary>
        /// 道具 id(= Luban item.TbItemDef 行 id)
        /// </summary>
        [ProtoMember(1)]
        public int ItemId { get; set; }
        /// <summary>
        /// 持有数量(服务端权威)
        /// </summary>
        [ProtoMember(2)]
        public long Count { get; set; }
    }
    /// <summary>
    /// 批次轨单条(有有效期道具:每次获得一条批次,各自过期时刻)。快照 / 推送共用。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class InventoryLot : AMessage, IDisposable
    {
        public static InventoryLot Create(bool autoReturn = true)
        {
            var inventoryLot = MessageObjectPool<InventoryLot>.Rent();
            inventoryLot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                inventoryLot.SetIsPool(false);
            }
            
            return inventoryLot;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            LotId = default;
            ItemId = default;
            Count = default;
            AcquireMs = default;
            ExpireMs = default;
            MessageObjectPool<InventoryLot>.Return(this);
        }
        /// <summary>
        /// 批次唯一 id(服务端签发,寻址 / 客户端投影对齐)
        /// </summary>
        [ProtoMember(1)]
        public string LotId { get; set; }
        /// <summary>
        /// 道具 id
        /// </summary>
        [ProtoMember(2)]
        public int ItemId { get; set; }
        /// <summary>
        /// 该批持有数量
        /// </summary>
        [ProtoMember(3)]
        public long Count { get; set; }
        /// <summary>
        /// 获得时刻(服务端权威 Unix 毫秒 UTC)
        /// </summary>
        [ProtoMember(4)]
        public long AcquireMs { get; set; }
        /// <summary>
        /// 过期绝对时刻(服务端权威 Unix 毫秒 UTC;客户端倒计时以此减去服务端时间基准 ServerNowMs)
        /// </summary>
        [ProtoMember(5)]
        public long ExpireMs { get; set; }
    }
    /// <summary>
    /// 客户端发起使用道具请求(身份从会话取,不携带账号)。
    /// reqSeq:客户端本地单调递增序号,同一次使用重发用同一 reqSeq,服务端据此幂等去重(防弱网重发双扣)。
    /// 契约(客户端必守):reqSeq 全账号单一单调计数器(跨道具共用,非每道具独立);且串行发号——
    /// 一次使用等其响应回来再发下一个,不并发/乱序投递。服务端幂等锚 LastUseReqSeq 是单标量,
    /// 一个较小 reqSeq 晚于较大者到达会被判 Duplicate(该次使用不生效);Duplicate 分支回推权威背包兜底对账。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_UseItem : AMessage, IRequest
    {
        public static C2G_UseItem Create(bool autoReturn = true)
        {
            var c2G_UseItem = MessageObjectPool<C2G_UseItem>.Rent();
            c2G_UseItem.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_UseItem.SetIsPool(false);
            }
            
            return c2G_UseItem;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ItemId = default;
            Count = default;
            ReqSeq = default;
            MessageObjectPool<C2G_UseItem>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_UseItem; } 
        [ProtoIgnore]
        public G2C_UseItemResponse ResponseType { get; set; }
        /// <summary>
        /// 要使用的道具 id
        /// </summary>
        [ProtoMember(1)]
        public int ItemId { get; set; }
        /// <summary>
        /// 使用数量(> 0)
        /// </summary>
        [ProtoMember(2)]
        public long Count { get; set; }
        /// <summary>
        /// 使用请求单调序号(幂等锚;<= 0 非法;全账号单调 + 串行,见 message 注)
        /// </summary>
        [ProtoMember(3)]
        public long ReqSeq { get; set; }
    }
    /// <summary>
    /// 服务端使用道具裁决响应。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_UseItemResponse : AMessage, IResponse
    {
        public static G2C_UseItemResponse Create(bool autoReturn = true)
        {
            var g2C_UseItemResponse = MessageObjectPool<G2C_UseItemResponse>.Rent();
            g2C_UseItemResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_UseItemResponse.SetIsPool(false);
            }
            
            return g2C_UseItemResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            ItemId = default;
            ConsumedCount = default;
            foreach (var __t in Produced) __t.Dispose();
            Produced.Clear();
            MessageObjectPool<G2C_UseItemResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_UseItemResponse; } 
        [ProtoMember(5)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public UseItemResultCode ResultCode { get; set; }
        /// <summary>
        /// 回声道具 id
        /// </summary>
        [ProtoMember(2)]
        public int ItemId { get; set; }
        /// <summary>
        /// 实际消耗数量(成功 = Count;失败 = 0)
        /// </summary>
        [ProtoMember(3)]
        public long ConsumedCount { get; set; }
        /// <summary>
        /// 产出清单(本轮效果为货币:复用 PropertyAmount 承载「产出的属性类型 + 数量」;失败为空)
        /// </summary>
        [ProtoMember(4)]
        public List<PropertyAmount> Produced { get; set; } = new List<PropertyAmount>();
    }
    /// <summary>
    /// 服务端背包变更主动推送(获得 / 使用 / 过期后起,推送目标 = 该 UUID 在线全部会话)。
    /// 语义:整份当前背包(两轨全量),客户端直接覆盖本地投影。Loaded=true 恒成立(推送仅在写库成功、持有权威文档时起),
    /// 客户端据 Loaded 区分「权威整份」与降级空占位(proto3 repeated 无法区分空集与缺失,防误清)。
    /// 丢失 = 下次登录拉快照对齐(不重试);离线 = 丢弃。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_InventoryDeltaPush : AMessage, IMessage
    {
        public static G2C_InventoryDeltaPush Create(bool autoReturn = true)
        {
            var g2C_InventoryDeltaPush = MessageObjectPool<G2C_InventoryDeltaPush>.Rent();
            g2C_InventoryDeltaPush.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_InventoryDeltaPush.SetIsPool(false);
            }
            
            return g2C_InventoryDeltaPush;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            foreach (var __t in Holdings) __t.Dispose();
            Holdings.Clear();
            foreach (var __t in Lots) __t.Dispose();
            Lots.Clear();
            ServerNowMs = default;
            Loaded = default;
            MessageObjectPool<G2C_InventoryDeltaPush>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_InventoryDeltaPush; } 
        /// <summary>
        /// 堆叠轨全量(变更后)
        /// </summary>
        [ProtoMember(1)]
        public List<InventoryHolding> Holdings { get; set; } = new List<InventoryHolding>();
        /// <summary>
        /// 批次轨全量(变更后,已剔除过期批次)
        /// </summary>
        [ProtoMember(2)]
        public List<InventoryLot> Lots { get; set; } = new List<InventoryLot>();
        /// <summary>
        /// 服务端权威当前时刻(Unix 毫秒 UTC;客户端据此校正倒计时基准)
        /// </summary>
        [ProtoMember(3)]
        public long ServerNowMs { get; set; }
        /// <summary>
        /// 权威整份标志(恒 true;客户端 false=保留投影不清空)
        /// </summary>
        [ProtoMember(4)]
        public bool Loaded { get; set; }
    }
    /// <summary>
    /// 单条奖励项：类型 + 目标 id + 数量（客户端据类型分别解析货币 / 道具元数据展示）。列表预览 MailListItem.Rewards 与领取响应 G2C_MailClaimResponse.Rewards 共用
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class MailRewardItem : AMessage, IDisposable
    {
        public static MailRewardItem Create(bool autoReturn = true)
        {
            var mailRewardItem = MessageObjectPool<MailRewardItem>.Rent();
            mailRewardItem.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                mailRewardItem.SetIsPool(false);
            }
            
            return mailRewardItem;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            RewardType = default;
            TargetId = default;
            Amount = default;
            MessageObjectPool<MailRewardItem>.Return(this);
        }
        /// <summary>
        /// 奖励类型：1=货币 / 2=道具（口径同 reward.ERewardType）
        /// </summary>
        [ProtoMember(1)]
        public int RewardType { get; set; }
        /// <summary>
        /// 目标 id：货币指 TbNum id，道具指 TbItemDef id
        /// </summary>
        [ProtoMember(2)]
        public int TargetId { get; set; }
        /// <summary>
        /// 发放数量
        /// </summary>
        [ProtoMember(3)]
        public int Amount { get; set; }
    }
    /// <summary>
    /// 邮件列表一条：客户端画收件箱用（含奖励明细，供领取前预览「给什么」）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class MailListItem : AMessage, IDisposable
    {
        public static MailListItem Create(bool autoReturn = true)
        {
            var mailListItem = MessageObjectPool<MailListItem>.Rent();
            mailListItem.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                mailListItem.SetIsPool(false);
            }
            
            return mailListItem;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MailId = default;
            Sender = default;
            Title = default;
            Content = default;
            SendUnixMs = default;
            HasReward = default;
            Claimed = default;
            foreach (var __t in Rewards) __t.Dispose();
            Rewards.Clear();
            MessageObjectPool<MailListItem>.Return(this);
        }
        /// <summary>
        /// 邮件标识（领取时回传定位；广播邮件与定向邮件用同一标识空间，客户端无需区分）
        /// </summary>
        [ProtoMember(1)]
        public string MailId { get; set; }
        /// <summary>
        /// 发件人类型（服务端赋值）
        /// </summary>
        [ProtoMember(2)]
        public MailSenderType Sender { get; set; }
        /// <summary>
        /// 标题（真实文本，来自邮件模板）
        /// </summary>
        [ProtoMember(3)]
        public string Title { get; set; }
        /// <summary>
        /// 正文（真实文本，来自邮件模板）
        /// </summary>
        [ProtoMember(4)]
        public string Content { get; set; }
        /// <summary>
        /// 收件/发件时间（服务端 Unix 毫秒）
        /// </summary>
        [ProtoMember(5)]
        public long SendUnixMs { get; set; }
        /// <summary>
        /// 是否有附件（内嵌附件非空；客户端据此画领取按钮/红点）
        /// </summary>
        [ProtoMember(6)]
        public bool HasReward { get; set; }
        /// <summary>
        /// 该账号对此邮件的领取态（已领=true / 未领=false）
        /// </summary>
        [ProtoMember(7)]
        public bool Claimed { get; set; }
        /// <summary>
        /// 内嵌附件奖励明细（类型+目标id+数量；空=无奖励通知邮件），供领取前预览；领取仍以服务端裁决为准
        /// </summary>
        [ProtoMember(8)]
        public List<MailRewardItem> Rewards { get; set; } = new List<MailRewardItem>();
    }
    /// <summary>
    /// 客户端拉邮件列表请求（无业务字段:身份从会话取,不携带账号;触发时机由客户端定）（§3.1）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_MailListRequest : AMessage, IRequest
    {
        public static C2G_MailListRequest Create(bool autoReturn = true)
        {
            var c2G_MailListRequest = MessageObjectPool<C2G_MailListRequest>.Rent();
            c2G_MailListRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_MailListRequest.SetIsPool(false);
            }
            
            return c2G_MailListRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_MailListRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_MailListRequest; } 
        [ProtoIgnore]
        public G2C_MailListResponse ResponseType { get; set; }
    }
    /// <summary>
    /// 服务端拉列表响应（该账号应收、未过期的邮件 + 每封领取态）（§3.2）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_MailListResponse : AMessage, IResponse
    {
        public static G2C_MailListResponse Create(bool autoReturn = true)
        {
            var g2C_MailListResponse = MessageObjectPool<G2C_MailListResponse>.Rent();
            g2C_MailListResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_MailListResponse.SetIsPool(false);
            }
            
            return g2C_MailListResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            foreach (var __t in Mails) __t.Dispose();
            Mails.Clear();
            MessageObjectPool<G2C_MailListResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_MailListResponse; } 
        [ProtoMember(3)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 拉列表只有 Success / ServiceUnavailable 两种
        /// </summary>
        [ProtoMember(1)]
        public MailClaimResultCode ResultCode { get; set; }
        /// <summary>
        /// 该账号应收（活跃广播 + 定向）且未过期的邮件；已过期不下发（服务端时钟已滤）
        /// </summary>
        [ProtoMember(2)]
        public List<MailListItem> Mails { get; set; } = new List<MailListItem>();
    }
    /// <summary>
    /// 客户端领取一封邮件请求（身份从会话取，不携带账号）（§3.3）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_MailClaimRequest : AMessage, IRequest
    {
        public static C2G_MailClaimRequest Create(bool autoReturn = true)
        {
            var c2G_MailClaimRequest = MessageObjectPool<C2G_MailClaimRequest>.Rent();
            c2G_MailClaimRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_MailClaimRequest.SetIsPool(false);
            }
            
            return c2G_MailClaimRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MailId = default;
            MessageObjectPool<C2G_MailClaimRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_MailClaimRequest; } 
        [ProtoIgnore]
        public G2C_MailClaimResponse ResponseType { get; set; }
        /// <summary>
        /// 要领取的那封邮件标识（来自拉列表响应）；定位不到按「邮件不存在」返
        /// </summary>
        [ProtoMember(1)]
        public string MailId { get; set; }
    }
    /// <summary>
    /// 服务端领取裁决响应（结果码 + 成功时奖励列表）（§3.4）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_MailClaimResponse : AMessage, IResponse
    {
        public static G2C_MailClaimResponse Create(bool autoReturn = true)
        {
            var g2C_MailClaimResponse = MessageObjectPool<G2C_MailClaimResponse>.Rent();
            g2C_MailClaimResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_MailClaimResponse.SetIsPool(false);
            }
            
            return g2C_MailClaimResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            foreach (var __t in Rewards) __t.Dispose();
            Rewards.Clear();
            MessageObjectPool<G2C_MailClaimResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_MailClaimResponse; } 
        [ProtoMember(3)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public MailClaimResultCode ResultCode { get; set; }
        /// <summary>
        /// 仅 ResultCode=Success 时非空：该邮件内嵌附件全部发放的明细（类型 + 目标 id + 数量），供客户端展示
        /// </summary>
        [ProtoMember(2)]
        public List<MailRewardItem> Rewards { get; set; } = new List<MailRewardItem>();
    }
    /// <summary>
    /// 服务端主动通知：该账号有新的定向邮件到达（SendMailTo 投递成功且目标在线时推送）。
    /// 纯信号——不携带邮件数据；客户端收到后拉列表刷新收件箱 / 红点（红点仍基于服务端权威列表重算，不据推送内容判定）。
    /// 离线不推（下次登录拉列表照常）；单向推送、丢失不重试（同 delta-push O6）。广播运营邮件不走本推送（保持拉取，避免全量扇出）。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_MailNotify : AMessage, IMessage
    {
        public static G2C_MailNotify Create(bool autoReturn = true)
        {
            var g2C_MailNotify = MessageObjectPool<G2C_MailNotify>.Rent();
            g2C_MailNotify.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_MailNotify.SetIsPool(false);
            }
            
            return g2C_MailNotify;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<G2C_MailNotify>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_MailNotify; } 
    }
    /// <summary>
    /// 单条订单项(= 客户端 GameLogic.BlockBlast.Order;Type 用 int32 与客户端 MergeElement 枚举 1..4 对齐,0=None=空槽)
    /// 奖励字段是服务端按 TbMergeOrder 表派生的展示投影:客户端只显示、不参与发奖计算(交付实发以响应/推送为准)。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class OrderItem : AMessage, IDisposable
    {
        public static OrderItem Create(bool autoReturn = true)
        {
            var orderItem = MessageObjectPool<OrderItem>.Rent();
            orderItem.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                orderItem.SetIsPool(false);
            }
            
            return orderItem;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Type = default;
            Level = default;
            Count = default;
            EnergyReward = default;
            PietyReward = default;
            MessageObjectPool<OrderItem>.Return(this);
        }
        /// <summary>
        /// MergeElement: 0=None(空槽,交付后保留到刷新),1=Butterfly,2=Chalice,3=Scroll,4=Star
        /// </summary>
        [ProtoMember(1)]
        public int Type { get; set; }
        /// <summary>
        /// 等级(1..5,MergeOrderConfig.MaxLevel)
        /// </summary>
        [ProtoMember(2)]
        public int Level { get; set; }
        /// <summary>
        /// 数量(订单要求该 (Type,Level) 的件数)
        /// </summary>
        [ProtoMember(3)]
        public int Count { get; set; }
        /// <summary>
        /// 交付奖励体力(展示用;空槽为 0)
        /// </summary>
        [ProtoMember(4)]
        public int EnergyReward { get; set; }
        /// <summary>
        /// 交付奖励虔诚币(展示用;空槽为 0)
        /// </summary>
        [ProtoMember(5)]
        public long PietyReward { get; set; }
    }
    /// <summary>
    /// 订单系统当前状态快照(服务端权威,客户端只持投影)。
    /// 登录后随玩家信息推送 + 整批刷新后推送 + 每次交付响应附带,客户端整份覆盖本地视图。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class MergeOrderSnapshot : AMessage, IDisposable
    {
        public static MergeOrderSnapshot Create(bool autoReturn = true)
        {
            var mergeOrderSnapshot = MessageObjectPool<MergeOrderSnapshot>.Rent();
            mergeOrderSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                mergeOrderSnapshot.SetIsPool(false);
            }
            
            return mergeOrderSnapshot;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            foreach (var __t in ActiveOrders) __t.Dispose();
            ActiveOrders.Clear();
            OrderCursor = default;
            LastOrderRefreshMs = default;
            OrderRefreshIntervalSec = default;
            MessageObjectPool<MergeOrderSnapshot>.Return(this);
        }
        /// <summary>
        /// 当前激活订单(长度 = ActiveOrders 配置=3;空槽以 Type=0 占位,槽位次序固定)
        /// </summary>
        [ProtoMember(1)]
        public List<OrderItem> ActiveOrders { get; set; } = new List<OrderItem>();
        /// <summary>
        /// 订单池游标(下一张未取的索引,服务端按池长取模;客户端可选地用于校验/排错)
        /// </summary>
        [ProtoMember(2)]
        public int OrderCursor { get; set; }
        /// <summary>
        /// 上次整批刷新时刻(Unix 毫秒,服务端权威时钟;客户端可据此 + 间隔显示倒计时)
        /// </summary>
        [ProtoMember(3)]
        public long LastOrderRefreshMs { get; set; }
        /// <summary>
        /// 整批刷新间隔秒数(客户端读这个值算下次刷新时刻,不再读本地 GlobalConfigMgr)
        /// </summary>
        [ProtoMember(4)]
        public int OrderRefreshIntervalSec { get; set; }
    }
    /// <summary>
    /// 客户端请求交付某槽位的订单(身份从会话取,不带账号 / 不带订单类型 / 不带奖励金额——服务端按 OrderCursor + DeliveredMask 自己定)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_DeliverOrderRequest : AMessage, IRequest
    {
        public static C2G_DeliverOrderRequest Create(bool autoReturn = true)
        {
            var c2G_DeliverOrderRequest = MessageObjectPool<C2G_DeliverOrderRequest>.Rent();
            c2G_DeliverOrderRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_DeliverOrderRequest.SetIsPool(false);
            }
            
            return c2G_DeliverOrderRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Slot = default;
            MessageObjectPool<C2G_DeliverOrderRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_DeliverOrderRequest; } 
        [ProtoIgnore]
        public G2C_DeliverOrderResponse ResponseType { get; set; }
        /// <summary>
        /// 槽位下标 [0, ActiveOrders),客户端按显示槽位填
        /// </summary>
        [ProtoMember(1)]
        public int Slot { get; set; }
    }
    /// <summary>
    /// 服务端交付裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_DeliverOrderResponse : AMessage, IResponse
    {
        public static G2C_DeliverOrderResponse Create(bool autoReturn = true)
        {
            var g2C_DeliverOrderResponse = MessageObjectPool<G2C_DeliverOrderResponse>.Rent();
            g2C_DeliverOrderResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_DeliverOrderResponse.SetIsPool(false);
            }
            
            return g2C_DeliverOrderResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            Slot = default;
            EnergyReward = default;
            PietyReward = default;
            if (Snapshot != null)
            {
                Snapshot.Dispose();
                Snapshot = null;
            }
            EnergyBalance = default;
            PietyBalance = default;
            MessageObjectPool<G2C_DeliverOrderResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_DeliverOrderResponse; } 
        [ProtoMember(8)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public DeliverOrderResultCode ResultCode { get; set; }
        /// <summary>
        /// 回声槽位
        /// </summary>
        [ProtoMember(2)]
        public int Slot { get; set; }
        /// <summary>
        /// 成功时:本次发奖体力实际净增量(= TbMergeOrder 该行配值;撞服务端硬顶被整笔拒时 = 0);失败时 = 0
        /// </summary>
        [ProtoMember(3)]
        public long EnergyReward { get; set; }
        /// <summary>
        /// 成功时:本次发奖虔诚币(= TbMergeOrder 该行配值);失败时 = 0
        /// </summary>
        [ProtoMember(4)]
        public long PietyReward { get; set; }
        /// <summary>
        /// 服务端权威更新后的最新订单快照(含已置空的本槽 + 可能触发的整批刷新);失败时为空数组的占位 snapshot
        /// </summary>
        [ProtoMember(5)]
        public MergeOrderSnapshot Snapshot { get; set; }
        /// <summary>
        /// 交付后 Energy 权威绝对余额:客户端据此对账 set 本地体力(免依赖 delta-push);-1 = 哨兵(本次未取到权威值,客户端不 set)
        /// </summary>
        [ProtoMember(6)]
        public long EnergyBalance { get; set; }
        /// <summary>
        /// 交付后 Piety 权威绝对余额(同 EnergyBalance 口径)
        /// </summary>
        [ProtoMember(7)]
        public long PietyBalance { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2G_TestEmptyMessage : AMessage, IMessage
    {
        public static C2G_TestEmptyMessage Create(bool autoReturn = true)
        {
            var c2G_TestEmptyMessage = MessageObjectPool<C2G_TestEmptyMessage>.Rent();
            c2G_TestEmptyMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_TestEmptyMessage.SetIsPool(false);
            }
            
            return c2G_TestEmptyMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_TestEmptyMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_TestEmptyMessage; } 
    }
    [Serializable]
    [ProtoContract]
    public partial class C2G_TestMessage : AMessage, IMessage
    {
        public static C2G_TestMessage Create(bool autoReturn = true)
        {
            var c2G_TestMessage = MessageObjectPool<C2G_TestMessage>.Rent();
            c2G_TestMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_TestMessage.SetIsPool(false);
            }
            
            return c2G_TestMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2G_TestMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_TestMessage; } 
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2G_TestRequest : AMessage, IRequest
    {
        public static C2G_TestRequest Create(bool autoReturn = true)
        {
            var c2G_TestRequest = MessageObjectPool<C2G_TestRequest>.Rent();
            c2G_TestRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_TestRequest.SetIsPool(false);
            }
            
            return c2G_TestRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            Data = null;
            MessageObjectPool<C2G_TestRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_TestRequest; } 
        [ProtoIgnore]
        public G2C_TestResponse ResponseType { get; set; }
        [ProtoMember(1)]
        public string Tag { get; set; }
        [ProtoMember(2)]
        public List<byte> Data { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_TestResponse : AMessage, IResponse
    {
        public static G2C_TestResponse Create(bool autoReturn = true)
        {
            var g2C_TestResponse = MessageObjectPool<G2C_TestResponse>.Rent();
            g2C_TestResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_TestResponse.SetIsPool(false);
            }
            
            return g2C_TestResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            Tag = default;
            Data = null;
            Lists = null;
            MessageObjectPool<G2C_TestResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_TestResponse; } 
        [ProtoMember(4)]
        public uint ErrorCode { get; set; }
        [ProtoMember(1)]
        public string Tag { get; set; }
        [ProtoMember(2)]
        public byte[] Data { get; set; }
        [ProtoMember(3)]
        public List<int> Lists { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2G_TestRequestPushMessage : AMessage, IMessage
    {
        public static C2G_TestRequestPushMessage Create(bool autoReturn = true)
        {
            var c2G_TestRequestPushMessage = MessageObjectPool<C2G_TestRequestPushMessage>.Rent();
            c2G_TestRequestPushMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_TestRequestPushMessage.SetIsPool(false);
            }
            
            return c2G_TestRequestPushMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_TestRequestPushMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_TestRequestPushMessage; } 
    }
    /// <summary>
    /// Gate服务器推送一个消息给客户端
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_PushMessage : AMessage, IMessage
    {
        public static G2C_PushMessage Create(bool autoReturn = true)
        {
            var g2C_PushMessage = MessageObjectPool<G2C_PushMessage>.Rent();
            g2C_PushMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_PushMessage.SetIsPool(false);
            }
            
            return g2C_PushMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<G2C_PushMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_PushMessage; } 
        /// <summary>
        /// 标记
        /// </summary>
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2G_CreateAddressableRequest : AMessage, IRequest
    {
        public static C2G_CreateAddressableRequest Create(bool autoReturn = true)
        {
            var c2G_CreateAddressableRequest = MessageObjectPool<C2G_CreateAddressableRequest>.Rent();
            c2G_CreateAddressableRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_CreateAddressableRequest.SetIsPool(false);
            }
            
            return c2G_CreateAddressableRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_CreateAddressableRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_CreateAddressableRequest; } 
        [ProtoIgnore]
        public G2C_CreateAddressableResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_CreateAddressableResponse : AMessage, IResponse
    {
        public static G2C_CreateAddressableResponse Create(bool autoReturn = true)
        {
            var g2C_CreateAddressableResponse = MessageObjectPool<G2C_CreateAddressableResponse>.Rent();
            g2C_CreateAddressableResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_CreateAddressableResponse.SetIsPool(false);
            }
            
            return g2C_CreateAddressableResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<G2C_CreateAddressableResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_CreateAddressableResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2M_TestMessage : AMessage, IAddressableMessage
    {
        public static C2M_TestMessage Create(bool autoReturn = true)
        {
            var c2M_TestMessage = MessageObjectPool<C2M_TestMessage>.Rent();
            c2M_TestMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2M_TestMessage.SetIsPool(false);
            }
            
            return c2M_TestMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2M_TestMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2M_TestMessage; } 
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2M_TestRequest : AMessage, IAddressableRequest
    {
        public static C2M_TestRequest Create(bool autoReturn = true)
        {
            var c2M_TestRequest = MessageObjectPool<C2M_TestRequest>.Rent();
            c2M_TestRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2M_TestRequest.SetIsPool(false);
            }
            
            return c2M_TestRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2M_TestRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2M_TestRequest; } 
        [ProtoIgnore]
        public M2C_TestResponse ResponseType { get; set; }
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class M2C_TestResponse : AMessage, IAddressableResponse
    {
        public static M2C_TestResponse Create(bool autoReturn = true)
        {
            var m2C_TestResponse = MessageObjectPool<M2C_TestResponse>.Rent();
            m2C_TestResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                m2C_TestResponse.SetIsPool(false);
            }
            
            return m2C_TestResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            Tag = default;
            MessageObjectPool<M2C_TestResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.M2C_TestResponse; } 
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 通知Gate服务器创建一个Chat的Route连接
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_CreateChatRouteRequest : AMessage, IRequest
    {
        public static C2G_CreateChatRouteRequest Create(bool autoReturn = true)
        {
            var c2G_CreateChatRouteRequest = MessageObjectPool<C2G_CreateChatRouteRequest>.Rent();
            c2G_CreateChatRouteRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_CreateChatRouteRequest.SetIsPool(false);
            }
            
            return c2G_CreateChatRouteRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_CreateChatRouteRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_CreateChatRouteRequest; } 
        [ProtoIgnore]
        public G2C_CreateChatRouteResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_CreateChatRouteResponse : AMessage, IResponse
    {
        public static G2C_CreateChatRouteResponse Create(bool autoReturn = true)
        {
            var g2C_CreateChatRouteResponse = MessageObjectPool<G2C_CreateChatRouteResponse>.Rent();
            g2C_CreateChatRouteResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_CreateChatRouteResponse.SetIsPool(false);
            }
            
            return g2C_CreateChatRouteResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<G2C_CreateChatRouteResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_CreateChatRouteResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 发送一个Route消息给Chat
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2Chat_TestMessage : AMessage, ICustomRouteMessage
    {
        public static C2Chat_TestMessage Create(bool autoReturn = true)
        {
            var c2Chat_TestMessage = MessageObjectPool<C2Chat_TestMessage>.Rent();
            c2Chat_TestMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2Chat_TestMessage.SetIsPool(false);
            }
            
            return c2Chat_TestMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2Chat_TestMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2Chat_TestMessage; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RouteType.ChatRoute;
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 发送一个RPCRoute消息给Chat
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2Chat_TestMessageRequest : AMessage, ICustomRouteRequest
    {
        public static C2Chat_TestMessageRequest Create(bool autoReturn = true)
        {
            var c2Chat_TestMessageRequest = MessageObjectPool<C2Chat_TestMessageRequest>.Rent();
            c2Chat_TestMessageRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2Chat_TestMessageRequest.SetIsPool(false);
            }
            
            return c2Chat_TestMessageRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2Chat_TestMessageRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2Chat_TestMessageRequest; } 
        [ProtoIgnore]
        public Chat2C_TestMessageResponse ResponseType { get; set; }
        [ProtoIgnore]
        public int RouteType => Fantasy.RouteType.ChatRoute;
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class Chat2C_TestMessageResponse : AMessage, ICustomRouteResponse
    {
        public static Chat2C_TestMessageResponse Create(bool autoReturn = true)
        {
            var chat2C_TestMessageResponse = MessageObjectPool<Chat2C_TestMessageResponse>.Rent();
            chat2C_TestMessageResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                chat2C_TestMessageResponse.SetIsPool(false);
            }
            
            return chat2C_TestMessageResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            Tag = default;
            MessageObjectPool<Chat2C_TestMessageResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.Chat2C_TestMessageResponse; } 
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 发送一个RPC消息给Map，让Map里的Entity转移到另外一个Map上
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2M_MoveToMapRequest : AMessage, IAddressableRequest
    {
        public static C2M_MoveToMapRequest Create(bool autoReturn = true)
        {
            var c2M_MoveToMapRequest = MessageObjectPool<C2M_MoveToMapRequest>.Rent();
            c2M_MoveToMapRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2M_MoveToMapRequest.SetIsPool(false);
            }
            
            return c2M_MoveToMapRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2M_MoveToMapRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2M_MoveToMapRequest; } 
        [ProtoIgnore]
        public M2C_MoveToMapResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class M2C_MoveToMapResponse : AMessage, IAddressableResponse
    {
        public static M2C_MoveToMapResponse Create(bool autoReturn = true)
        {
            var m2C_MoveToMapResponse = MessageObjectPool<M2C_MoveToMapResponse>.Rent();
            m2C_MoveToMapResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                m2C_MoveToMapResponse.SetIsPool(false);
            }
            
            return m2C_MoveToMapResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<M2C_MoveToMapResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.M2C_MoveToMapResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 发送一个消息给Gate，让Gate发送一个Addressable消息给MAP
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_SendAddressableToMap : AMessage, IMessage
    {
        public static C2G_SendAddressableToMap Create(bool autoReturn = true)
        {
            var c2G_SendAddressableToMap = MessageObjectPool<C2G_SendAddressableToMap>.Rent();
            c2G_SendAddressableToMap.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_SendAddressableToMap.SetIsPool(false);
            }
            
            return c2G_SendAddressableToMap;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2G_SendAddressableToMap>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_SendAddressableToMap; } 
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 发送一个消息给Chat，让Chat服务器主动推送一个RouteMessage消息给客户端
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2Chat_TestRequestPushMessage : AMessage, ICustomRouteMessage
    {
        public static C2Chat_TestRequestPushMessage Create(bool autoReturn = true)
        {
            var c2Chat_TestRequestPushMessage = MessageObjectPool<C2Chat_TestRequestPushMessage>.Rent();
            c2Chat_TestRequestPushMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2Chat_TestRequestPushMessage.SetIsPool(false);
            }
            
            return c2Chat_TestRequestPushMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2Chat_TestRequestPushMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2Chat_TestRequestPushMessage; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RouteType.ChatRoute;
    }
    /// <summary>
    /// Chat服务器主动推送一个消息给客户端
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class Chat2C_PushMessage : AMessage, ICustomRouteMessage
    {
        public static Chat2C_PushMessage Create(bool autoReturn = true)
        {
            var chat2C_PushMessage = MessageObjectPool<Chat2C_PushMessage>.Rent();
            chat2C_PushMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                chat2C_PushMessage.SetIsPool(false);
            }
            
            return chat2C_PushMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<Chat2C_PushMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.Chat2C_PushMessage; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RouteType.ChatRoute;
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 客户端发送给Gate服务器通知map服务器创建一个SubScene
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_CreateSubSceneRequest : AMessage, IRequest
    {
        public static C2G_CreateSubSceneRequest Create(bool autoReturn = true)
        {
            var c2G_CreateSubSceneRequest = MessageObjectPool<C2G_CreateSubSceneRequest>.Rent();
            c2G_CreateSubSceneRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_CreateSubSceneRequest.SetIsPool(false);
            }
            
            return c2G_CreateSubSceneRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_CreateSubSceneRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_CreateSubSceneRequest; } 
        [ProtoIgnore]
        public G2C_CreateSubSceneResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_CreateSubSceneResponse : AMessage, IResponse
    {
        public static G2C_CreateSubSceneResponse Create(bool autoReturn = true)
        {
            var g2C_CreateSubSceneResponse = MessageObjectPool<G2C_CreateSubSceneResponse>.Rent();
            g2C_CreateSubSceneResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_CreateSubSceneResponse.SetIsPool(false);
            }
            
            return g2C_CreateSubSceneResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<G2C_CreateSubSceneResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_CreateSubSceneResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 客户端通知Gate服务器给SubScene发送一个消息
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_SendToSubSceneMessage : AMessage, IMessage
    {
        public static C2G_SendToSubSceneMessage Create(bool autoReturn = true)
        {
            var c2G_SendToSubSceneMessage = MessageObjectPool<C2G_SendToSubSceneMessage>.Rent();
            c2G_SendToSubSceneMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_SendToSubSceneMessage.SetIsPool(false);
            }
            
            return c2G_SendToSubSceneMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_SendToSubSceneMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_SendToSubSceneMessage; } 
    }
    /// <summary>
    /// 客户端通知Gate服务器创建一个SubScene的Address消息
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_CreateSubSceneAddressableRequest : AMessage, IRequest
    {
        public static C2G_CreateSubSceneAddressableRequest Create(bool autoReturn = true)
        {
            var c2G_CreateSubSceneAddressableRequest = MessageObjectPool<C2G_CreateSubSceneAddressableRequest>.Rent();
            c2G_CreateSubSceneAddressableRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_CreateSubSceneAddressableRequest.SetIsPool(false);
            }
            
            return c2G_CreateSubSceneAddressableRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_CreateSubSceneAddressableRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_CreateSubSceneAddressableRequest; } 
        [ProtoIgnore]
        public G2C_CreateSubSceneAddressableResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_CreateSubSceneAddressableResponse : AMessage, IResponse
    {
        public static G2C_CreateSubSceneAddressableResponse Create(bool autoReturn = true)
        {
            var g2C_CreateSubSceneAddressableResponse = MessageObjectPool<G2C_CreateSubSceneAddressableResponse>.Rent();
            g2C_CreateSubSceneAddressableResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_CreateSubSceneAddressableResponse.SetIsPool(false);
            }
            
            return g2C_CreateSubSceneAddressableResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<G2C_CreateSubSceneAddressableResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_CreateSubSceneAddressableResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 客户端向SubScene发送一个测试消息
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2SubScene_TestMessage : AMessage, IAddressableMessage
    {
        public static C2SubScene_TestMessage Create(bool autoReturn = true)
        {
            var c2SubScene_TestMessage = MessageObjectPool<C2SubScene_TestMessage>.Rent();
            c2SubScene_TestMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2SubScene_TestMessage.SetIsPool(false);
            }
            
            return c2SubScene_TestMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2SubScene_TestMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2SubScene_TestMessage; } 
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 客户端向SubScene发送一个销毁测试消息
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2SubScene_TestDisposeMessage : AMessage, IAddressableMessage
    {
        public static C2SubScene_TestDisposeMessage Create(bool autoReturn = true)
        {
            var c2SubScene_TestDisposeMessage = MessageObjectPool<C2SubScene_TestDisposeMessage>.Rent();
            c2SubScene_TestDisposeMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2SubScene_TestDisposeMessage.SetIsPool(false);
            }
            
            return c2SubScene_TestDisposeMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2SubScene_TestDisposeMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2SubScene_TestDisposeMessage; } 
    }
    /// <summary>
    /// 客户端向服务器发送连接消息（Roaming）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_ConnectRoamingRequest : AMessage, IRequest
    {
        public static C2G_ConnectRoamingRequest Create(bool autoReturn = true)
        {
            var c2G_ConnectRoamingRequest = MessageObjectPool<C2G_ConnectRoamingRequest>.Rent();
            c2G_ConnectRoamingRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_ConnectRoamingRequest.SetIsPool(false);
            }
            
            return c2G_ConnectRoamingRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_ConnectRoamingRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_ConnectRoamingRequest; } 
        [ProtoIgnore]
        public G2C_ConnectRoamingResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_ConnectRoamingResponse : AMessage, IResponse
    {
        public static G2C_ConnectRoamingResponse Create(bool autoReturn = true)
        {
            var g2C_ConnectRoamingResponse = MessageObjectPool<G2C_ConnectRoamingResponse>.Rent();
            g2C_ConnectRoamingResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_ConnectRoamingResponse.SetIsPool(false);
            }
            
            return g2C_ConnectRoamingResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<G2C_ConnectRoamingResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_ConnectRoamingResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 测试一个Chat漫游普通消息
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2Chat_TestRoamingMessage : AMessage, IRoamingMessage
    {
        public static C2Chat_TestRoamingMessage Create(bool autoReturn = true)
        {
            var c2Chat_TestRoamingMessage = MessageObjectPool<C2Chat_TestRoamingMessage>.Rent();
            c2Chat_TestRoamingMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2Chat_TestRoamingMessage.SetIsPool(false);
            }
            
            return c2Chat_TestRoamingMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2Chat_TestRoamingMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2Chat_TestRoamingMessage; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.ChatRoamingType;
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 测试一个Map漫游普通消息
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2Map_TestRoamingMessage : AMessage, IRoamingMessage
    {
        public static C2Map_TestRoamingMessage Create(bool autoReturn = true)
        {
            var c2Map_TestRoamingMessage = MessageObjectPool<C2Map_TestRoamingMessage>.Rent();
            c2Map_TestRoamingMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2Map_TestRoamingMessage.SetIsPool(false);
            }
            
            return c2Map_TestRoamingMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2Map_TestRoamingMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2Map_TestRoamingMessage; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.MapRoamingType;
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 测试一个Chat漫游RPC消息
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2Chat_TestRPCRoamingRequest : AMessage, IRoamingRequest
    {
        public static C2Chat_TestRPCRoamingRequest Create(bool autoReturn = true)
        {
            var c2Chat_TestRPCRoamingRequest = MessageObjectPool<C2Chat_TestRPCRoamingRequest>.Rent();
            c2Chat_TestRPCRoamingRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2Chat_TestRPCRoamingRequest.SetIsPool(false);
            }
            
            return c2Chat_TestRPCRoamingRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2Chat_TestRPCRoamingRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2Chat_TestRPCRoamingRequest; } 
        [ProtoIgnore]
        public Chat2C_TestRPCRoamingResponse ResponseType { get; set; }
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.ChatRoamingType;
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class Chat2C_TestRPCRoamingResponse : AMessage, IRoamingResponse
    {
        public static Chat2C_TestRPCRoamingResponse Create(bool autoReturn = true)
        {
            var chat2C_TestRPCRoamingResponse = MessageObjectPool<Chat2C_TestRPCRoamingResponse>.Rent();
            chat2C_TestRPCRoamingResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                chat2C_TestRPCRoamingResponse.SetIsPool(false);
            }
            
            return chat2C_TestRPCRoamingResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<Chat2C_TestRPCRoamingResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.Chat2C_TestRPCRoamingResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 客户端发送一个漫游消息给Map通知Map主动推送一个消息给客户端
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2Map_PushMessageToClient : AMessage, IRoamingMessage
    {
        public static C2Map_PushMessageToClient Create(bool autoReturn = true)
        {
            var c2Map_PushMessageToClient = MessageObjectPool<C2Map_PushMessageToClient>.Rent();
            c2Map_PushMessageToClient.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2Map_PushMessageToClient.SetIsPool(false);
            }
            
            return c2Map_PushMessageToClient;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2Map_PushMessageToClient>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2Map_PushMessageToClient; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.MapRoamingType;
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 漫游端发送一个消息给客户端
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class Map2C_PushMessageToClient : AMessage, IRoamingMessage
    {
        public static Map2C_PushMessageToClient Create(bool autoReturn = true)
        {
            var map2C_PushMessageToClient = MessageObjectPool<Map2C_PushMessageToClient>.Rent();
            map2C_PushMessageToClient.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                map2C_PushMessageToClient.SetIsPool(false);
            }
            
            return map2C_PushMessageToClient;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<Map2C_PushMessageToClient>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.Map2C_PushMessageToClient; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.MapRoamingType;
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 测试传送漫游的触发协议
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2Map_TestTransferRequest : AMessage, IRoamingRequest
    {
        public static C2Map_TestTransferRequest Create(bool autoReturn = true)
        {
            var c2Map_TestTransferRequest = MessageObjectPool<C2Map_TestTransferRequest>.Rent();
            c2Map_TestTransferRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2Map_TestTransferRequest.SetIsPool(false);
            }
            
            return c2Map_TestTransferRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2Map_TestTransferRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2Map_TestTransferRequest; } 
        [ProtoIgnore]
        public Map2C_TestTransferResponse ResponseType { get; set; }
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.MapRoamingType;
    }
    [Serializable]
    [ProtoContract]
    public partial class Map2C_TestTransferResponse : AMessage, IRoamingResponse
    {
        public static Map2C_TestTransferResponse Create(bool autoReturn = true)
        {
            var map2C_TestTransferResponse = MessageObjectPool<Map2C_TestTransferResponse>.Rent();
            map2C_TestTransferResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                map2C_TestTransferResponse.SetIsPool(false);
            }
            
            return map2C_TestTransferResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<Map2C_TestTransferResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.Map2C_TestTransferResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 测试一个Chat发送到Map之间漫游协议
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2Chat_TestSendMapMessage : AMessage, IRoamingMessage
    {
        public static C2Chat_TestSendMapMessage Create(bool autoReturn = true)
        {
            var c2Chat_TestSendMapMessage = MessageObjectPool<C2Chat_TestSendMapMessage>.Rent();
            c2Chat_TestSendMapMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2Chat_TestSendMapMessage.SetIsPool(false);
            }
            
            return c2Chat_TestSendMapMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2Chat_TestSendMapMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2Chat_TestSendMapMessage; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.ChatRoamingType;
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 通知Gate服务器发送一个Route消息给Map的漫游终端
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_TestRouteToRoaming : AMessage, IMessage
    {
        public static C2G_TestRouteToRoaming Create(bool autoReturn = true)
        {
            var c2G_TestRouteToRoaming = MessageObjectPool<C2G_TestRouteToRoaming>.Rent();
            c2G_TestRouteToRoaming.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_TestRouteToRoaming.SetIsPool(false);
            }
            
            return c2G_TestRouteToRoaming;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2G_TestRouteToRoaming>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_TestRouteToRoaming; } 
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 通知Gate服务器发送一个漫游消息给Map的漫游终端
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_TestRoamingToRoaming : AMessage, IMessage
    {
        public static C2G_TestRoamingToRoaming Create(bool autoReturn = true)
        {
            var c2G_TestRoamingToRoaming = MessageObjectPool<C2G_TestRoamingToRoaming>.Rent();
            c2G_TestRoamingToRoaming.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_TestRoamingToRoaming.SetIsPool(false);
            }
            
            return c2G_TestRoamingToRoaming;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Tag = default;
            MessageObjectPool<C2G_TestRoamingToRoaming>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_TestRoamingToRoaming; } 
        [ProtoMember(1)]
        public string Tag { get; set; }
    }
    /// <summary>
    /// 客户端向服务器发送登录连接消息（Roaming）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_LoginRoamingRequest : AMessage, IRequest
    {
        public static C2G_LoginRoamingRequest Create(bool autoReturn = true)
        {
            var c2G_LoginRoamingRequest = MessageObjectPool<C2G_LoginRoamingRequest>.Rent();
            c2G_LoginRoamingRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_LoginRoamingRequest.SetIsPool(false);
            }
            
            return c2G_LoginRoamingRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_LoginRoamingRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_LoginRoamingRequest; } 
        [ProtoIgnore]
        public G2C_ConnectRoamingResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_LoginRoamingResponse : AMessage, IResponse
    {
        public static G2C_LoginRoamingResponse Create(bool autoReturn = true)
        {
            var g2C_LoginRoamingResponse = MessageObjectPool<G2C_LoginRoamingResponse>.Rent();
            g2C_LoginRoamingResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_LoginRoamingResponse.SetIsPool(false);
            }
            
            return g2C_LoginRoamingResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<G2C_LoginRoamingResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_LoginRoamingResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 通知Gate服务器发送一个内网消息通知Map服务器向Gate服务器注册一个领域事件
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_SubscribeSphereEventRequest : AMessage, IRequest
    {
        public static C2G_SubscribeSphereEventRequest Create(bool autoReturn = true)
        {
            var c2G_SubscribeSphereEventRequest = MessageObjectPool<C2G_SubscribeSphereEventRequest>.Rent();
            c2G_SubscribeSphereEventRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_SubscribeSphereEventRequest.SetIsPool(false);
            }
            
            return c2G_SubscribeSphereEventRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_SubscribeSphereEventRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_SubscribeSphereEventRequest; } 
        [ProtoIgnore]
        public G2C_SubscribeSphereEventResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_SubscribeSphereEventResponse : AMessage, IResponse
    {
        public static G2C_SubscribeSphereEventResponse Create(bool autoReturn = true)
        {
            var g2C_SubscribeSphereEventResponse = MessageObjectPool<G2C_SubscribeSphereEventResponse>.Rent();
            g2C_SubscribeSphereEventResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_SubscribeSphereEventResponse.SetIsPool(false);
            }
            
            return g2C_SubscribeSphereEventResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<G2C_SubscribeSphereEventResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_SubscribeSphereEventResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 通知Gate发送一个订阅领域事件
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_PublishSphereEventRequest : AMessage, IRequest
    {
        public static C2G_PublishSphereEventRequest Create(bool autoReturn = true)
        {
            var c2G_PublishSphereEventRequest = MessageObjectPool<C2G_PublishSphereEventRequest>.Rent();
            c2G_PublishSphereEventRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_PublishSphereEventRequest.SetIsPool(false);
            }
            
            return c2G_PublishSphereEventRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_PublishSphereEventRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_PublishSphereEventRequest; } 
        [ProtoIgnore]
        public G2C_PublishSphereEventResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_PublishSphereEventResponse : AMessage, IResponse
    {
        public static G2C_PublishSphereEventResponse Create(bool autoReturn = true)
        {
            var g2C_PublishSphereEventResponse = MessageObjectPool<G2C_PublishSphereEventResponse>.Rent();
            g2C_PublishSphereEventResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_PublishSphereEventResponse.SetIsPool(false);
            }
            
            return g2C_PublishSphereEventResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<G2C_PublishSphereEventResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_PublishSphereEventResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 通知Gate取消一个订阅领域事件
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_UnsubscribeSphereEventRequest : AMessage, IRequest
    {
        public static C2G_UnsubscribeSphereEventRequest Create(bool autoReturn = true)
        {
            var c2G_UnsubscribeSphereEventRequest = MessageObjectPool<C2G_UnsubscribeSphereEventRequest>.Rent();
            c2G_UnsubscribeSphereEventRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_UnsubscribeSphereEventRequest.SetIsPool(false);
            }
            
            return c2G_UnsubscribeSphereEventRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_UnsubscribeSphereEventRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_UnsubscribeSphereEventRequest; } 
        [ProtoIgnore]
        public G2C_UnsubscribeSphereEventResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_UnsubscribeSphereEventResponse : AMessage, IResponse
    {
        public static G2C_UnsubscribeSphereEventResponse Create(bool autoReturn = true)
        {
            var g2C_UnsubscribeSphereEventResponse = MessageObjectPool<G2C_UnsubscribeSphereEventResponse>.Rent();
            g2C_UnsubscribeSphereEventResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_UnsubscribeSphereEventResponse.SetIsPool(false);
            }
            
            return g2C_UnsubscribeSphereEventResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<G2C_UnsubscribeSphereEventResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_UnsubscribeSphereEventResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 通知Map取消一个订阅领域事件
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_MapUnsubscribeSphereEventRequest : AMessage, IRequest
    {
        public static C2G_MapUnsubscribeSphereEventRequest Create(bool autoReturn = true)
        {
            var c2G_MapUnsubscribeSphereEventRequest = MessageObjectPool<C2G_MapUnsubscribeSphereEventRequest>.Rent();
            c2G_MapUnsubscribeSphereEventRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_MapUnsubscribeSphereEventRequest.SetIsPool(false);
            }
            
            return c2G_MapUnsubscribeSphereEventRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_MapUnsubscribeSphereEventRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_MapUnsubscribeSphereEventRequest; } 
        [ProtoIgnore]
        public G2C_MapUnsubscribeSphereEventResponse ResponseType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_MapUnsubscribeSphereEventResponse : AMessage, IResponse
    {
        public static G2C_MapUnsubscribeSphereEventResponse Create(bool autoReturn = true)
        {
            var g2C_MapUnsubscribeSphereEventResponse = MessageObjectPool<G2C_MapUnsubscribeSphereEventResponse>.Rent();
            g2C_MapUnsubscribeSphereEventResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_MapUnsubscribeSphereEventResponse.SetIsPool(false);
            }
            
            return g2C_MapUnsubscribeSphereEventResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            MessageObjectPool<G2C_MapUnsubscribeSphereEventResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_MapUnsubscribeSphereEventResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class ChatInfoData : AMessage, IDisposable
    {
        public static ChatInfoData Create(bool autoReturn = true)
        {
            var chatInfoData = MessageObjectPool<ChatInfoData>.Rent();
            chatInfoData.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                chatInfoData.SetIsPool(false);
            }
            
            return chatInfoData;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Data = null;
            MessageObjectPool<ChatInfoData>.Return(this);
        }
        [ProtoMember(1)]
        public byte[] Data { get; set; }
    }
    [Serializable]
    [MemoryPackable]
    public partial class TestMemoryPackInfo : AMessage, IDisposable
    {
        public static TestMemoryPackInfo Create(bool autoReturn = true)
        {
            var testMemoryPackInfo = MessageObjectPool<TestMemoryPackInfo>.Rent();
            testMemoryPackInfo.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                testMemoryPackInfo.SetIsPool(false);
            }
            
            return testMemoryPackInfo;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            A = default;
            MessageObjectPool<TestMemoryPackInfo>.Return(this);
        }
        [MemoryPackOrder(1)]
        public string A { get; set; }
    }
    [Serializable]
    [MemoryPackable]
    public partial class C2G_TestMemoryPackRequest : AMessage, IRequest
    {
        public static C2G_TestMemoryPackRequest Create(bool autoReturn = true)
        {
            var c2G_TestMemoryPackRequest = MessageObjectPool<C2G_TestMemoryPackRequest>.Rent();
            c2G_TestMemoryPackRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_TestMemoryPackRequest.SetIsPool(false);
            }
            
            return c2G_TestMemoryPackRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_TestMemoryPackRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_TestMemoryPackRequest; } 
        [MemoryPackIgnore]
        public G2C_TestMemoryPackResponse ResponseType { get; set; }
    }
    [Serializable]
    [MemoryPackable]
    public partial class G2C_TestMemoryPackResponse : AMessage, IResponse
    {
        public static G2C_TestMemoryPackResponse Create(bool autoReturn = true)
        {
            var g2C_TestMemoryPackResponse = MessageObjectPool<G2C_TestMemoryPackResponse>.Rent();
            g2C_TestMemoryPackResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_TestMemoryPackResponse.SetIsPool(false);
            }
            
            return g2C_TestMemoryPackResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            if (Info != null)
            {
                Info.Dispose();
                Info = null;
            }
            MessageObjectPool<G2C_TestMemoryPackResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_TestMemoryPackResponse; } 
        [MemoryPackOrder(2)]
        public uint ErrorCode { get; set; }
        [MemoryPackOrder(1)]
        public TestMemoryPackInfo Info { get; set; }
    }
    /// <summary>
    /// 玩家信息(基础档案 + 三数值属性快照),登录后整份下发
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class PlayerInfo : AMessage, IDisposable
    {
        public static PlayerInfo Create(bool autoReturn = true)
        {
            var playerInfo = MessageObjectPool<PlayerInfo>.Rent();
            playerInfo.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                playerInfo.SetIsPool(false);
            }
            
            return playerInfo;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            AccountId = default;
            Nickname = default;
            Level = default;
            Exp = default;
            foreach (var __t in Properties) __t.Dispose();
            Properties.Clear();
            SchemaVersion = default;
            RenameCount = default;
            CurrentAvatarId = default;
            CurrentFrameId = default;
            UnlockedAvatarIds.Clear();
            UnlockedFrameIds.Clear();
            WishUsedToday = default;
            WishDailyLimit = default;
            SkinMono = default;
            SkinMonoId = default;
            TempleDecorated = default;
            foreach (var __t in Holdings) __t.Dispose();
            Holdings.Clear();
            foreach (var __t in Lots) __t.Dispose();
            Lots.Clear();
            ServerNowMs = default;
            InventoryLoaded = default;
            LastUseReqSeq = default;
            AdEnergyUsedToday = default;
            AdEnergyDailyLimit = default;
            BuyEnergyUsedToday = default;
            BuyEnergyDailyLimit = default;
            EnergyOverflowPool = default;
            MessageObjectPool<PlayerInfo>.Return(this);
        }
        /// <summary>
        /// 账号 ID(UUID,= 登录账号名)
        /// </summary>
        [ProtoMember(1)]
        public string AccountId { get; set; }
        /// <summary>
        /// 昵称(首登默认空串,后续改名功能再填)
        /// </summary>
        [ProtoMember(2)]
        public string Nickname { get; set; }
        /// <summary>
        /// 等级(首登默认 1)
        /// </summary>
        [ProtoMember(3)]
        public int Level { get; set; }
        /// <summary>
        /// 经验(首登默认 0)
        /// </summary>
        [ProtoMember(4)]
        public long Exp { get; set; }
        /// <summary>
        /// 三数值属性当前余额(金币/钻石/体力,复用 PropertyAmount)
        /// </summary>
        [ProtoMember(5)]
        public List<PropertyAmount> Properties { get; set; } = new List<PropertyAmount>();
        /// <summary>
        /// schema 版本(加字段时升,客户端据此识别)
        /// </summary>
        [ProtoMember(6)]
        public int SchemaVersion { get; set; }
        /// <summary>
        /// 改名次数(服务端权威;客户端据此算下次改名费:0=首次免费)
        /// </summary>
        [ProtoMember(7)]
        public int RenameCount { get; set; }
        /// <summary>
        /// 当前佩戴头像 id(服务端权威;缺省与客户端 DefaultAvatarId=1 对齐)
        /// </summary>
        [ProtoMember(8)]
        public int CurrentAvatarId { get; set; }
        /// <summary>
        /// 当前佩戴头像框 id(服务端权威;缺省与客户端 DefaultFrameId=101 对齐)
        /// </summary>
        [ProtoMember(9)]
        public int CurrentFrameId { get; set; }
        /// <summary>
        /// 已解锁头像 id 集合(服务端权威;首登空,客户端 bootstrap 上报默认解锁后填入)
        /// </summary>
        [ProtoMember(10)]
        public List<int> UnlockedAvatarIds { get; set; } = new List<int>();
        /// <summary>
        /// 已解锁头像框 id 集合(服务端权威;同上)
        /// </summary>
        [ProtoMember(11)]
        public List<int> UnlockedFrameIds { get; set; } = new List<int>();
        /// <summary>
        /// 今日已用祈愿次数(服务端权威;快照前已跑懒每日重置,故为重置后当日值)
        /// </summary>
        [ProtoMember(12)]
        public int WishUsedToday { get; set; }
        /// <summary>
        /// 每日祈愿次数上限(= WishConfigServer.WishDailyLimit,供客户端算今日剩余)
        /// </summary>
        [ProtoMember(13)]
        public int WishDailyLimit { get; set; }
        /// <summary>
        /// 皮肤态服务端权威(3b):是否单色皮肤模式(0=彩色 / 1=单色;缺省 0=彩色)
        /// </summary>
        [ProtoMember(14)]
        public int SkinMono { get; set; }
        /// <summary>
        /// 皮肤态服务端权威(3b):当前单色皮肤 id(彩色态 -1;缺省 -1)
        /// </summary>
        [ProtoMember(15)]
        public int SkinMonoId { get; set; }
        /// <summary>
        /// 神庙装饰服务端权威(3b):已装饰厅数标量(前缀语义,= 已修厅数;缺省 0)
        /// </summary>
        [ProtoMember(16)]
        public long TempleDecorated { get; set; }
        /// <summary>
        /// 堆叠轨全量(itemId → 数量)
        /// </summary>
        [ProtoMember(17)]
        public List<InventoryHolding> Holdings { get; set; } = new List<InventoryHolding>();
        /// <summary>
        /// 批次轨全量(有有效期道具,已剔除过期批次)
        /// </summary>
        [ProtoMember(18)]
        public List<InventoryLot> Lots { get; set; } = new List<InventoryLot>();
        /// <summary>
        /// 服务端权威当前时刻(Unix 毫秒 UTC);客户端锚定倒计时基准,批次剩余 = ExpireMs - (ServerNowMs + 本地流逝)
        /// </summary>
        [ProtoMember(19)]
        public long ServerNowMs { get; set; }
        /// <summary>
        /// 背包权威整份标志(true=Holdings/Lots 为权威全量;false=服务端读库降级,客户端保留本地投影不清空)
        /// </summary>
        [ProtoMember(20)]
        public bool InventoryLoaded { get; set; }
        /// <summary>
        /// 使用事务幂等锚当前值(服务端持久);客户端登录 seed 本地 reqSeq 底,保重登后首个使用序号 > 服务端已处理值(防时钟回拨误判 Duplicate)
        /// </summary>
        [ProtoMember(21)]
        public long LastUseReqSeq { get; set; }
        /// <summary>
        /// 今日已用看广告领体力次数(服务端权威;快照前已跑懒每日重置,故为重置后当日值)
        /// </summary>
        [ProtoMember(22)]
        public int AdEnergyUsedToday { get; set; }
        /// <summary>
        /// 每日看广告领体力次数上限(= AdEnergyConfigServer.DailyMax,供客户端算今日剩余)
        /// </summary>
        [ProtoMember(23)]
        public int AdEnergyDailyLimit { get; set; }
        /// <summary>
        /// 今日已用钻石购买体力次数(服务端权威;快照前已跑懒每日重置,故为重置后当日值)
        /// </summary>
        [ProtoMember(24)]
        public int BuyEnergyUsedToday { get; set; }
        /// <summary>
        /// 每日钻石购买体力次数上限(= BuyEnergyConfigServer.DailyLimit,供客户端算今日剩余)
        /// </summary>
        [ProtoMember(25)]
        public int BuyEnergyDailyLimit { get; set; }
        /// <summary>
        /// 体力溢出储存池当前值(服务端权威;自然恢复超软上限的存量,玩家手动取用转回体力)
        /// </summary>
        [ProtoMember(26)]
        public long EnergyOverflowPool { get; set; }
    }
    /// <summary>
    /// 服务端登录后下发玩家信息整份快照(主动 push,取代 G2C_PropertyInitSnapshot)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_PlayerInfoSnapshot : AMessage, IMessage
    {
        public static G2C_PlayerInfoSnapshot Create(bool autoReturn = true)
        {
            var g2C_PlayerInfoSnapshot = MessageObjectPool<G2C_PlayerInfoSnapshot>.Rent();
            g2C_PlayerInfoSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_PlayerInfoSnapshot.SetIsPool(false);
            }
            
            return g2C_PlayerInfoSnapshot;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            if (Info != null)
            {
                Info.Dispose();
                Info = null;
            }
            MessageObjectPool<G2C_PlayerInfoSnapshot>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_PlayerInfoSnapshot; } 
        /// <summary>
        /// 玩家完整信息(基础档案 + 三属性)
        /// </summary>
        [ProtoMember(1)]
        public PlayerInfo Info { get; set; }
    }
    /// <summary>
    /// 单条属性余额项(初始快照下发用,可复用)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class PropertyAmount : AMessage, IDisposable
    {
        public static PropertyAmount Create(bool autoReturn = true)
        {
            var propertyAmount = MessageObjectPool<PropertyAmount>.Rent();
            propertyAmount.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                propertyAmount.SetIsPool(false);
            }
            
            return propertyAmount;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Type = default;
            Amount = default;
            MessageObjectPool<PropertyAmount>.Return(this);
        }
        /// <summary>
        /// 属性类型
        /// </summary>
        [ProtoMember(1)]
        public PropertyType Type { get; set; }
        /// <summary>
        /// 当前余额(服务端权威值)
        /// </summary>
        [ProtoMember(2)]
        public long Amount { get; set; }
    }
    /// <summary>
    /// 客户端发起属性变更声明请求(只声明相对增量 + 原因,身份从会话取)(§3.3.2)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_PropertyChangeRequest : AMessage, IRequest
    {
        public static C2G_PropertyChangeRequest Create(bool autoReturn = true)
        {
            var c2G_PropertyChangeRequest = MessageObjectPool<C2G_PropertyChangeRequest>.Rent();
            c2G_PropertyChangeRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_PropertyChangeRequest.SetIsPool(false);
            }
            
            return c2G_PropertyChangeRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Type = default;
            Delta = default;
            Reason = default;
            MessageObjectPool<C2G_PropertyChangeRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_PropertyChangeRequest; } 
        [ProtoIgnore]
        public G2C_PropertyChangeResponse ResponseType { get; set; }
        /// <summary>
        /// 要变更的属性类型
        /// </summary>
        [ProtoMember(1)]
        public PropertyType Type { get; set; }
        /// <summary>
        /// 有符号增量(正 = 增加 / 负 = 减少 / 消费;变长编码下 long 体积可接受,不强求 sint64 zigzag,Fantasy 导出工具不识别 sint64)
        /// </summary>
        [ProtoMember(2)]
        public long Delta { get; set; }
        /// <summary>
        /// 变更来源标识(如 "shop_item_123" / "mail_claim_456" / "stamina_consume_level_789",供后续 ledger 审计)
        /// </summary>
        [ProtoMember(3)]
        public string Reason { get; set; }
    }
    /// <summary>
    /// 服务端属性变更裁决响应(§3.3.2)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_PropertyChangeResponse : AMessage, IResponse
    {
        public static G2C_PropertyChangeResponse Create(bool autoReturn = true)
        {
            var g2C_PropertyChangeResponse = MessageObjectPool<G2C_PropertyChangeResponse>.Rent();
            g2C_PropertyChangeResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_PropertyChangeResponse.SetIsPool(false);
            }
            
            return g2C_PropertyChangeResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            Type = default;
            NewAmount = default;
            MessageObjectPool<G2C_PropertyChangeResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_PropertyChangeResponse; } 
        [ProtoMember(4)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public PropertyChangeResultCode ResultCode { get; set; }
        /// <summary>
        /// 回声请求的类型(便于客户端段下一刀路由更新到对应字段)
        /// </summary>
        [ProtoMember(2)]
        public PropertyType Type { get; set; }
        /// <summary>
        /// 成功 = 变更后该属性新余额;NotEnough / OverLimit = 当前实际余额(供 toast「需要 X,你有 Y」);其它失败 = 0
        /// </summary>
        [ProtoMember(3)]
        public long NewAmount { get; set; }
    }
    /// <summary>
    /// 服务端属性变更主动推送(每次写库成功后服务端起,推送目标 = 该 UUID 在线全部会话,§3.3.3 + §5.4)
    /// 推送是「绝对余额快照」非「相对变更流水」,丢失 = 下次登录拉快照对齐(O6 不重试)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_PropertyDeltaPush : AMessage, IMessage
    {
        public static G2C_PropertyDeltaPush Create(bool autoReturn = true)
        {
            var g2C_PropertyDeltaPush = MessageObjectPool<G2C_PropertyDeltaPush>.Rent();
            g2C_PropertyDeltaPush.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_PropertyDeltaPush.SetIsPool(false);
            }
            
            return g2C_PropertyDeltaPush;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Type = default;
            NewAmount = default;
            Reason = default;
            MessageObjectPool<G2C_PropertyDeltaPush>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_PropertyDeltaPush; } 
        /// <summary>
        /// 变更的属性类型
        /// </summary>
        [ProtoMember(1)]
        public PropertyType Type { get; set; }
        /// <summary>
        /// 变更后该属性新余额(绝对值,客户端段下一刀直接覆盖本地视图)
        /// </summary>
        [ProtoMember(2)]
        public long NewAmount { get; set; }
        /// <summary>
        /// 变更来源标识(回声触发方传入的 reason,供客户端段下一刀做 toast / 弹奖动画的来源识别)
        /// </summary>
        [ProtoMember(3)]
        public string Reason { get; set; }
    }
    /// <summary>
    /// 单条批量变更项(声明相对增量,身份从会话取,不携带账号 / 绝对余额)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class PropertyChangeItem : AMessage, IDisposable
    {
        public static PropertyChangeItem Create(bool autoReturn = true)
        {
            var propertyChangeItem = MessageObjectPool<PropertyChangeItem>.Rent();
            propertyChangeItem.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                propertyChangeItem.SetIsPool(false);
            }
            
            return propertyChangeItem;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Type = default;
            Delta = default;
            MessageObjectPool<PropertyChangeItem>.Return(this);
        }
        /// <summary>
        /// 要变更的属性类型
        /// </summary>
        [ProtoMember(1)]
        public PropertyType Type { get; set; }
        /// <summary>
        /// 有符号增量(正 = 增加 / 负 = 减少 / 消费)
        /// </summary>
        [ProtoMember(2)]
        public long Delta { get; set; }
    }
    /// <summary>
    /// 单项裁决结果(结果码 + 变更后余额,口径同单条链路 G2C_PropertyChangeResponse)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class PropertyBatchChangeResultItem : AMessage, IDisposable
    {
        public static PropertyBatchChangeResultItem Create(bool autoReturn = true)
        {
            var propertyBatchChangeResultItem = MessageObjectPool<PropertyBatchChangeResultItem>.Rent();
            propertyBatchChangeResultItem.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                propertyBatchChangeResultItem.SetIsPool(false);
            }
            
            return propertyBatchChangeResultItem;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Type = default;
            ResultCode = default;
            NewAmount = default;
            MessageObjectPool<PropertyBatchChangeResultItem>.Return(this);
        }
        /// <summary>
        /// 回声该项的属性类型
        /// </summary>
        [ProtoMember(1)]
        public PropertyType Type { get; set; }
        /// <summary>
        /// 该项裁决结果码
        /// </summary>
        [ProtoMember(2)]
        public PropertyChangeResultCode ResultCode { get; set; }
        /// <summary>
        /// 成功 = 变更后新余额;NotEnough / OverLimit = 当前实际余额;其它失败 = 0
        /// </summary>
        [ProtoMember(3)]
        public long NewAmount { get; set; }
    }
    /// <summary>
    /// 客户端批量属性变更请求(一次携带 N 项,身份从会话取)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_PropertyBatchChangeRequest : AMessage, IRequest
    {
        public static C2G_PropertyBatchChangeRequest Create(bool autoReturn = true)
        {
            var c2G_PropertyBatchChangeRequest = MessageObjectPool<C2G_PropertyBatchChangeRequest>.Rent();
            c2G_PropertyBatchChangeRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_PropertyBatchChangeRequest.SetIsPool(false);
            }
            
            return c2G_PropertyBatchChangeRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            foreach (var __t in Items) __t.Dispose();
            Items.Clear();
            Reason = default;
            MessageObjectPool<C2G_PropertyBatchChangeRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_PropertyBatchChangeRequest; } 
        [ProtoIgnore]
        public G2C_PropertyBatchChangeResponse ResponseType { get; set; }
        /// <summary>
        /// 待变更项列表(空 → no-op,响应 Results 为空)
        /// </summary>
        [ProtoMember(1)]
        public List<PropertyChangeItem> Items { get; set; } = new List<PropertyChangeItem>();
        /// <summary>
        /// 批次统一来源标识(逐项落账 reason = "{Reason}_{Type}",与单条链路口径一致)
        /// </summary>
        [ProtoMember(2)]
        public string Reason { get; set; }
    }
    /// <summary>
    /// 服务端批量变更逐项裁决响应(Results 顺序与请求 Items 一一对应;客户端也按 Type 匹配双保险)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_PropertyBatchChangeResponse : AMessage, IResponse
    {
        public static G2C_PropertyBatchChangeResponse Create(bool autoReturn = true)
        {
            var g2C_PropertyBatchChangeResponse = MessageObjectPool<G2C_PropertyBatchChangeResponse>.Rent();
            g2C_PropertyBatchChangeResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_PropertyBatchChangeResponse.SetIsPool(false);
            }
            
            return g2C_PropertyBatchChangeResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            foreach (var __t in Results) __t.Dispose();
            Results.Clear();
            MessageObjectPool<G2C_PropertyBatchChangeResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_PropertyBatchChangeResponse; } 
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 逐项结果(每项独立裁决,含成功 / 各类失败)
        /// </summary>
        [ProtoMember(1)]
        public List<PropertyBatchChangeResultItem> Results { get; set; } = new List<PropertyBatchChangeResultItem>();
    }
    /// <summary>
    /// 单条 ledger 流水项(白名单 7 字段,不暴露 ObjectId / SchemaVersion / Account)(§3.2)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class AttrLedgerEntry : AMessage, IDisposable
    {
        public static AttrLedgerEntry Create(bool autoReturn = true)
        {
            var attrLedgerEntry = MessageObjectPool<AttrLedgerEntry>.Rent();
            attrLedgerEntry.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                attrLedgerEntry.SetIsPool(false);
            }
            
            return attrLedgerEntry;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Timestamp = default;
            Kind = default;
            BalanceBefore = default;
            BalanceAfter = default;
            Delta = default;
            Source = default;
            ReasonRaw = default;
            MessageObjectPool<AttrLedgerEntry>.Return(this);
        }
        /// <summary>
        /// 该笔变更的应用端时刻(Unix 毫秒 UTC,= 44 Timestamp 同源)
        /// </summary>
        [ProtoMember(1)]
        public long Timestamp { get; set; }
        /// <summary>
        /// 属性种类(沿 37 PropertyType 枚举)
        /// </summary>
        [ProtoMember(2)]
        public PropertyType Kind { get; set; }
        /// <summary>
        /// 变更前余额(非负)
        /// </summary>
        [ProtoMember(3)]
        public long BalanceBefore { get; set; }
        /// <summary>
        /// 变更后余额(非负,= BalanceBefore + Delta)
        /// </summary>
        [ProtoMember(4)]
        public long BalanceAfter { get; set; }
        /// <summary>
        /// 相对变更量(有符号)
        /// </summary>
        [ProtoMember(5)]
        public long Delta { get; set; }
        /// <summary>
        /// 变更来源枚举码(= 服务端 AttrChangeSource 整数;客户端段下一刀映射为人类可读文本)
        /// </summary>
        [ProtoMember(6)]
        public int Source { get; set; }
        /// <summary>
        /// 调用方原始 reason 字符串(供运营 ad-hoc 查子分类如 mailId / codeId / rankIdx)
        /// </summary>
        [ProtoMember(7)]
        public string ReasonRaw { get; set; }
    }
    /// <summary>
    /// 客户端拉 ledger 流水请求(身份从会话取,不携带账号)(§3.1)
    /// limit 必填(本子单不设默认);服务端钳制 [0, 100],超上限钳为 100 不报错(降级语义)。
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_QueryAttrLedger : AMessage, IRequest
    {
        public static C2G_QueryAttrLedger Create(bool autoReturn = true)
        {
            var c2G_QueryAttrLedger = MessageObjectPool<C2G_QueryAttrLedger>.Rent();
            c2G_QueryAttrLedger.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_QueryAttrLedger.SetIsPool(false);
            }
            
            return c2G_QueryAttrLedger;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Kind = default;
            SinceTs = default;
            Limit = default;
            MessageObjectPool<C2G_QueryAttrLedger>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_QueryAttrLedger; } 
        [ProtoIgnore]
        public G2C_QueryAttrLedgerResponse ResponseType { get; set; }
        /// <summary>
        /// 属性种类过滤(0 = 不过滤;1=Diamond / 2=SoulPower / 3=Piety / 4=GuardianExp / 5=Energy / 6=GoddessRating / 7=UnlockedChapter / 8=BlindBoxCount / 9=TempleRepaired / 10=NextRepairIndex,协议层整数 = PropertyType 枚举 + 1 错开一位作 sentinel,未知值返 InvalidRequest)
        /// </summary>
        [ProtoMember(1)]
        public int Kind { get; set; }
        /// <summary>
        /// 时间下界(只返 Timestamp > SinceTs 的行;0 = 不过滤;负数返 InvalidRequest)
        /// </summary>
        [ProtoMember(2)]
        public long SinceTs { get; set; }
        /// <summary>
        /// 单次最多返回行数(服务端钳制 [0, 100];Limit=0 返空 entries[];负数返 InvalidRequest)
        /// </summary>
        [ProtoMember(3)]
        public int Limit { get; set; }
    }
    /// <summary>
    /// 服务端查询响应(§3.2)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_QueryAttrLedgerResponse : AMessage, IResponse
    {
        public static G2C_QueryAttrLedgerResponse Create(bool autoReturn = true)
        {
            var g2C_QueryAttrLedgerResponse = MessageObjectPool<G2C_QueryAttrLedgerResponse>.Rent();
            g2C_QueryAttrLedgerResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_QueryAttrLedgerResponse.SetIsPool(false);
            }
            
            return g2C_QueryAttrLedgerResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            foreach (var __t in Entries) __t.Dispose();
            Entries.Clear();
            HasMore = default;
            MessageObjectPool<G2C_QueryAttrLedgerResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_QueryAttrLedgerResponse; } 
        [ProtoMember(4)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public AttrLedgerQueryResultCode ResultCode { get; set; }
        /// <summary>
        /// 按 (Account, Timestamp DESC) 索引取出的 ledger 行,按 Timestamp DESC 排(最新在前);失败或返空时为空数组
        /// </summary>
        [ProtoMember(2)]
        public List<AttrLedgerEntry> Entries { get; set; } = new List<AttrLedgerEntry>();
        /// <summary>
        /// 是否还有更旧的行(true = 取到 Limit 条且存在 Timestamp 比最后一行更早的行)
        /// </summary>
        [ProtoMember(3)]
        public bool HasMore { get; set; }
    }
    /// <summary>
    /// 客户端全量上报三态(身份从会话取,不带账号;SET 语义,服务端存客户端设的值 + sanity)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_SetProfileStateRequest : AMessage, IRequest
    {
        public static C2G_SetProfileStateRequest Create(bool autoReturn = true)
        {
            var c2G_SetProfileStateRequest = MessageObjectPool<C2G_SetProfileStateRequest>.Rent();
            c2G_SetProfileStateRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_SetProfileStateRequest.SetIsPool(false);
            }
            
            return c2G_SetProfileStateRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            SkinMono = default;
            SkinMonoId = default;
            TempleDecorated = default;
            MessageObjectPool<C2G_SetProfileStateRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_SetProfileStateRequest; } 
        [ProtoIgnore]
        public G2C_SetProfileStateResponse ResponseType { get; set; }
        /// <summary>
        /// 是否单色皮肤模式(0=彩色 / 1=单色)
        /// </summary>
        [ProtoMember(1)]
        public int SkinMono { get; set; }
        /// <summary>
        /// 当前单色皮肤 id(彩色态客户端记 -1;单色态为在用编号)
        /// </summary>
        [ProtoMember(2)]
        public int SkinMonoId { get; set; }
        /// <summary>
        /// 已装饰厅数标量(前缀语义,= 已修厅数)
        /// </summary>
        [ProtoMember(3)]
        public long TempleDecorated { get; set; }
    }
    /// <summary>
    /// 服务端设置三态裁决响应(回带当前权威三态供客户端对账)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_SetProfileStateResponse : AMessage, IResponse
    {
        public static G2C_SetProfileStateResponse Create(bool autoReturn = true)
        {
            var g2C_SetProfileStateResponse = MessageObjectPool<G2C_SetProfileStateResponse>.Rent();
            g2C_SetProfileStateResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_SetProfileStateResponse.SetIsPool(false);
            }
            
            return g2C_SetProfileStateResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            SkinMono = default;
            SkinMonoId = default;
            TempleDecorated = default;
            MessageObjectPool<G2C_SetProfileStateResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_SetProfileStateResponse; } 
        [ProtoMember(5)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码(SetProfileStateResultCode)
        /// </summary>
        [ProtoMember(1)]
        public int ResultCode { get; set; }
        /// <summary>
        /// 服务端当前权威:是否单色(成功 = set 后;失败 = 当前值供客户端回退)
        /// </summary>
        [ProtoMember(2)]
        public int SkinMono { get; set; }
        /// <summary>
        /// 服务端当前权威:当前单色皮肤 id(同上)
        /// </summary>
        [ProtoMember(3)]
        public int SkinMonoId { get; set; }
        /// <summary>
        /// 服务端当前权威:已装饰厅数(同上)
        /// </summary>
        [ProtoMember(4)]
        public long TempleDecorated { get; set; }
    }
    /// <summary>
    /// 榜单一条条目：名次 + 玩家展示名 + 分数（§3.5）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class RankEntryItem : AMessage, IDisposable
    {
        public static RankEntryItem Create(bool autoReturn = true)
        {
            var rankEntryItem = MessageObjectPool<RankEntryItem>.Rent();
            rankEntryItem.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                rankEntryItem.SetIsPool(false);
            }
            
            return rankEntryItem;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Rank = default;
            PlayerName = default;
            Score = default;
            MessageObjectPool<RankEntryItem>.Return(this);
        }
        /// <summary>
        /// 服务端算好的名次（1 起，顺序名次，同分各占唯一名次）
        /// </summary>
        [ProtoMember(1)]
        public int Rank { get; set; }
        /// <summary>
        /// 玩家展示名：本增量回账号标识占位，客户端有本地昵称则替换（§3.5 注 / O5）
        /// </summary>
        [ProtoMember(2)]
        public string PlayerName { get; set; }
        /// <summary>
        /// 该条目的最佳成绩
        /// </summary>
        [ProtoMember(3)]
        public long Score { get; set; }
    }
    /// <summary>
    /// 客户端上报一次成绩请求（玩法结束提交；身份从会话取，不携带账号）（§3.1）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_RankSubmitScoreRequest : AMessage, IRequest
    {
        public static C2G_RankSubmitScoreRequest Create(bool autoReturn = true)
        {
            var c2G_RankSubmitScoreRequest = MessageObjectPool<C2G_RankSubmitScoreRequest>.Rent();
            c2G_RankSubmitScoreRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_RankSubmitScoreRequest.SetIsPool(false);
            }
            
            return c2G_RankSubmitScoreRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            RankId = default;
            Score = default;
            MessageObjectPool<C2G_RankSubmitScoreRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_RankSubmitScoreRequest; } 
        [ProtoIgnore]
        public G2C_RankSubmitScoreResponse ResponseType { get; set; }
        /// <summary>
        /// 这次成绩提交到哪个榜（设计 22 的榜唯一 id）
        /// </summary>
        [ProtoMember(1)]
        public int RankId { get; set; }
        /// <summary>
        /// 客户端玩法这一局算出的成绩值（容纳 rank_condition 同量级 long；负/0 由服务端按入榜要求过滤）
        /// </summary>
        [ProtoMember(2)]
        public long Score { get; set; }
    }
    /// <summary>
    /// 服务端上报裁决响应（结果码 + 当前最佳）（§3.2）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_RankSubmitScoreResponse : AMessage, IResponse
    {
        public static G2C_RankSubmitScoreResponse Create(bool autoReturn = true)
        {
            var g2C_RankSubmitScoreResponse = MessageObjectPool<G2C_RankSubmitScoreResponse>.Rent();
            g2C_RankSubmitScoreResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_RankSubmitScoreResponse.SetIsPool(false);
            }
            
            return g2C_RankSubmitScoreResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            BestScore = default;
            MessageObjectPool<G2C_RankSubmitScoreResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_RankSubmitScoreResponse; } 
        [ProtoMember(3)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public RankSubmitResultCode ResultCode { get; set; }
        /// <summary>
        /// 此账号该榜当前最佳成绩（便于客户端显示「你的最佳分」；无成绩为 0）
        /// </summary>
        [ProtoMember(2)]
        public long BestScore { get; set; }
    }
    /// <summary>
    /// 客户端查榜请求（不分页，返展示上限条数；身份从会话取，不携带账号）（§3.4）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_RankQueryRequest : AMessage, IRequest
    {
        public static C2G_RankQueryRequest Create(bool autoReturn = true)
        {
            var c2G_RankQueryRequest = MessageObjectPool<C2G_RankQueryRequest>.Rent();
            c2G_RankQueryRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_RankQueryRequest.SetIsPool(false);
            }
            
            return c2G_RankQueryRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            RankId = default;
            MessageObjectPool<C2G_RankQueryRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_RankQueryRequest; } 
        [ProtoIgnore]
        public G2C_RankQueryResponse ResponseType { get; set; }
        /// <summary>
        /// 查哪个榜
        /// </summary>
        [ProtoMember(1)]
        public int RankId { get; set; }
    }
    /// <summary>
    /// 服务端查榜响应（前 N 名条目 + 自己名次 + 自己分数）（§3.5）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_RankQueryResponse : AMessage, IResponse
    {
        public static G2C_RankQueryResponse Create(bool autoReturn = true)
        {
            var g2C_RankQueryResponse = MessageObjectPool<G2C_RankQueryResponse>.Rent();
            g2C_RankQueryResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_RankQueryResponse.SetIsPool(false);
            }
            
            return g2C_RankQueryResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            foreach (var __t in Entries) __t.Dispose();
            Entries.Clear();
            MyRank = default;
            MyScore = default;
            DailyClaimable = default;
            PraiseClaimable = default;
            MessageObjectPool<G2C_RankQueryResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_RankQueryResponse; } 
        [ProtoMember(7)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 结果码
        /// </summary>
        [ProtoMember(1)]
        public RankQueryResultCode ResultCode { get; set; }
        /// <summary>
        /// 已按分数降序 + 同分达到时间升序排好、截到展示上限（show_count_max）的条目
        /// </summary>
        [ProtoMember(2)]
        public List<RankEntryItem> Entries { get; set; } = new List<RankEntryItem>();
        /// <summary>
        /// 请求者（会话账号）在全服的名次；未入榜（无成绩/低于入榜要求/超入榜上限）= 0
        /// </summary>
        [ProtoMember(3)]
        public int MyRank { get; set; }
        /// <summary>
        /// 请求者当前最佳成绩；无成绩 = 0（名次 0 时分数照回供「距上榜差值」显示）
        /// </summary>
        [ProtoMember(4)]
        public long MyScore { get; set; }
        /// <summary>
        /// 该榜今日每日奖是否可领（已入榜 + 名次档有每日奖 + 今日未领）；客户端画每日领取按钮/红点
        /// </summary>
        [ProtoMember(5)]
        public bool DailyClaimable { get; set; }
        /// <summary>
        /// 该榜今日点赞奖是否可领（该榜有点赞奖 + 今日未领）；客户端画点赞领取按钮/红点
        /// </summary>
        [ProtoMember(6)]
        public bool PraiseClaimable { get; set; }
    }
    /// <summary>
    /// 客户端领取每日/点赞奖请求（身份从会话取，不携带账号）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_RankClaimRewardRequest : AMessage, IRequest
    {
        public static C2G_RankClaimRewardRequest Create(bool autoReturn = true)
        {
            var c2G_RankClaimRewardRequest = MessageObjectPool<C2G_RankClaimRewardRequest>.Rent();
            c2G_RankClaimRewardRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_RankClaimRewardRequest.SetIsPool(false);
            }
            
            return c2G_RankClaimRewardRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            RankId = default;
            ClaimType = default;
            MessageObjectPool<C2G_RankClaimRewardRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_RankClaimRewardRequest; } 
        [ProtoIgnore]
        public G2C_RankClaimRewardResponse ResponseType { get; set; }
        /// <summary>
        /// 领哪个榜的奖
        /// </summary>
        [ProtoMember(1)]
        public int RankId { get; set; }
        /// <summary>
        /// 领哪类奖：1=每日奖（按名次档）/ 2=点赞奖（榜级）
        /// </summary>
        [ProtoMember(2)]
        public int ClaimType { get; set; }
    }
    /// <summary>
    /// 服务端领取裁决响应（奖励走服务端邮件，不在回包；客户端成功后重拉收件箱见奖）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_RankClaimRewardResponse : AMessage, IResponse
    {
        public static G2C_RankClaimRewardResponse Create(bool autoReturn = true)
        {
            var g2C_RankClaimRewardResponse = MessageObjectPool<G2C_RankClaimRewardResponse>.Rent();
            g2C_RankClaimRewardResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_RankClaimRewardResponse.SetIsPool(false);
            }
            
            return g2C_RankClaimRewardResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            MessageObjectPool<G2C_RankClaimRewardResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_RankClaimRewardResponse; } 
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public RankClaimResultCode ResultCode { get; set; }
    }
    /// <summary>
    /// 客户端提交兑换码请求（玩家身份从会话取，不在请求中携带账号）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_RedeemCodeRequest : AMessage, IRequest
    {
        public static C2G_RedeemCodeRequest Create(bool autoReturn = true)
        {
            var c2G_RedeemCodeRequest = MessageObjectPool<C2G_RedeemCodeRequest>.Rent();
            c2G_RedeemCodeRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_RedeemCodeRequest.SetIsPool(false);
            }
            
            return c2G_RedeemCodeRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Code = default;
            MessageObjectPool<C2G_RedeemCodeRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_RedeemCodeRequest; } 
        [ProtoIgnore]
        public G2C_RedeemCodeResponse ResponseType { get; set; }
        /// <summary>
        /// 玩家提交的兑换码字符串（规整权威在服务端：服务端 trim + 转大写后查表）
        /// </summary>
        [ProtoMember(1)]
        public string Code { get; set; }
    }
    /// <summary>
    /// 服务端兑换裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_RedeemCodeResponse : AMessage, IResponse
    {
        public static G2C_RedeemCodeResponse Create(bool autoReturn = true)
        {
            var g2C_RedeemCodeResponse = MessageObjectPool<G2C_RedeemCodeResponse>.Rent();
            g2C_RedeemCodeResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_RedeemCodeResponse.SetIsPool(false);
            }
            
            return g2C_RedeemCodeResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            RewardBoxId = default;
            MessageObjectPool<G2C_RedeemCodeResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_RedeemCodeResponse; } 
        [ProtoMember(3)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public RedeemResultCode ResultCode { get; set; }
        /// <summary>
        /// 仅 ResultCode=Success 时非 0：本次发放的奖励盒 id（服务端已权威发奖 + 推送，客户端读 TbRewardBox 展示内容）
        /// </summary>
        [ProtoMember(2)]
        public int RewardBoxId { get; set; }
    }
    /// <summary>
    /// 客户端请求改名(身份从会话取,不带账号 / 不带费用 / 不带次数——服务端按 PlayerDoc.RenameCount 自己算)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_RenameRequest : AMessage, IRequest
    {
        public static C2G_RenameRequest Create(bool autoReturn = true)
        {
            var c2G_RenameRequest = MessageObjectPool<C2G_RenameRequest>.Rent();
            c2G_RenameRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_RenameRequest.SetIsPool(false);
            }
            
            return c2G_RenameRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            NewNickname = default;
            MessageObjectPool<C2G_RenameRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_RenameRequest; } 
        [ProtoIgnore]
        public G2C_RenameResponse ResponseType { get; set; }
        /// <summary>
        /// 新昵称(客户端已做合法性/屏蔽字校验;服务端本批信任并存,仅挡长度/空串等基本 sanity)
        /// </summary>
        [ProtoMember(1)]
        public string NewNickname { get; set; }
    }
    /// <summary>
    /// 服务端改名裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_RenameResponse : AMessage, IResponse
    {
        public static G2C_RenameResponse Create(bool autoReturn = true)
        {
            var g2C_RenameResponse = MessageObjectPool<G2C_RenameResponse>.Rent();
            g2C_RenameResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_RenameResponse.SetIsPool(false);
            }
            
            return g2C_RenameResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            Nickname = default;
            RenameCount = default;
            Diamond = default;
            MessageObjectPool<G2C_RenameResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_RenameResponse; } 
        [ProtoMember(5)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public RenameResultCode ResultCode { get; set; }
        /// <summary>
        /// 成功 = 新昵称;失败 = 服务端当前权威昵称(供客户端回退显示)
        /// </summary>
        [ProtoMember(2)]
        public string Nickname { get; set; }
        /// <summary>
        /// 成功 = +1 后的次数;失败 = 当前次数(供客户端算下次费用)
        /// </summary>
        [ProtoMember(3)]
        public int RenameCount { get; set; }
        /// <summary>
        /// 成功且扣费 = 扣后钻石余额;免费成功 / 钻不足 / 其它失败 = 当前钻石余额;读取失败为 0
        /// </summary>
        [ProtoMember(4)]
        public long Diamond { get; set; }
    }
    /// <summary>
    /// 单条道具持有(itemId → 数量;通用道具持有投影,当前业务方 = 背包堆叠道具)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class ItemHolding : AMessage, IDisposable
    {
        public static ItemHolding Create(bool autoReturn = true)
        {
            var itemHolding = MessageObjectPool<ItemHolding>.Rent();
            itemHolding.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                itemHolding.SetIsPool(false);
            }
            
            return itemHolding;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ItemId = default;
            Count = default;
            MessageObjectPool<ItemHolding>.Return(this);
        }
        /// <summary>
        /// 道具 id(= TbItemDef 行)
        /// </summary>
        [ProtoMember(1)]
        public int ItemId { get; set; }
        /// <summary>
        /// 持有数量(权威绝对值)
        /// </summary>
        [ProtoMember(2)]
        public long Count { get; set; }
    }
    /// <summary>
    /// 单张牌进度(cardId → 已购步数;塔罗进度投影载体,快照与购买响应共用)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class TarotProgressEntry : AMessage, IDisposable
    {
        public static TarotProgressEntry Create(bool autoReturn = true)
        {
            var tarotProgressEntry = MessageObjectPool<TarotProgressEntry>.Rent();
            tarotProgressEntry.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                tarotProgressEntry.SetIsPool(false);
            }
            
            return tarotProgressEntry;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            CardId = default;
            Steps = default;
            MessageObjectPool<TarotProgressEntry>.Return(this);
        }
        /// <summary>
        /// 牌 id(= TbTarotCard 行)
        /// </summary>
        [ProtoMember(1)]
        public int CardId { get; set; }
        /// <summary>
        /// 已购进度步数(0..UnlockCosts.Length;== 数组长度即该牌已激活)
        /// </summary>
        [ProtoMember(2)]
        public int Steps { get; set; }
    }
    /// <summary>
    /// 客户端请求购买一步塔罗牌进度(身份从会话取;本步成本由服务端按 TbTarotCard.UnlockCosts[当前步] 自算,客户端不上报)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_TarotPurchaseRequest : AMessage, IRequest
    {
        public static C2G_TarotPurchaseRequest Create(bool autoReturn = true)
        {
            var c2G_TarotPurchaseRequest = MessageObjectPool<C2G_TarotPurchaseRequest>.Rent();
            c2G_TarotPurchaseRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_TarotPurchaseRequest.SetIsPool(false);
            }
            
            return c2G_TarotPurchaseRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            CardId = default;
            MessageObjectPool<C2G_TarotPurchaseRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_TarotPurchaseRequest; } 
        [ProtoIgnore]
        public G2C_TarotPurchaseResponse ResponseType { get; set; }
        /// <summary>
        /// 目标塔罗牌 id(= TbTarotCard 行)
        /// </summary>
        [ProtoMember(1)]
        public int CardId { get; set; }
    }
    /// <summary>
    /// 服务端购买裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_TarotPurchaseResponse : AMessage, IResponse
    {
        public static G2C_TarotPurchaseResponse Create(bool autoReturn = true)
        {
            var g2C_TarotPurchaseResponse = MessageObjectPool<G2C_TarotPurchaseResponse>.Rent();
            g2C_TarotPurchaseResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_TarotPurchaseResponse.SetIsPool(false);
            }
            
            return g2C_TarotPurchaseResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            CardId = default;
            Steps = default;
            PietyBalance = default;
            foreach (var __t in ProgressAll) __t.Dispose();
            ProgressAll.Clear();
            ProgressValid = default;
            MessageObjectPool<G2C_TarotPurchaseResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_TarotPurchaseResponse; } 
        [ProtoMember(7)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public TarotPurchaseResultCode ResultCode { get; set; }
        /// <summary>
        /// 回声牌 id
        /// </summary>
        [ProtoMember(2)]
        public int CardId { get; set; }
        /// <summary>
        /// 该牌裁决后已购进度步数(-1 = 哨兵,未取到权威值,客户端不据此 set)
        /// </summary>
        [ProtoMember(3)]
        public int Steps { get; set; }
        /// <summary>
        /// 扣减后虔诚币权威余额(-1 = 哨兵,本次未取到权威值,客户端不据此 set)
        /// </summary>
        [ProtoMember(4)]
        public long PietyBalance { get; set; }
        /// <summary>
        /// 全牌进度全集(仅 ProgressValid=true 时可信,客户端整份覆盖)
        /// </summary>
        [ProtoMember(5)]
        public List<TarotProgressEntry> ProgressAll { get; set; } = new List<TarotProgressEntry>();
        /// <summary>
        /// ProgressAll 是否真取自玩家文档:false = 降级路径空占位
        /// </summary>
        [ProtoMember(6)]
        public bool ProgressValid { get; set; }
    }
    /// <summary>
    /// 测试使用ErrorCode枚举的消息
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_TestEnumMessage : AMessage, IMessage
    {
        public static C2G_TestEnumMessage Create(bool autoReturn = true)
        {
            var c2G_TestEnumMessage = MessageObjectPool<C2G_TestEnumMessage>.Rent();
            c2G_TestEnumMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_TestEnumMessage.SetIsPool(false);
            }
            
            return c2G_TestEnumMessage;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            Code = default;
            Message = default;
            State = default;
            MessageObjectPool<C2G_TestEnumMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_TestEnumMessage; } 
        /// <summary>
        /// 错误码
        /// </summary>
        [ProtoMember(1)]
        public ErrorCodeEnum Code { get; set; }
        /// <summary>
        /// 消息内容
        /// </summary>
        [ProtoMember(2)]
        public string Message { get; set; }
        /// <summary>
        /// 玩家状态
        /// </summary>
        [ProtoMember(3)]
        public PlayerState State { get; set; }
    }
    /// <summary>
    /// 客户端请求祈愿兑体力(身份从会话取,不带账号 / 费用 / 次数——服务端按 WishConfigServer 派生 + 按 PlayerDoc 判每日闸)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_WishForEnergyRequest : AMessage, IRequest
    {
        public static C2G_WishForEnergyRequest Create(bool autoReturn = true)
        {
            var c2G_WishForEnergyRequest = MessageObjectPool<C2G_WishForEnergyRequest>.Rent();
            c2G_WishForEnergyRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_WishForEnergyRequest.SetIsPool(false);
            }
            
            return c2G_WishForEnergyRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_WishForEnergyRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_WishForEnergyRequest; } 
        [ProtoIgnore]
        public G2C_WishForEnergyResponse ResponseType { get; set; }
    }
    /// <summary>
    /// 服务端祈愿裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_WishForEnergyResponse : AMessage, IResponse
    {
        public static G2C_WishForEnergyResponse Create(bool autoReturn = true)
        {
            var g2C_WishForEnergyResponse = MessageObjectPool<G2C_WishForEnergyResponse>.Rent();
            g2C_WishForEnergyResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_WishForEnergyResponse.SetIsPool(false);
            }
            
            return g2C_WishForEnergyResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            SoulPower = default;
            Energy = default;
            WishUsedToday = default;
            WishDailyLimit = default;
            MessageObjectPool<G2C_WishForEnergyResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_WishForEnergyResponse; } 
        [ProtoMember(6)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码(WishForEnergyResultCode)
        /// </summary>
        [ProtoMember(1)]
        public int ResultCode { get; set; }
        /// <summary>
        /// 服务端当前权威灵力(成功 = 扣后;失败 = 当前余额供客户端回退;读取失败为 0)
        /// </summary>
        [ProtoMember(2)]
        public long SoulPower { get; set; }
        /// <summary>
        /// 服务端当前权威体力(成功 = 发后夹软上限;失败 = 当前值;读取失败为 0)
        /// </summary>
        [ProtoMember(3)]
        public long Energy { get; set; }
        /// <summary>
        /// 今日已用祈愿次数(懒重置 + 本次成功 +1 后的权威值;失败 = 懒重置后当前值)
        /// </summary>
        [ProtoMember(4)]
        public int WishUsedToday { get; set; }
        /// <summary>
        /// 每日祈愿次数上限(= WishConfigServer.WishDailyLimit,供客户端算今日剩余次数)
        /// </summary>
        [ProtoMember(5)]
        public int WishDailyLimit { get; set; }
    }
    /// <summary>
    /// 客户端请求取用溢出储存(身份从会话取,不带账号 / 不带数量——服务端把整池尽量转入体力)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_WithdrawOverflowRequest : AMessage, IRequest
    {
        public static C2G_WithdrawOverflowRequest Create(bool autoReturn = true)
        {
            var c2G_WithdrawOverflowRequest = MessageObjectPool<C2G_WithdrawOverflowRequest>.Rent();
            c2G_WithdrawOverflowRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_WithdrawOverflowRequest.SetIsPool(false);
            }
            
            return c2G_WithdrawOverflowRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_WithdrawOverflowRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_WithdrawOverflowRequest; } 
        [ProtoIgnore]
        public G2C_WithdrawOverflowResponse ResponseType { get; set; }
    }
    /// <summary>
    /// 服务端取用溢出储存裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_WithdrawOverflowResponse : AMessage, IResponse
    {
        public static G2C_WithdrawOverflowResponse Create(bool autoReturn = true)
        {
            var g2C_WithdrawOverflowResponse = MessageObjectPool<G2C_WithdrawOverflowResponse>.Rent();
            g2C_WithdrawOverflowResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_WithdrawOverflowResponse.SetIsPool(false);
            }
            
            return g2C_WithdrawOverflowResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            Energy = default;
            EnergyOverflowPool = default;
            Withdrawn = default;
            MessageObjectPool<G2C_WithdrawOverflowResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_WithdrawOverflowResponse; } 
        [ProtoMember(5)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public WithdrawOverflowResultCode ResultCode { get; set; }
        /// <summary>
        /// 服务端当前权威体力(成功 = 取用后;失败 = 当前值;读失败为 0)
        /// </summary>
        [ProtoMember(2)]
        public long Energy { get; set; }
        /// <summary>
        /// 服务端当前权威溢出池余(成功 = 扣后;失败 = 当前值;读失败为 0)
        /// </summary>
        [ProtoMember(3)]
        public long EnergyOverflowPool { get; set; }
        /// <summary>
        /// 本次实际取用量(成功 > 0;Empty = 0)
        /// </summary>
        [ProtoMember(4)]
        public long Withdrawn { get; set; }
    }
    /// <summary>
    /// 客户端请求清空自己的玩家数据(身份从会话取,不携带 playerId / 不接受指定清别人)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_ClearPlayerDataRequest : AMessage, IRequest
    {
        public static C2G_ClearPlayerDataRequest Create(bool autoReturn = true)
        {
            var c2G_ClearPlayerDataRequest = MessageObjectPool<C2G_ClearPlayerDataRequest>.Rent();
            c2G_ClearPlayerDataRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_ClearPlayerDataRequest.SetIsPool(false);
            }
            
            return c2G_ClearPlayerDataRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_ClearPlayerDataRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_ClearPlayerDataRequest; } 
        [ProtoIgnore]
        public G2C_ClearPlayerDataResponse ResponseType { get; set; }
    }
    /// <summary>
    /// 服务端清档裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_ClearPlayerDataResponse : AMessage, IResponse
    {
        public static G2C_ClearPlayerDataResponse Create(bool autoReturn = true)
        {
            var g2C_ClearPlayerDataResponse = MessageObjectPool<G2C_ClearPlayerDataResponse>.Rent();
            g2C_ClearPlayerDataResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_ClearPlayerDataResponse.SetIsPool(false);
            }
            
            return g2C_ClearPlayerDataResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            MessageObjectPool<G2C_ClearPlayerDataResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_ClearPlayerDataResponse; } 
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public ClearPlayerDataResultCode ResultCode { get; set; }
    }
    /// <summary>
    /// 客户端 GM 清盘请求(身份从会话取,不携带账号)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_ClearBoardRequest : AMessage, IRequest
    {
        public static C2G_ClearBoardRequest Create(bool autoReturn = true)
        {
            var c2G_ClearBoardRequest = MessageObjectPool<C2G_ClearBoardRequest>.Rent();
            c2G_ClearBoardRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_ClearBoardRequest.SetIsPool(false);
            }
            
            return c2G_ClearBoardRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            GameId = default;
            SliceJson = default;
            MessageObjectPool<C2G_ClearBoardRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_ClearBoardRequest; } 
        [ProtoIgnore]
        public G2C_ClearBoardResponse ResponseType { get; set; }
        /// <summary>
        /// 目标对局 id
        /// </summary>
        [ProtoMember(1)]
        public long GameId { get; set; }
        /// <summary>
        /// 客户端清后局内叠加层不透明切片,搭车存盘。服务端只搬运不解析
        /// </summary>
        [ProtoMember(2)]
        public string SliceJson { get; set; }
    }
    /// <summary>
    /// 服务端清盘裁决 + 最新权威态
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_ClearBoardResponse : AMessage, IResponse
    {
        public static G2C_ClearBoardResponse Create(bool autoReturn = true)
        {
            var g2C_ClearBoardResponse = MessageObjectPool<G2C_ClearBoardResponse>.Rent();
            g2C_ClearBoardResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_ClearBoardResponse.SetIsPool(false);
            }
            
            return g2C_ClearBoardResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            Step = default;
            Score = default;
            ClearedCells = default;
            Board.Clear();
            if (GeneratorState != null)
            {
                GeneratorState.Dispose();
                GeneratorState = null;
            }
            MessageObjectPool<G2C_ClearBoardResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_ClearBoardResponse; } 
        [ProtoMember(7)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public ClearBoardResultCode ResultCode { get; set; }
        /// <summary>
        /// 清盘后(或当前)权威步号
        /// </summary>
        [ProtoMember(2)]
        public int Step { get; set; }
        /// <summary>
        /// 当前权威分数(清盘不计分,回带当前值供对账)
        /// </summary>
        [ProtoMember(3)]
        public int Score { get; set; }
        /// <summary>
        /// 本次清掉的格数(Cleared 时有效)
        /// </summary>
        [ProtoMember(4)]
        public int ClearedCells { get; set; }
        /// <summary>
        /// 最新权威棋盘(8 行位掩码,清盘后全 0)
        /// </summary>
        [ProtoMember(5)]
        public List<int> Board { get; set; } = new List<int>();
        /// <summary>
        /// 最新完整生成器状态向量(清盘不推进发牌,回带当前态供对账)
        /// </summary>
        [ProtoMember(6)]
        public BlockGenState GeneratorState { get; set; }
    }
    /// <summary>
    /// 客户端 GM 清背包请求(身份从会话取,不携带账号)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_ClearInventoryRequest : AMessage, IRequest
    {
        public static C2G_ClearInventoryRequest Create(bool autoReturn = true)
        {
            var c2G_ClearInventoryRequest = MessageObjectPool<C2G_ClearInventoryRequest>.Rent();
            c2G_ClearInventoryRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_ClearInventoryRequest.SetIsPool(false);
            }
            
            return c2G_ClearInventoryRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            MessageObjectPool<C2G_ClearInventoryRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_ClearInventoryRequest; } 
        [ProtoIgnore]
        public G2C_ClearInventoryResponse ResponseType { get; set; }
    }
    /// <summary>
    /// 服务端清背包裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_ClearInventoryResponse : AMessage, IResponse
    {
        public static G2C_ClearInventoryResponse Create(bool autoReturn = true)
        {
            var g2C_ClearInventoryResponse = MessageObjectPool<G2C_ClearInventoryResponse>.Rent();
            g2C_ClearInventoryResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_ClearInventoryResponse.SetIsPool(false);
            }
            
            return g2C_ClearInventoryResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            MessageObjectPool<G2C_ClearInventoryResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_ClearInventoryResponse; } 
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public ClearInventoryResultCode ResultCode { get; set; }
    }
    /// <summary>
    /// 客户端 GM 发测试道具请求(身份从会话取,不携带账号)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2G_GrantTestItemRequest : AMessage, IRequest
    {
        public static C2G_GrantTestItemRequest Create(bool autoReturn = true)
        {
            var c2G_GrantTestItemRequest = MessageObjectPool<C2G_GrantTestItemRequest>.Rent();
            c2G_GrantTestItemRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2G_GrantTestItemRequest.SetIsPool(false);
            }
            
            return c2G_GrantTestItemRequest;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ItemId = default;
            Count = default;
            MessageObjectPool<C2G_GrantTestItemRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_GrantTestItemRequest; } 
        [ProtoIgnore]
        public G2C_GrantTestItemResponse ResponseType { get; set; }
        /// <summary>
        /// 道具 id(TbItemDef 主键)
        /// </summary>
        [ProtoMember(1)]
        public int ItemId { get; set; }
        /// <summary>
        /// 发放数量(> 0)
        /// </summary>
        [ProtoMember(2)]
        public long Count { get; set; }
    }
    /// <summary>
    /// 服务端发测试道具裁决响应
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_GrantTestItemResponse : AMessage, IResponse
    {
        public static G2C_GrantTestItemResponse Create(bool autoReturn = true)
        {
            var g2C_GrantTestItemResponse = MessageObjectPool<G2C_GrantTestItemResponse>.Rent();
            g2C_GrantTestItemResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_GrantTestItemResponse.SetIsPool(false);
            }
            
            return g2C_GrantTestItemResponse;
        }
        
        public void Return()
        {
            if (!AutoReturn)
            {
                SetIsPool(true);
                AutoReturn = true;
            }
            else if (!IsPool())
            {
                return;
            }
            Dispose();
        }

        public void Dispose()
        {
            if (!IsPool()) return; 
            ErrorCode = 0;
            ResultCode = default;
            MessageObjectPool<G2C_GrantTestItemResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_GrantTestItemResponse; } 
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
        /// <summary>
        /// 裁决结果码
        /// </summary>
        [ProtoMember(1)]
        public GrantTestItemResultCode ResultCode { get; set; }
    }
}