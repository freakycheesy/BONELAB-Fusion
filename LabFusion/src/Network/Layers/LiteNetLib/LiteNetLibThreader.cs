using Il2CppSLZ.Marrow;
using Il2CppSLZ.ModIO.WebSockets;
using LabFusion.Player;
using LabFusion.Senders;
using LabFusion.UI.Popups;
using LiteNetLib;
using LiteNetLib.Utils;
using MelonLoader;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace LabFusion.Network;

internal static class LiteNetLibThreader
{
    private static Thread _lnlThread;

    /// <summary>
    /// Starts the Riptide Thread and begins polling messages/actions.
    /// </summary>
    internal static void StartThread()
    {
        _lnlThread = new Thread(LNLThread);

        if (_lnlThread.IsAlive)
            return;

        _isThreadAlive = true;

        _lnlThread.IsBackground = true;
        _lnlThread.Start();
    }

    /// <summary>
    /// Stops the Riptide Thread from running and clears all events in the action queue.
    /// </summary>
    internal static void KillThread()
    {
        _isThreadAlive = false;
    }

    // ANYTIME these are accessed outside of the Riptide Thread, please, please, please lock it, future people. (You shouldn't do that anyway but still)
    private static NetManager _client;
    private static NetManager _server;
    private static EventBasedNetListener _clientListener;
    private static EventBasedNetListener _serverListener;

    internal static readonly ConcurrentQueue<Tuple<byte[], DeliveryMethod>> ClientSendQueue = new();
    internal static readonly ConcurrentQueue<Tuple<byte[], DeliveryMethod, int, bool>> ServerSendQueue = new();

    private static bool _isThreadAlive = false;
    private static void LNLThread()
    {
        InitializeLNL();

        while (_isThreadAlive)
        {
            try
            {
                _client.PollEvents();
                _server.PollEvents();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to update Riptide with exception: {ex}");
            }

            while (!ClientSendQueue.IsEmpty)
            {
                if (ClientSendQueue.TryDequeue(out Tuple<byte[], DeliveryMethod> clientMessageTuple))
                {
                    byte[] data = clientMessageTuple.Item1;
                    DeliveryMethod deliveryMethod = clientMessageTuple.Item2;

                    try
                    {
                        _client.FirstPeer.Send(data, deliveryMethod);
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Error($"Failed to send message with exception: {ex}");
                    }
                }
            }

            while (!ServerSendQueue.IsEmpty)
            {
                if (ServerSendQueue.TryDequeue(out Tuple<byte[], DeliveryMethod, int, bool> serverMessageTuple))
                {
                    byte[] data = serverMessageTuple.Item1;
                    DeliveryMethod deliveryMethod = serverMessageTuple.Item2;
                    NetPeer peer = _server.GetPeerById(serverMessageTuple.Item3);
                    bool isBroadcast = serverMessageTuple.Item4;

                    try
                    {
                        if (isBroadcast && IsServerRunning)
                            _server.SendToAll(data, deliveryMethod);
                        else if (isBroadcast && !IsServerRunning)
                            _client.FirstPeer.Send(data, deliveryMethod);
                        else
                            peer.Send(data, deliveryMethod);
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Error($"Failed to send message with exception: {ex}");
                    }
                }
            }
        }

        DeinitializeLNL();
    }

    private static void InitializeLNL()
    {
#if DEBUG
            RiptideLogger.Initialize(MelonLogger.Msg, true);
#endif
        _clientListener = new();
        _serverListener = new();
        _client = new(_clientListener)
        {
            AutoRecycle = true,
        };
        _server = new(_serverListener)
        {
            AutoRecycle = true,
        };

        _clientListener.NetworkReceiveEvent += OnClientReceives;
        _serverListener.NetworkReceiveEvent += OnServerReceives;
        _serverListener.ConnectionRequestEvent += OnConnectionRequest;
        _clientListener.PeerDisconnectedEvent += OnDisconnected;
        _serverListener.PeerDisconnectedEvent += OnClientDisconnected;
        _serverListener.PeerConnectedEvent += OnClientConnected;
    }

    private static void OnConnectionRequest(ConnectionRequest request)
    {
        request.Accept();
    }

    private static void OnDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        LiteNetLibLayer.ActionQueue.Enqueue(new Action(() => NetworkHelper.Disconnect()));
    }

    private static void OnClientConnected(NetPeer peer)
    {
    }

    private static void OnClientDisconnected(NetPeer peer, DisconnectInfo info)
    {
        ulong ID = (ulong)peer.Id;

        LiteNetLibLayer.ActionQueue.Enqueue(new Action(() =>
        {
            if (ID == PlayerIDManager.LocalID)
                return;

            // Make sure the user hasn't previously disconnected
            if (PlayerIDManager.HasPlayerID(ID))
            {
                // Update the mod so it knows this user has left
                InternalServerHelpers.OnPlayerLeft(ID);

                // Send disconnect notif to everyone
                ConnectionSender.SendDisconnect(ID);
            }
        }));
    }

    private static void DeinitializeLNL()
    {
        _client?.Stop();
        _server?.Stop();
        _client = null;
        _server = null;
    }

    internal static bool IsServerRunning { get; private set; }
    internal static bool IsClientConnected { get; private set; }

    public static void StartServer()
    {
        lock (_client)
        {
            lock (_server)
            {
                _client.Start();
                _server.Start(LiteNetLibLayer.port);

                _clientListener.PeerConnectedEvent += OnConnect;
                _client.Connect("127.0.0.1", LiteNetLibLayer.port, "");

                void OnConnect(NetPeer peer)
                {
                    _clientListener.PeerConnectedEvent -= OnConnect;

                    _client.DisconnectTimeout = 30000;
                    _client.DisconnectOnUnreachable = false;
                    _server.DisconnectTimeout = 30000;

                    IsServerRunning = true;
                    IsClientConnected = true;

                    LiteNetLibLayer.ActionQueue.Enqueue(() =>
                    {
                        PlayerIDManager.SetLongID((ulong)_client.FirstPeer.Id);

                        InternalServerHelpers.OnStartServer();
                    });
                }
            }
        }
    }

    public static void StopServer()
    {
        lock (_server)
        {
            _server.Stop();

            IsServerRunning = false;
            IsClientConnected = false;
        }
    }

    private static bool _isConnecting;
    public static void ConnectToServer(string serverCode)
    {
        if (_isConnecting)
        {
            Notifier.Send(new Notification()
            {
                Message = "Client is still connecting! Please wait until connection is finalized or failed.",
                PopupLength = 5,
                SaveToMenu = false,
                Type = NotificationType.WARNING,
            });
            return;
        }

        if (IsClientConnected)
        {
            Notifier.Send(new Notification()
            {
                Message = "Disconnect from the current server before connecting to another!",
                PopupLength = 5,
                SaveToMenu = false,
                Type = NotificationType.WARNING,
            });
            return;
        }

        string ipString;

        if (IPAddress.TryParse(serverCode, out IPAddress ipAddress))
            ipString = serverCode;
        else
        {
            Notifier.Send(new Notification()
            {
                Message = "Server Code or IP Address incorrect! Make sure you used the right code/IP!",
                PopupLength = 5,
                SaveToMenu = false,
                Type = NotificationType.WARNING,
            });
            return;
        }

        lock (_client)
        {
            _client.Start();
            _clientListener.PeerConnectedEvent += OnConnect;
            _clientListener.NetworkErrorEvent += OnConnectionFailed;
            _client.Connect(ipString, LiteNetLibLayer.port, "");
            _isConnecting = true;

            void OnConnect(NetPeer peer)
            {
                _clientListener.PeerConnectedEvent -= OnConnect;
                _clientListener.NetworkErrorEvent -= OnConnectionFailed;

                _isConnecting = false;

                _client.DisconnectTimeout = 30000;
                _client.DisconnectOnUnreachable = false;

                IsClientConnected = true;

                LiteNetLibLayer.ActionQueue.Enqueue(() =>
                {
                    PlayerIDManager.SetLongID((ulong)_client.FirstPeer.Id);

                    ConnectionSender.SendConnectionRequest();
                });
            }

            void OnConnectionFailed(IPEndPoint endPoint, SocketError socketError)
            {
                _clientListener.PeerConnectedEvent -= OnConnect;
                _clientListener.NetworkErrorEvent -= OnConnectionFailed;

                _isConnecting = false;

                Notifier.Send(new Notification()
                {
                    Title = "Failed to Connect!",
                    Message = $"REASON: {socketError}",
                    SaveToMenu = false,
                    PopupLength = 5,
                    Type = NotificationType.ERROR,
                });
            }
        }
    }

    public static void Disconnect(string reason = "")
    {
        lock (_client)
        {
            lock (_server)
            {
                if (!IsClientConnected)
                    return;

                _server.Stop();
                _client.Stop();

                IsClientConnected = false;
                IsServerRunning = false;

                InternalServerHelpers.OnDisconnect(reason);
            }
        }
    }

    private static void OnServerReceives(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        LiteNetLibLayer.MessageQueue.Enqueue(new Tuple<byte[], bool>(reader.GetRemainingBytes(), true));
    }

    private static void OnClientReceives(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        LiteNetLibLayer.MessageQueue.Enqueue(new Tuple<byte[], bool>(reader.GetRemainingBytes(), false));
    }

    internal static void KickPlayer(ulong platformID)
    {
        lock (_server)
        {
            _server.DisconnectPeer(_server.GetPeerById((int)platformID));
        }
    }
}
