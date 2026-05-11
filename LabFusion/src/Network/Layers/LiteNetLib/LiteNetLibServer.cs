using LabFusion.Utilities;
using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LabFusion.Network;

public class LiteNetLibServer : LiteNetLibCommon
{
    public override void OnConnectionRequest(ConnectionRequest request)
    {
        request.Accept();
    }

    public override void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
    }

    public override void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        FusionLogger.Log($"Network Latency! Peer: {peer.Id} Latency: {latency}");
    }

    public override void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        ReadableMessage message = new()
        {
            Buffer = reader.GetRemainingBytes(),
            IsServerHandled = true,
            PlatformID = (ulong?)peer.Id,
        };
        NativeMessageHandler.ReadMessage(message);
        reader.Recycle();
    }

    public override void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
    }

    public override void OnPeerConnected(NetPeer peer)
    {
        FusionLogger.Log($"Peer Connected: {peer.Id}");
    }

    public override void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        FusionLogger.Log($"Peer Disconnected: {peer.Id}");
    }

    public void Start(int port)
    {
        netManager.Start(port);
    }
}