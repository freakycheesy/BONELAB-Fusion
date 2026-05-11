using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace LabFusion.Network;

public class LiteNetLibClient : LiteNetLibCommon
{
    public NetPeer peer;

    public override void Shutdown()
    {
        peer?.Disconnect();
        peer = null;
        base.Shutdown();
    }
    public override void OnConnectionRequest(ConnectionRequest request)
    {
    }

    public override void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
    }

    public override void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
    }

    public override void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        ReadableMessage message = new()
        {
            Buffer = reader.GetRemainingBytes(),
            IsServerHandled = false,
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
    }

    public override void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
    }

    public void Connect(string address, int port)
    {
        peer = netManager.Connect(address, port, "");
    }
}