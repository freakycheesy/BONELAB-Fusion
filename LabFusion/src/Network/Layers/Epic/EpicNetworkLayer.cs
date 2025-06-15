using LabFusion.Data;
using LabFusion.Player;
using LabFusion.Utilities;
using LabFusion.UI.Popups;

using LabFusion.Senders;
using LabFusion.Voice;
using LabFusion.Voice.Unity;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Sessions;

namespace LabFusion.Network;

public abstract class EpicNetworkLayer : NetworkLayer
{
    public abstract uint ApplicationID { get; }

    public const int ReceiveBufferSize = 32;

    public override string Title => "Epic";

    public override string Platform => "Epic";

    public override bool RequiresValidId => true;

    public override bool IsHost => _isServerActive;
    public override bool IsClient => _isConnectionActive;

    private INetworkLobby _currentLobby;
    public override INetworkLobby Lobby => _currentLobby;

    private IVoiceManager _voiceManager = null;
    public override IVoiceManager VoiceManager => _voiceManager;

    private IMatchmaker _matchmaker = null;
    public override IMatchmaker Matchmaker => _matchmaker;

    public ProductUserId SteamId;

    public static EpicSocketManager SteamSocket;
    public static EpicConnectionManager SteamConnection;

    protected bool _isServerActive = false;
    protected bool _isConnectionActive = false;

    protected ulong _targetServerId;

    protected string _targetJoinId;

    protected bool _isInitialized = false;

    // A local reference to a lobby
    // This isn't actually used for joining servers, just for matchmaking
    protected object _localLobby;
    public static PlatformInterface platformInterface;

    public override bool CheckSupported()
    {
        return !PlatformHelper.IsAndroid;
    }

    public override bool CheckValidation()
    {
        return SteamAPILoader.HasSteamAPI;
    }

    public override void OnInitializeLayer()
    {
        InitEOS();
        if (!SteamId.IsValid())
        {
            FusionLogger.Error("Steamworks failed to initialize!");
            return;
        }

        // Get steam information
        SteamId = platformInterface.GetConnectInterface().userid;
        PlayerIDManager.SetLongID((ulong)SteamId);
        LocalPlayer.Username = GetUsername(SteamId.Value);

        FusionLogger.Log($"Steamworks initialized with SteamID {SteamId} and ApplicationID {ApplicationID}!");

        SteamNetworkingUtils.InitRelayNetworkAccess();

        HookSteamEvents();

        // Create managers
        _voiceManager = new UnityVoiceManager();
        _voiceManager.Enable();

        _matchmaker = new EpicMatchmaker();

        // Set initialized
        _isInitialized = true;
    }

    private void InitEOS() {
        // Set these values as appropriate. For more information, see the Developer Portal documentation.
        string productName = "Fusion";
        string productVersion = FusionMod.Version.ToString();
        string productId = "9d11e30b5f604b68af59bbfd376afcf8";
        string sandboxId = "5706f401d54347d08132af6eb267c8d2";
        string deploymentId = "d763553e75954e988f659ac8dbe9b9b1";
        string clientId = "xyza7891PlpZwqvEZZ92EH15ffehM7j9";
        string clientSecret = "kqJJwObDU0xIz0rnh6sqy7J9x0/jlWsLoqS4BN804bQ";

        var initializeOptions = new Epic.OnlineServices.Platform.InitializeOptions() {
            ProductName = productName,
            ProductVersion = productVersion
        };

        var initializeResult = Epic.OnlineServices.Platform.PlatformInterface.Initialize(ref initializeOptions);
        if (initializeResult != Epic.OnlineServices.Result.Success) {
            throw new System.Exception("Failed to initialize platform: " + initializeResult);
        }

        // The SDK outputs lots of information that is useful for debugging.
        // Make sure to set up the logging interface as early as possible: after initializing.
        Epic.OnlineServices.Logging.LoggingInterface.SetLogLevel(Epic.OnlineServices.Logging.LogCategory.AllCategories, Epic.OnlineServices.Logging.LogLevel.VeryVerbose);
        Epic.OnlineServices.Logging.LoggingInterface.SetCallback((ref Epic.OnlineServices.Logging.LogMessage logMessage) =>
        {
            System.Console.WriteLine(logMessage.Message);
        });

        var options = new Epic.OnlineServices.Platform.Options() {
            ProductId = productId,
            SandboxId = sandboxId,
            DeploymentId = deploymentId,
            ClientCredentials = new Epic.OnlineServices.Platform.ClientCredentials() {
                ClientId = clientId,
                ClientSecret = clientSecret
            }
        };

        platformInterface = Epic.OnlineServices.Platform.PlatformInterface.Create(ref options);
        if (platformInterface == null) {
            throw new System.Exception("Failed to create platform");
        }

        var loginCredentialType = Epic.OnlineServices.Auth.LoginCredentialType.AccountPortal;
        /// These fields correspond to <see cref="Epic.OnlineServices.Auth.Credentials.Id" /> and <see cref="Epic.OnlineServices.Auth.Credentials.Token" />,
        /// and their use differs based on the login type. For more information, see <see cref="Epic.OnlineServices.Auth.Credentials" />
        /// and the Auth Interface documentation.
        string loginCredentialId = null;
        string loginCredentialToken = null;

        var authInterface = platformInterface.GetAuthInterface();
        if (authInterface == null) {
            throw new System.Exception("Failed to get auth interface");
        }

        var loginOptions = new Epic.OnlineServices.Auth.LoginOptions() {
            Credentials = new Epic.OnlineServices.Auth.Credentials() {
                Type = loginCredentialType,
                Id = loginCredentialId,
                Token = loginCredentialToken
            },
            // Change these scopes to match the ones set up on your product on the Developer Portal.
            ScopeFlags = Epic.OnlineServices.Auth.AuthScopeFlags.BasicProfile | Epic.OnlineServices.Auth.AuthScopeFlags.FriendsList | Epic.OnlineServices.Auth.AuthScopeFlags.Presence
        };

        // Ensure platform tick is called on an interval, or the following call will never callback.
        authInterface.Login(ref loginOptions, null, (ref Epic.OnlineServices.Auth.LoginCallbackInfo loginCallbackInfo) =>
        {
            if (loginCallbackInfo.ResultCode == Epic.OnlineServices.Result.Success) {
                System.Console.WriteLine("Login succeeded");
            }
            else if (Epic.OnlineServices.Common.IsOperationComplete(loginCallbackInfo.ResultCode)) {
                System.Console.WriteLine("Login failed: " + loginCallbackInfo.ResultCode);
            }
        });
        tick = true;
    }

    public override void OnDeinitializeLayer()
    {
        _voiceManager.Disable();
        _voiceManager = null;

        _matchmaker = null;

        _localLobby = default;
        _currentLobby = null;

        Disconnect();

        UnHookSteamEvents();
        tick = false;
    }

    public static bool tick;

    public override void LogIn()
    {
        if (SteamId.IsValid())
        {
            return;
        }

        // Shutdown the game's steam client, if available
        if (GameHasSteamworks())
        {
            ShutdownGameClient();
        }

        bool succeeded;

        try
        {
            succeeded = true;
        }
        catch (Exception e)
        {
            FusionLogger.LogException("initializing Steamworks", e);

            succeeded = false;
        }

        if (!succeeded)
        {
            Notifier.Send(new Notification()
            {
                Title = "Log In Failed",
                Message = "Failed connecting to Steamworks! Make sure Steam is running and signed in!",
                SaveToMenu = false,
                ShowPopup = true,
                Type = NotificationType.ERROR,
                PopupLength = 6f,
            });

            InvokeLoggedOutEvent();
            return;
        }

        InvokeLoggedInEvent();
    }

    public override void LogOut()
    {
        tick = false;

        InvokeLoggedOutEvent();
    }

    private const string STEAMWORKS_ASSEMBLY_NAME = "Il2CppFacepunch.Steamworks.Win64";

    private static bool GameHasSteamworks()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            if (assembly.FullName.StartsWith(STEAMWORKS_ASSEMBLY_NAME))
            {
                return true;
            }
        }

        return false;
    }

    private static void ShutdownGameClient()
    {
        FusionLogger.Log("Shutting down the game's Steamworks instance...");
        tick = false;
    }

    public override void OnUpdateLayer()
    {
        // Receive any needed messages
        try
        {
            if(platformInterface != null && tick)
                platformInterface.Tick();
        }
        catch (Exception e)
        {
            FusionLogger.LogException("receiving data on Socket and Connection", e);
        }
    }

    public override string GetUsername(ProductUserId userId)
    {
        return userId.InnerHandle.ToString();
    }

    public override bool IsFriend(ulong userId)
    {
        return userId == PlayerIDManager.LocalPlatformID || new Friend(userId).IsFriend;
    }

    public override void BroadcastMessage(NetworkChannel channel, NetMessage message)
    {
        if (IsHost)
        {
            EpicSocketHandler.BroadcastToClients(SteamSocket, channel, message);
        }
        else
        {
            EpicSocketHandler.BroadcastToServer(channel, message);
        }
    }

    public override void SendToServer(NetworkChannel channel, NetMessage message)
    {
        EpicSocketHandler.BroadcastToServer(channel, message);
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
        // Make sure this is actually the server
        if (!IsHost)
        {
            return;
        }

        // Get the connection from the userid dictionary
        if (SteamSocket.ConnectedSteamIds.TryGetValue(userId, out var connection))
        {
            SteamSocket.SendToClient(connection, channel, message);
        }
    }

    public override void StartServer() {
        CreateLobbyOptions options = new CreateLobbyOptions();
        options.DisableHostMigration = false;
        SteamSocket = platformInterface.GetLobbyInterface().CreateLobby(ref options, null, null);

        // Host needs to connect to own socket server with a ConnectionManager to send/receive messages
        // Relay Socket servers are created/connected to through SteamIds rather than "Normal" Socket Servers which take IP addresses
        SteamConnection = SteamNetworkingSockets.ConnectRelay<EpicConnectionManager>(SteamId);
        _isServerActive = true;
        _isConnectionActive = true;

        // Call server setup
        InternalServerHelpers.OnStartServer();

        RefreshServerCode();
    }

    public void JoinServer(string serverId)
    {
        // Leave existing server
        if (_isConnectionActive || _isServerActive)
            Disconnect();

        JoinLobbyByIdOptions options = new();
        options.LobbyId = serverId;
        platformInterface.GetLobbyInterface().JoinLobbyById(ref options, null, null);

        _isServerActive = false;
        _isConnectionActive = true;

        ConnectionSender.SendConnectionRequest();
    }

    public override void Disconnect(string reason = "")
    {
        // Make sure we are currently in a server
        if (!_isServerActive && !_isConnectionActive)
            return;

        try
        {
            SteamConnection?.Close();

            SteamSocket?.Close();
        }
        catch
        {
            FusionLogger.Log("Error closing socket server / connection manager");
        }

        _isServerActive = false;
        _isConnectionActive = false;

        InternalServerHelpers.OnDisconnect(reason);
    }

    public string ServerCode { get; private set; } = null;

    public override string GetServerCode()
    {
        return ServerCode;
    }

    public override void RefreshServerCode()
    {
        ServerCode = RandomCodeGenerator.GetString(8);

        LobbyInfoManager.PushLobbyUpdate();
    }

    public override void JoinServerByCode(string code)
    {
        if (Matchmaker == null)
        {
            return;
        }

#if DEBUG
        FusionLogger.Log($"Searching for servers with code {code}...");
#endif

        Matchmaker.RequestLobbies((info) =>
        {
            foreach (var lobby in info.Lobbies)
            {
                var lobbyCode = lobby.Metadata.LobbyInfo.LobbyCode;
                var inputCode = code;

#if DEBUG
                FusionLogger.Log($"Found server with code {lobbyCode}");
#endif

                // Case insensitive
                // Makes it easier to input
                if (lobbyCode.ToLower() == code.ToLower())
                {
                    JoinServer(lobby.Metadata.LobbyInfo.LobbyId);
                    break;
                }
            }
        });
    }

    private void HookSteamEvents()
    {
        // Add server hooks
        MultiplayerHooking.OnPlayerJoined += OnPlayerJoin;
        MultiplayerHooking.OnPlayerLeft += OnPlayerLeave;
        MultiplayerHooking.OnDisconnected += OnDisconnect;

        LobbyInfoManager.OnLobbyInfoChanged += OnUpdateLobby;

        // Create a local lobby
        AwaitLobbyCreation();
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
        if (VoiceManager == null)
        {
            return;
        }

        VoiceManager.ClearManager();
    }

    private void UnHookSteamEvents()
    {
        // Remove server hooks
        MultiplayerHooking.OnPlayerJoined -= OnPlayerJoin;
        MultiplayerHooking.OnPlayerLeft -= OnPlayerLeave;
        MultiplayerHooking.OnDisconnected -= OnDisconnect;

        LobbyInfoManager.OnLobbyInfoChanged -= OnUpdateLobby;

        // Remove the local lobby
        if (_localLobby.Id == SteamId)
        {
            _localLobby.Leave();
        }
    }

    private async void AwaitLobbyCreation()
    {
        var lobbyTask = await SteamMatchmaking.CreateLobbyAsync();

        if (!lobbyTask.HasValue)
        {
#if DEBUG
            FusionLogger.Log("Failed to create a steam lobby!");
#endif
            return;
        }

        _localLobby = lobbyTask.Value;
        _currentLobby = new EpicLobby(_localLobby);
    }

    public void OnUpdateLobby()
    {
        // Make sure the lobby exists
        if (Lobby == null)
        {
#if DEBUG
            FusionLogger.Warn("Tried updating the steam lobby, but it was null!");
#endif
            return;
        }

        // Write active info about the lobby
        LobbyMetadataHelper.WriteInfo(Lobby);
    }
}