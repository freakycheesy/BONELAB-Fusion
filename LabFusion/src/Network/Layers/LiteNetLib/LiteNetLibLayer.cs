using Il2CppTMPro;
using LabFusion.Menu;
using LabFusion.Player;
using LabFusion.Senders;
using LabFusion.UI.Popups;
using LabFusion.Utilities;
using LabFusion.Voice;
using LabFusion.Voice.Unity;
using LiteNetLib;
using System.Collections.Concurrent;
using UnityEngine;

namespace LabFusion.Network;

public class LiteNetLibLayer : NetworkLayer
{
    public const int port = 7778;
    public const string localAddress = "127.0.0.1";
    private IVoiceManager voiceManager = null;
    public override IVoiceManager VoiceManager => voiceManager;


    internal static readonly ConcurrentQueue<Tuple<byte[], bool>> MessageQueue = new ConcurrentQueue<Tuple<byte[], bool>>();

    internal static readonly ConcurrentQueue<Action> ActionQueue = new ConcurrentQueue<Action>();

    public override string Title => "LiteNetLib";

    public override string Platform => "P2P";

    public override bool IsHost => LiteNetLibThreader.IsServerRunning;

    public override bool IsClient => LiteNetLibThreader.IsClientConnected;

    private string ServerCode
    {
        get; set;
    }
    public override bool CheckSupported() => true;
    public override bool CheckValidation() => true;

    // Riptide doesn't really have a way to add these features out of the box...
    public override string GetUsername(ulong userId) => userId.ToString();
    public override bool IsFriend(ulong userId) => false;

    public override void LogIn() => InvokeLoggedInEvent();
    public override void LogOut() => InvokeLoggedOutEvent();

    public override void OnInitializeLayer()
    {
        LiteNetLibThreader.StartThread();
        HookRiptideEvents();


        voiceManager = new UnityVoiceManager();
        voiceManager.Enable();
    }

    public override void OnDeinitializeLayer()
    {
        voiceManager.Disable();
        voiceManager = null;

        Disconnect();

        LiteNetLibThreader.KillThread();

        UnhookRiptideEvents();
    }

    private void HookRiptideEvents()
    {
        // Add server hooks
        MultiplayerHooking.OnPlayerJoined += OnPlayerJoin;
        MultiplayerHooking.OnPlayerLeft += OnPlayerLeave;
        MultiplayerHooking.OnDisconnected += OnDisconnect;
    }

    private void UnhookRiptideEvents()
    {
        // Remove server hooks
        MultiplayerHooking.OnPlayerJoined -= OnPlayerJoin;
        MultiplayerHooking.OnPlayerLeft -= OnPlayerLeave;
        MultiplayerHooking.OnDisconnected -= OnDisconnect;
    }

    private void OnPlayerJoin(PlayerID id)
    {
        if (VoiceManager == null)
        {
            return;
        }

        if (!id.IsMe)
        {
            VoiceManager.GetSpeaker(id);
        }
    }

    private void OnPlayerLeave(PlayerID id)
    {
        if (VoiceManager == null)
        {
            return;
        }

        VoiceManager.RemoveSpeaker(id);
    }

    private void OnDisconnect()
    {
        VoiceManager.ClearManager();
    }
    public override void OnUpdateLayer()
    {
        for (int i = 0; i < MessageQueue.Count; i++)
        {
            if (MessageQueue.TryDequeue(out Tuple<byte[], bool> messageTuple))
            {
                byte[] bytes = messageTuple.Item1;
                bool isServerHandled = messageTuple.Item2;
                ReadableMessage message = new();
                message.Buffer = bytes;
                message.IsServerHandled = isServerHandled;
                NativeMessageHandler.ReadMessage(message);
            }
        }

        for (int i = 0; i < ActionQueue.Count; i++)
            if (ActionQueue.TryDequeue(out Action action))
                action();
    }

    public override void StartServer()
    {
        LiteNetLibThreader.StartServer();
    }

    public override void Disconnect(string reason = "")
    {
        LiteNetLibThreader.Disconnect();
    }

    public override string GetServerCode() => ServerCode = localAddress;

    public override void RefreshServerCode()
    {
        ServerCode = localAddress;
        GUIUtility.systemCopyBuffer = ServerCode;

        Notifier.Send(new Notification()
        {
            SaveToMenu = false,
            Message = $"For security purposes, Look up your IP Address and Open a port on {port}!",
            Type = NotificationType.INFORMATION,
            PopupLength = 3,
        });
    }

    public override void JoinServerByCode(string code)
    {
        if (code == "1") code = localAddress;
        LiteNetLibThreader.ConnectToServer(code);
    }

    private static DeliveryMethod GetDeliveryMethod(NetworkChannel channel)
    {
        return channel switch
        {
            NetworkChannel.Reliable => DeliveryMethod.ReliableOrdered,
            NetworkChannel.Unreliable => DeliveryMethod.Unreliable,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
        };
    }

    public override void BroadcastMessage(NetworkChannel channel, NetMessage message)
    {
        byte[] data = message.ToByteArray();
        DeliveryMethod deliveryMethod = GetDeliveryMethod(channel);

        var messageTuple = new Tuple<byte[], DeliveryMethod, int, bool>(data, deliveryMethod, 0, true);

        LiteNetLibThreader.ServerSendQueue.Enqueue(messageTuple);
    }

    public override void SendFromServer(byte userId, NetworkChannel channel, NetMessage message)
    {
        var id = PlayerIDManager.GetPlayerID(userId);

        if (id != null)
        {
            SendFromServer(id.PlatformID, channel, message);
        }
    }

    public override void SendFromServer(ulong userId, NetworkChannel channel, NetMessage message)
    {
        byte[] data = message.ToByteArray();
        DeliveryMethod deliveryMethod = GetDeliveryMethod(channel);

        var messageTuple = new Tuple<byte[], DeliveryMethod, int, bool>(data, deliveryMethod, (int)userId, false);

        LiteNetLibThreader.ServerSendQueue.Enqueue(messageTuple);
    }

    public override void SendToServer(NetworkChannel channel, NetMessage message)
    {
        byte[] data = message.ToByteArray();
        DeliveryMethod deliveryMethod = GetDeliveryMethod(channel);

        var messageTuple = new Tuple<byte[], DeliveryMethod>(data, deliveryMethod);
        if (IsHost)
            NativeMessageHandler.ReadMessage(new()
            {
                Buffer = data,
                IsServerHandled = true,
            });
        else
            LiteNetLibThreader.ClientSendQueue.Enqueue(messageTuple);
    }

    public override void DisconnectUser(ulong platformID)
    {
        LiteNetLibThreader.KickPlayer(platformID);
    }
}
