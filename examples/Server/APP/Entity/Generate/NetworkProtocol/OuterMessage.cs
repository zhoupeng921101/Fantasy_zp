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
            MessageObjectPool<C2G_LoginGameRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2G_LoginGameRequest; } 
        [ProtoIgnore]
        public G2C_LoginGameResponse ResponseType { get; set; }
        [ProtoMember(1)]
        public string AccountName { get; set; }
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
            MessageObjectPool<G2C_LoginGameResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_LoginGameResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 客户端通知服务器可以接收服务器推送的消息
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2M_InitComplete : AMessage, IRoamingMessage
    {
        public static C2M_InitComplete Create(bool autoReturn = true)
        {
            var c2M_InitComplete = MessageObjectPool<C2M_InitComplete>.Rent();
            c2M_InitComplete.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2M_InitComplete.SetIsPool(false);
            }
            
            return c2M_InitComplete;
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
            MessageObjectPool<C2M_InitComplete>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2M_InitComplete; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.MapRoamingType;
    }
    /// <summary>
    /// Map服务器通知客户端创建新的Unit
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class M2C_UnitCreate : AMessage, IRoamingMessage
    {
        public static M2C_UnitCreate Create(bool autoReturn = true)
        {
            var m2C_UnitCreate = MessageObjectPool<M2C_UnitCreate>.Rent();
            m2C_UnitCreate.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                m2C_UnitCreate.SetIsPool(false);
            }
            
            return m2C_UnitCreate;
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
            if (Unit != null)
            {
                Unit.Dispose();
                Unit = null;
            }
            IsSelf = default;
            MessageObjectPool<M2C_UnitCreate>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.M2C_UnitCreate; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.MapRoamingType;
        [ProtoMember(1)]
        public UnitInfo Unit { get; set; }
        [ProtoMember(2)]
        public bool IsSelf { get; set; }
    }
    /// <summary>
    /// Map通知客户端有Unit离开
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class M2C_UnitLeave : AMessage, IRoamingMessage
    {
        public static M2C_UnitLeave Create(bool autoReturn = true)
        {
            var m2C_UnitLeave = MessageObjectPool<M2C_UnitLeave>.Rent();
            m2C_UnitLeave.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                m2C_UnitLeave.SetIsPool(false);
            }
            
            return m2C_UnitLeave;
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
            UnitId = default;
            MessageObjectPool<M2C_UnitLeave>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.M2C_UnitLeave; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.MapRoamingType;
        [ProtoMember(1)]
        public long UnitId { get; set; }
    }
    /// <summary>
    /// Unit信息
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class UnitInfo : AMessage, IDisposable
    {
        public static UnitInfo Create(bool autoReturn = true)
        {
            var unitInfo = MessageObjectPool<UnitInfo>.Rent();
            unitInfo.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                unitInfo.SetIsPool(false);
            }
            
            return unitInfo;
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
            UnitId = default;
            Name = default;
            if (Pos != null)
            {
                Pos.Dispose();
                Pos = null;
            }
            UnitType = default;
            MessageObjectPool<UnitInfo>.Return(this);
        }
        [ProtoMember(1)]
        public long UnitId { get; set; }
        [ProtoMember(2)]
        public string Name { get; set; }
        [ProtoMember(3)]
        public Position Pos { get; set; }
        [ProtoMember(4)]
        public int UnitType { get; set; }
    }
    /// <summary>
    /// 客户端发送请求移动
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class C2M_MoveRequest : AMessage, IRoamingRequest
    {
        public static C2M_MoveRequest Create(bool autoReturn = true)
        {
            var c2M_MoveRequest = MessageObjectPool<C2M_MoveRequest>.Rent();
            c2M_MoveRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2M_MoveRequest.SetIsPool(false);
            }
            
            return c2M_MoveRequest;
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
            if (TargetPos != null)
            {
                TargetPos.Dispose();
                TargetPos = null;
            }
            MessageObjectPool<C2M_MoveRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2M_MoveRequest; } 
        [ProtoIgnore]
        public M2C_MoveResponse ResponseType { get; set; }
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.MapRoamingType;
        [ProtoMember(1)]
        public Position TargetPos { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class M2C_MoveResponse : AMessage, IRoamingResponse
    {
        public static M2C_MoveResponse Create(bool autoReturn = true)
        {
            var m2C_MoveResponse = MessageObjectPool<M2C_MoveResponse>.Rent();
            m2C_MoveResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                m2C_MoveResponse.SetIsPool(false);
            }
            
            return m2C_MoveResponse;
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
            foreach (var __t in Data) __t.Dispose();
            Data.Clear();
            MessageObjectPool<M2C_MoveResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.M2C_MoveResponse; } 
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
        [ProtoMember(1)]
        public List<Position> Data { get; set; } = new List<Position>();
    }
    /// <summary>
    /// 坐标信息
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class Position : AMessage, IDisposable
    {
        public static Position Create(bool autoReturn = true)
        {
            var position = MessageObjectPool<Position>.Rent();
            position.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                position.SetIsPool(false);
            }
            
            return position;
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
            X = default;
            Y = default;
            Z = default;
            MessageObjectPool<Position>.Return(this);
        }
        [ProtoMember(1)]
        public float X { get; set; }
        [ProtoMember(2)]
        public float Y { get; set; }
        [ProtoMember(3)]
        public float Z { get; set; }
    }
    /// <summary>
    /// 通知客户端Unit移动状态改变
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class M2C_UnitMoveState : AMessage, IRoamingMessage
    {
        public static M2C_UnitMoveState Create(bool autoReturn = true)
        {
            var m2C_UnitMoveState = MessageObjectPool<M2C_UnitMoveState>.Rent();
            m2C_UnitMoveState.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                m2C_UnitMoveState.SetIsPool(false);
            }
            
            return m2C_UnitMoveState;
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
            State = default;
            UnitId = default;
            if (Pos != null)
            {
                Pos.Dispose();
                Pos = null;
            }
            MessageObjectPool<M2C_UnitMoveState>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.M2C_UnitMoveState; } 
        [ProtoIgnore]
        public int RouteType => Fantasy.RoamingType.MapRoamingType;
        [ProtoMember(1)]
        public int State { get; set; }
        [ProtoMember(2)]
        public long UnitId { get; set; }
        [ProtoMember(3)]
        public Position Pos { get; set; }
    }
    /// <summary>
    /// 邮件列表一条：客户端画收件箱用（不含奖励明细，奖励领取时才抽，见 §3.2 注）
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
            SenderTextId = default;
            TitleTextId = default;
            ContentTextId = default;
            SendUnixMs = default;
            HasReward = default;
            Claimed = default;
            MessageObjectPool<MailListItem>.Return(this);
        }
        /// <summary>
        /// 邮件标识（领取时回传定位；广播邮件与定向邮件用同一标识空间，客户端无需区分）
        /// </summary>
        [ProtoMember(1)]
        public string MailId { get; set; }
        /// <summary>
        /// 发件人（多语言 textId 占位）
        /// </summary>
        [ProtoMember(2)]
        public int SenderTextId { get; set; }
        /// <summary>
        /// 标题（多语言 textId 占位）
        /// </summary>
        [ProtoMember(3)]
        public int TitleTextId { get; set; }
        /// <summary>
        /// 正文（多语言 textId 占位）
        /// </summary>
        [ProtoMember(4)]
        public int ContentTextId { get; set; }
        /// <summary>
        /// 收件/发件时间（服务端 Unix 毫秒）
        /// </summary>
        [ProtoMember(5)]
        public long SendUnixMs { get; set; }
        /// <summary>
        /// 是否有附件（附件库 id != 0；客户端据此画领取按钮/红点）
        /// </summary>
        [ProtoMember(6)]
        public bool HasReward { get; set; }
        /// <summary>
        /// 该账号对此邮件的领取态（已领=true / 未领=false）
        /// </summary>
        [ProtoMember(7)]
        public bool Claimed { get; set; }
    }
    /// <summary>
    /// 领取响应内单条奖励项：道具 id × 数量（与既有奖励同源，客户端用道具元数据解析展示）
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
            ItemId = default;
            Count = default;
            MessageObjectPool<MailRewardItem>.Return(this);
        }
        /// <summary>
        /// 道具 id
        /// </summary>
        [ProtoMember(1)]
        public int ItemId { get; set; }
        /// <summary>
        /// 数量
        /// </summary>
        [ProtoMember(2)]
        public int Count { get; set; }
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
        /// 仅 ResultCode=Success 时非空：服务端按该邮件附件库 id 抽礼包随机库一次的产物（道具 id × 数量）
        /// </summary>
        [ProtoMember(2)]
        public List<MailRewardItem> Rewards { get; set; } = new List<MailRewardItem>();
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
    /// 服务端登录后下发属性初始快照(服务端主动 push,本子单选独立 push message 而非登录响应捎带,
    /// 形态与 G2C_PropertyDeltaPush 对齐,客户端段下一刀同一处订阅)(§3.3.1 + plan D3 + O4)
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class G2C_PropertyInitSnapshot : AMessage, IMessage
    {
        public static G2C_PropertyInitSnapshot Create(bool autoReturn = true)
        {
            var g2C_PropertyInitSnapshot = MessageObjectPool<G2C_PropertyInitSnapshot>.Rent();
            g2C_PropertyInitSnapshot.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_PropertyInitSnapshot.SetIsPool(false);
            }
            
            return g2C_PropertyInitSnapshot;
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
            foreach (var __t in Properties) __t.Dispose();
            Properties.Clear();
            SchemaVersion = default;
            MessageObjectPool<G2C_PropertyInitSnapshot>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_PropertyInitSnapshot; } 
        /// <summary>
        /// 三属性当前余额(每登录一次完整下发,客户端段下一刀作初视图)
        /// </summary>
        [ProtoMember(1)]
        public List<PropertyAmount> Properties { get; set; } = new List<PropertyAmount>();
        /// <summary>
        /// schema 版本(本子单 = 1;Tier 2+ 加字段时升版,客户端段据此识别)
        /// </summary>
        [ProtoMember(2)]
        public int SchemaVersion { get; set; }
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
            MessageObjectPool<G2C_RankQueryResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_RankQueryResponse; } 
        [ProtoMember(5)]
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
    }
    /// <summary>
    /// 兑换奖励项：道具 id × 数量（与既有奖励同源，客户端用道具元数据解析展示）
    /// </summary>
    [Serializable]
    [ProtoContract]
    public partial class RedeemRewardItem : AMessage, IDisposable
    {
        public static RedeemRewardItem Create(bool autoReturn = true)
        {
            var redeemRewardItem = MessageObjectPool<RedeemRewardItem>.Rent();
            redeemRewardItem.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                redeemRewardItem.SetIsPool(false);
            }
            
            return redeemRewardItem;
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
            MessageObjectPool<RedeemRewardItem>.Return(this);
        }
        /// <summary>
        /// 道具 id
        /// </summary>
        [ProtoMember(1)]
        public int ItemId { get; set; }
        /// <summary>
        /// 数量
        /// </summary>
        [ProtoMember(2)]
        public int Count { get; set; }
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
            foreach (var __t in Rewards) __t.Dispose();
            Rewards.Clear();
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
        /// 仅 ResultCode=Success 时非空：本次应发奖励（道具 id × 数量）
        /// </summary>
        [ProtoMember(2)]
        public List<RedeemRewardItem> Rewards { get; set; } = new List<RedeemRewardItem>();
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
}