using LiteNetLib;
using System.Net;
using System.Net.Sockets;

namespace LabFusion.Network;

public abstract class LiteNetLibCommon : INetEventListener
{
    public NetManager netManager { get; protected set; }
    public virtual void Shutdown()
    {
        netManager?.DisconnectAll();
        netManager?.Stop();
        netManager = null;
    }
    public void Initialize()
    {
        netManager = new(this);
    }
    public abstract void OnPeerConnected(NetPeer peer);
    public abstract void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo);
    public abstract void OnNetworkError(IPEndPoint endPoint, SocketError socketError);
    public abstract void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod);
    public abstract void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType);
    public abstract void OnNetworkLatencyUpdate(NetPeer peer, int latency);
    public abstract void OnConnectionRequest(ConnectionRequest request);
}