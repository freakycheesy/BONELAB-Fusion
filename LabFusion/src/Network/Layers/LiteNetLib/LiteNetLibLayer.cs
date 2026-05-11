using Il2CppSLZ.Bonelab;
using LabFusion.Player;
using LabFusion.Utilities;
using LabFusion.Voice;
using LabFusion.Voice.Unity;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Net;
using System.Net.Sockets;
using static Il2Cpp.Interop;

namespace LabFusion.Network;

public class LiteNetLibLayer : NetworkLayer
{
    public const int port = 7778;
    public const string localAddress = "127.0.0.1";
    private VoiceManager voiceManager;
    public override IVoiceManager VoiceManager => voiceManager;
    public LiteNetLibServer server { get; private set; }
    public LiteNetLibClient client { get; private set; }
    public override string Title => "LiteNetLib";
    public override string Platform => "Empty";
    public override bool CheckSupported()
    {
        return true;
    }

    public override bool CheckValidation()
    {
        return true;
    }

    public override void Disconnect(string reason = "")
    {
        Shutdown();
    }
    private void Shutdown()
    {
        client?.Shutdown();
        server?.Shutdown();
    }


    public override void DisconnectUser(ulong platformID)
    {
        server.netManager.DisconnectPeer(server.netManager.GetPeerById((int)platformID));
    }

    public override void LogIn()
    {
    }

    public override void LogOut()
    {
    }

    public override void OnDeinitializeLayer()
    {
        voiceManager.Disable();
        voiceManager = null;
        Shutdown();
        InvokeLoggedOutEvent();
    }

    public override void OnInitializeLayer()
    {
        server ??= new();
        server.Initialize();
        client ??= new();
        client.Initialize();

        voiceManager = new UnityVoiceManager();
        voiceManager.Enable();
        InvokeLoggedInEvent();
    }
    public override void OnUpdateLayer()
    {
        server.netManager.PollEvents();
        client.netManager.PollEvents();
    }
    public override void StartServer()
    {
        server.Start(port);
        client.Connect(localAddress, port);
    }

    public override void SendToServer(NetworkChannel channel, NetMessage message)
    {
        byte[] data = message.ToByteArray();
        DeliveryMethod deliveryMethod = GetDeliveryMethod(channel);
        client.peer.Send(data, deliveryMethod);
    }

    public override void SendFromServer(byte userId, NetworkChannel channel, NetMessage message)
    {
        var id = PlayerIDManager.GetPlayerID(userId);

        if (id != null)
        {
            SendFromServer(id.PlatformID, channel, message);
        }
    }

    public override void BroadcastMessage(NetworkChannel channel, NetMessage message)
    {
        byte[] data = message.ToByteArray();
        DeliveryMethod deliveryMethod = GetDeliveryMethod(channel);

        server.netManager.SendToAll(data, deliveryMethod);
    }

    public override void SendFromServer(ulong userId, NetworkChannel channel, NetMessage message)
    {
        byte[] data = message.ToByteArray();
        DeliveryMethod deliveryMethod = GetDeliveryMethod(channel);

        var peer = server.netManager.GetPeerById((int)userId);
        peer.Send(data, deliveryMethod);
    }

    public override void JoinServerByCode(string address)
    {
        client.Connect(address, port);
    }

    public static DeliveryMethod GetDeliveryMethod(NetworkChannel channel) => channel == NetworkChannel.Reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
}
