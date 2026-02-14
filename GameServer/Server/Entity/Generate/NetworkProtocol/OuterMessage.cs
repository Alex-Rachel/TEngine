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
    [Serializable]
    [ProtoContract]
    public partial class C2A_RegisterRequest : AMessage, IRequest
    {
        public static C2A_RegisterRequest Create(bool autoReturn = true)
        {
            var c2A_RegisterRequest = MessageObjectPool<C2A_RegisterRequest>.Rent();
            c2A_RegisterRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2A_RegisterRequest.SetIsPool(false);
            }
            
            return c2A_RegisterRequest;
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
            UserName = default;
            Password = default;
            MessageObjectPool<C2A_RegisterRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2A_RegisterRequest; } 
        [ProtoIgnore]
        public A2C_RegisterResponse ResponseType { get; set; }
        [ProtoMember(1)]
        public string UserName { get; set; }
        [ProtoMember(2)]
        public string Password { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class A2C_RegisterResponse : AMessage, IResponse
    {
        public static A2C_RegisterResponse Create(bool autoReturn = true)
        {
            var a2C_RegisterResponse = MessageObjectPool<A2C_RegisterResponse>.Rent();
            a2C_RegisterResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                a2C_RegisterResponse.SetIsPool(false);
            }
            
            return a2C_RegisterResponse;
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
            MessageObjectPool<A2C_RegisterResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.A2C_RegisterResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class C2A_LoginRequest : AMessage, IRequest
    {
        public static C2A_LoginRequest Create(bool autoReturn = true)
        {
            var c2A_LoginRequest = MessageObjectPool<C2A_LoginRequest>.Rent();
            c2A_LoginRequest.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                c2A_LoginRequest.SetIsPool(false);
            }
            
            return c2A_LoginRequest;
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
            UserName = default;
            Password = default;
            LoginType = default;
            MessageObjectPool<C2A_LoginRequest>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.C2A_LoginRequest; } 
        [ProtoIgnore]
        public A2C_LoginResponse ResponseType { get; set; }
        [ProtoMember(1)]
        public string UserName { get; set; }
        [ProtoMember(2)]
        public string Password { get; set; }
        [ProtoMember(3)]
        public uint LoginType { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class A2C_LoginResponse : AMessage, IResponse
    {
        public static A2C_LoginResponse Create(bool autoReturn = true)
        {
            var a2C_LoginResponse = MessageObjectPool<A2C_LoginResponse>.Rent();
            a2C_LoginResponse.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                a2C_LoginResponse.SetIsPool(false);
            }
            
            return a2C_LoginResponse;
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
            Token = default;
            MessageObjectPool<A2C_LoginResponse>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.A2C_LoginResponse; } 
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
        [ProtoMember(2)]
        public string Token { get; set; }
    }
    [Serializable]
    [ProtoContract]
    public partial class G2C_LoginMessage : AMessage, IMessage
    {
        public static G2C_LoginMessage Create(bool autoReturn = true)
        {
            var g2C_LoginMessage = MessageObjectPool<G2C_LoginMessage>.Rent();
            g2C_LoginMessage.AutoReturn = autoReturn;
            
            if (!autoReturn)
            {
                g2C_LoginMessage.SetIsPool(false);
            }
            
            return g2C_LoginMessage;
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
            Msg = default;
            MessageObjectPool<G2C_LoginMessage>.Return(this);
        }
        public uint OpCode() { return OuterOpcode.G2C_LoginMessage; } 
        [ProtoMember(1)]
        public string Msg { get; set; }
    }
}