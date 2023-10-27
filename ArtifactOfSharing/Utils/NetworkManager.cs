using System;
using System.Linq;
using RoR2;
using RoR2.Networking;
using UnityEngine.Networking;

namespace ArtifactOfSharing.Utils
{
    public static class NetworkManager
    {
        public static Type[] RegisteredMessages;

        public static void Initialize()
        {
            NetworkManagerSystem.onStartServerGlobal += RegisterMessages;
            NetworkManagerSystem.onStartClientGlobal += RegisterMessages;

            RegisteredMessages = typeof(ArtifactMessageBase).Assembly.GetTypes().Where(x =>
                typeof(ArtifactMessageBase).IsAssignableFrom(x) && x != typeof(ArtifactMessageBase)).ToArray();
        }

        private static void RegisterMessages()
        {
            NetworkServer.RegisterHandler(2004, HandleMessage);
        }

        public static void RegisterMessages(NetworkClient client)
        {
            client.RegisterHandler(2004, HandleMessage);
        }

        private static void HandleMessage(NetworkMessage netmsg)
        {
            // we can do some auth checks in here later
            var message = netmsg.ReadMessage<ArtifactMessage>();
            if (message.message is BroadcastMessage mess) mess.fromConnection = netmsg.conn;
            message.message.Handle();
        }

        public static void Send<T>(this NetworkConnection connection, T message) where T : ArtifactMessageBase
        {
            var mes = new ArtifactMessage(message);
            connection.Send(2004, mes);
        }
    }

    public class ArtifactMessageBase : MessageBase
    {
        // Needed to create empty instance of class so it can be read
        public virtual void Handle()
        {
        }

        public void SendToServer()
        {
            if (!NetworkServer.active)
                ClientScene.readyConnection.Send(this);
            else
                Handle();
        }

        public void SendToEveryone()
        {
            Handle();
            new BroadcastMessage(this).SendToServer();
        }

        public void SendToAuthority(NetworkIdentity identity)
        {
            if (!Util.HasEffectiveAuthority(identity) && NetworkServer.active)
                identity.clientAuthorityOwner.Send(this);
            else if (!NetworkServer.active)
                new NewAuthMessage(identity, this).SendToServer();
            else
                Handle();
        }

        public void SendToAuthority(NetworkUser user)
        {
            SendToAuthority(user.netIdentity);
        }

        public void SendToAuthority(CharacterMaster master)
        {
            SendToAuthority(master.networkIdentity);
        }

        public void SendToAuthority(CharacterBody body)
        {
            SendToAuthority(body.networkIdentity);
        }
    }

    public class BroadcastMessage : ArtifactMessageBase
    {
        public NetworkConnection fromConnection;
        private ArtifactMessageBase message;

        public BroadcastMessage()
        {
        }

        public BroadcastMessage(ArtifactMessageBase artifactMessageBase)
        {
            message = artifactMessageBase;
        }

        public override void Handle()
        {
            base.Handle();
            foreach (var connection in NetworkServer.connections)
            {
                if (connection == fromConnection) continue;
                if (!connection.isConnected) continue;
                connection.Send(message);
            }

            message.Handle();
        }

        public override void Deserialize(NetworkReader reader)
        {
            base.Deserialize(reader);
            message = reader.ReadMessage<ArtifactMessage>().message;
        }

        public override void Serialize(NetworkWriter writer)
        {
            base.Serialize(writer);
            writer.Write(new ArtifactMessage(message));
        }
    }

    public class NewAuthMessage : ArtifactMessageBase
    {
        private ArtifactMessageBase message;
        private NetworkIdentity target;

        public NewAuthMessage()
        {
        }

        public NewAuthMessage(NetworkIdentity identity, ArtifactMessageBase artifactMessageBase)
        {
            target = identity;
            message = artifactMessageBase;
        }

        public override void Handle()
        {
            base.Handle();
            message.SendToAuthority(target);
        }

        public override void Deserialize(NetworkReader reader)
        {
            base.Deserialize(reader);
            var obj = Util.FindNetworkObject(reader.ReadNetworkId());
            if (obj)
                target = obj.GetComponent<NetworkIdentity>();
            message = reader.ReadMessage<ArtifactMessage>().message;
        }

        public override void Serialize(NetworkWriter writer)
        {
            base.Serialize(writer);
            writer.Write(target.netId);
            writer.Write(new ArtifactMessage(message));
        }
    }

    internal class ArtifactMessage : MessageBase
    {
        public ArtifactMessageBase message;
        public uint Type;

        public ArtifactMessage()
        {
        }

        public ArtifactMessage(ArtifactMessageBase artifactMessageBase)
        {
            message = artifactMessageBase;
            Type = (uint)Array.IndexOf(NetworkManager.RegisteredMessages, message.GetType());
        }

        public override void Serialize(NetworkWriter writer)
        {
            base.Serialize(writer);
            writer.WritePackedUInt32(Type);
            writer.Write(message);
        }

        public override void Deserialize(NetworkReader reader)
        {
            base.Deserialize(reader);
            Type = reader.ReadPackedUInt32();
            var tmsg = (ArtifactMessageBase)Activator.CreateInstance(NetworkManager.RegisteredMessages[Type]);
            tmsg.Deserialize(reader);
            message = tmsg;
        }
    }
}