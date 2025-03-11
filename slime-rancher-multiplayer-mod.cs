using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using MonomiPark.SlimeRancher.Regions;
using MonomiPark.SlimeRancher.DataModel;

namespace SlimeRancherMultiplayer
{
    public class MultiplayerMod : MonoBehaviour
    {
        // Configuration
        private const int DEFAULT_PORT = 7777;
        private const int MAX_PLAYERS = 4;
        private const float SYNC_INTERVAL = 0.1f; // Seconds between sync

        // Networking components
        private NetworkManager networkManager;
        private Server server;
        private Client client;

        // Player management
        private Dictionary<int, RemotePlayer> remotePlayers = new Dictionary<int, RemotePlayer>();
        private int localPlayerId;
        private bool isHost = false;

        // UI components
        private MultiplayerUI ui;

        void Awake()
        {
            // Register mod with the game
            Debug.Log("Slime Rancher Multiplayer Mod initializing...");

            // Initialize UI
            ui = gameObject.AddComponent<MultiplayerUI>();

            // Initialize network manager
            networkManager = gameObject.AddComponent<NetworkManager>();

            // Register event handlers
            if (SceneContext.Instance != null && SceneContext.Instance.GameModel != null)
                SceneContext.Instance.GameModel.onPause += OnGamePause;
        }

        void OnDestroy()
        {
            // Clean up resources
            StopMultiplayer();

            // Unregister event handlers
            if (SceneContext.Instance != null && SceneContext.Instance.GameModel != null)
                SceneContext.Instance.GameModel.onPause -= OnGamePause;
        }

        void Update()
        {
            // Process networking events
            if (server != null)
                server.ProcessMessages();

            if (client != null)
                client.ProcessMessages();

            // Update remote players
            foreach (var player in remotePlayers.Values)
                player.Update();
        }

        public void HostGame(string playerName, int port = DEFAULT_PORT)
        {
            StopMultiplayer(); // Clean up existing connections

            // Initialize server
            server = new Server(port, MAX_PLAYERS);
            server.OnPlayerConnected += OnPlayerConnected;
            server.OnPlayerDisconnected += OnPlayerDisconnected;
            server.OnDataReceived += OnDataReceived;
            server.Start();

            // Initialize local client connection to server
            client = new Client();
            client.OnConnected += OnConnectedToServer;
            client.OnDisconnected += OnDisconnectedFromServer;
            client.OnDataReceived += OnDataReceived;
            client.Connect("127.0.0.1", port, playerName);

            isHost = true;
            Debug.Log($"Hosting game on port {port}");

            // Start syncing game state
            InvokeRepeating("SyncGameState", 0f, SYNC_INTERVAL);
        }

        public void JoinGame(string playerName, string hostAddress, int port = DEFAULT_PORT)
        {
            StopMultiplayer(); // Clean up existing connections

            // Initialize client
            client = new Client();
            client.OnConnected += OnConnectedToServer;
            client.OnDisconnected += OnDisconnectedFromServer;
            client.OnDataReceived += OnDataReceived;
            client.Connect(hostAddress, port, playerName);

            isHost = false;
            Debug.Log($"Joining game at {hostAddress}:{port}");

            // Start sending player updates
            InvokeRepeating("SendPlayerUpdate", 0f, SYNC_INTERVAL);
        }

        public void StopMultiplayer()
        {
            // Stop update loops
            CancelInvoke("SyncGameState");
            CancelInvoke("SendPlayerUpdate");

            // Clean up networking
            if (server != null)
            {
                server.Stop();
                server = null;
            }

            if (client != null)
            {
                client.Disconnect();
                client = null;
            }

            // Clean up remote players
            foreach (var player in remotePlayers.Values)
                Destroy(player.gameObject);

            remotePlayers.Clear();
            isHost = false;
        }

        #region Network Event Handlers

        private void OnPlayerConnected(int playerId, string playerName)
        {
            Debug.Log($"Player connected: {playerName} (ID: {playerId})");

            // Instantiate remote player if not local
            if (playerId != localPlayerId)
            {
                SpawnRemotePlayer(playerId, playerName);

                // If we're host, send the current game state to the new player
                if (isHost)
                    SendGameStateToPlayer(playerId);
            }

            // Notify UI
            ui.AddPlayerToList(playerId, playerName);
        }

        private void OnPlayerDisconnected(int playerId)
        {
            Debug.Log($"Player disconnected: {playerId}");

            // Remove remote player
            if (remotePlayers.ContainsKey(playerId))
            {
                Destroy(remotePlayers[playerId].gameObject);
                remotePlayers.Remove(playerId);
            }

            // Notify UI
            ui.RemovePlayerFromList(playerId);
        }

        private void OnConnectedToServer(int assignedPlayerId)
        {
            Debug.Log($"Connected to server. Assigned ID: {assignedPlayerId}");
            localPlayerId = assignedPlayerId;

            // Notify UI
            ui.SetConnectionStatus(true);
        }

        private void OnDisconnectedFromServer()
        {
            Debug.Log("Disconnected from server");

            // Clean up and return to single-player mode
            StopMultiplayer();

            // Notify UI
            ui.SetConnectionStatus(false);
            ui.ShowDisconnectMessage();
        }

        private void OnDataReceived(int senderId, byte[] data)
        {
            // Deserialize and process message
            NetworkMessage message = NetworkMessage.Deserialize(data);

            switch (message.Type)
            {
                case MessageType.PlayerUpdate:
                    HandlePlayerUpdate(senderId, message as PlayerUpdateMessage);
                    break;

                case MessageType.SlimeUpdate:
                    HandleSlimeUpdate(message as SlimeUpdateMessage);
                    break;

                case MessageType.ResourceUpdate:
                    HandleResourceUpdate(message as ResourceUpdateMessage);
                    break;

                case MessageType.GameState:
                    HandleGameState(message as GameStateMessage);
                    break;

                case MessageType.Chat:
                    HandleChatMessage(senderId, message as ChatMessage);
                    break;
            }
        }

        #endregion

        #region Game Sync Methods

        private void SyncGameState()
        {
            if (!isHost || client == null)
                return;

            // 1. Sync slimes (position, type, mood)
            SyncSlimes();

            // 2. Sync resources (fruits, veggies, etc.)
            SyncResources();

            // 3. Sync player state (position, inventory)
            SendPlayerUpdate();
        }

        private void SyncSlimes()
        {
            // Get all active slimes in the scene
            var slimeActors = SceneContext.Instance.SlimeAppearanceDirector.SlimeDefinitions
                .SelectMany(sd => UnityEngine.Object.FindObjectsOfType<SlimeEat>()
                .Where(se => se.SlimeDefinition == sd))
                .Select(se => se.GetComponent<Identifiable>())
                .Where(id => id != null)
                .ToList();

            // Create batch update message
            var message = new SlimeUpdateMessage();

            foreach (var slime in slimeActors.Take(50)) // Limit batch size
            {
                message.SlimeUpdates.Add(new SlimeData
                {
                    NetworkId = GetNetworkId(slime),
                    Position = slime.transform.position,
                    Rotation = slime.transform.rotation,
                    SlimeType = slime.id.ToString(),
                    // Additional slime properties as needed
                });
            }

            // Send to all clients
            server.BroadcastMessage(message.Serialize());
        }

        private void SyncResources()
        {
            // Get all relevant resources
            var resources = UnityEngine.Object.FindObjectsOfType<Identifiable>()
                .Where(id => id.id.IsFood() || id.id.IsPlort() || id.id.IsResource())
                .Take(50) // Limit batch size
                .ToList();

            // Create batch update message
            var message = new ResourceUpdateMessage();

            foreach (var resource in resources)
            {
                message.ResourceUpdates.Add(new ResourceData
                {
                    NetworkId = GetNetworkId(resource),
                    Position = resource.transform.position,
                    Rotation = resource.transform.rotation,
                    ResourceType = resource.id.ToString()
                });
            }

            // Send to all clients
            server.BroadcastMessage(message.Serialize());
        }

        private void SendPlayerUpdate()
        {
            if (client == null)
                return;

            // Get player state
            var player = SceneContext.Instance.PlayerState;

            // Create player update message
            var message = new PlayerUpdateMessage
            {
                Position = player.position,
                Rotation = player.GetComponent<FirstPersonController>().rotY,
                Health = player.GetComponent<PlayerHealth>().GetCurrentHealth(),
                Energy = player.GetComponent<PlayerEnergy>().GetCurrEnergy(),
                EquippedSlot = player.Ammo.GetSelectedAmmoIdx(),
                InventoryItems = GetInventoryItems(player)
            };

            // Send to server (which will relay to other clients)
            client.SendMessage(message.Serialize());
        }

        private List<InventoryItemData> GetInventoryItems(PlayerState player)
        {
            var items = new List<InventoryItemData>();

            // Add equipped items
            for (int i = 0; i < player.Ammo.GetSlotCount(); i++)
            {
                var ammo = player.Ammo.GetSlotName(i);
                var count = player.Ammo.GetSlotCount(i);

                if (count > 0)
                {
                    items.Add(new InventoryItemData
                    {
                        Slot = i,
                        ItemType = ammo,
                        Count = count
                    });
                }
            }

            return items;
        }

        private void SendGameStateToPlayer(int playerId)
        {
            // Create comprehensive game state message
            var message = new GameStateMessage
            {
                GameTime = SceneContext.Instance.TimeDirector.WorldTime(),
                Weather = SceneContext.Instance.WeatherDirector.CurrWeather().ToString(),
                // Add other relevant game state data
            };

            // Add all slimes
            SyncSlimes();

            // Add all resources
            SyncResources();

            // Send to specific player
            server.SendMessage(playerId, message.Serialize());
        }

        #endregion

        #region Message Handlers

        private void HandlePlayerUpdate(int playerId, PlayerUpdateMessage message)
        {
            if (playerId == localPlayerId)
                return; // Ignore our own updates

            // Update or create remote player
            if (!remotePlayers.ContainsKey(playerId))
            {
                SpawnRemotePlayer(playerId, $"Player {playerId}");
            }

            var remotePlayer = remotePlayers[playerId];
            remotePlayer.UpdateState(message);
        }

        private void HandleSlimeUpdate(SlimeUpdateMessage message)
        {
            if (isHost)
                return; // Host manages slimes directly

            foreach (var slimeData in message.SlimeUpdates)
            {
                // Find or spawn slime with network ID
                var slime = GetOrSpawnNetworkObject<Identifiable>(slimeData.NetworkId);

                if (slime != null)
                {
                    // Update slime
                    slime.transform.position = slimeData.Position;
                    slime.transform.rotation = slimeData.Rotation;

                    // Additional slime properties update
                }
            }
        }

        private void HandleResourceUpdate(ResourceUpdateMessage message)
        {
            if (isHost)
                return; // Host manages resources directly

            foreach (var resourceData in message.ResourceUpdates)
            {
                // Find or spawn resource with network ID
                var resource = GetOrSpawnNetworkObject<Identifiable>(resourceData.NetworkId, resourceData.ResourceType);

                if (resource != null)
                {
                    // Update resource
                    resource.transform.position = resourceData.Position;
                    resource.transform.rotation = resourceData.Rotation;
                }
            }
        }

        private void HandleGameState(GameStateMessage message)
        {
            // Update game state from host
            if (isHost)
                return;

            // Set game time
            SceneContext.Instance.TimeDirector.SetWorldTime(message.GameTime);

            // Set weather
            Enum.TryParse(message.Weather, out WeatherType weather);
            SceneContext.Instance.WeatherDirector.SetWeather(weather);

            // Other game state updates
        }

        private void HandleChatMessage(int senderId, ChatMessage message)
        {
            string senderName = senderId == localPlayerId ? "You" :
                                remotePlayers.ContainsKey(senderId) ? remotePlayers[senderId].PlayerName : $"Player {senderId}";

            // Display chat message in UI
            ui.AddChatMessage(senderName, message.Text);
        }

        #endregion

        #region Helper Methods

        private void SpawnRemotePlayer(int playerId, string playerName)
        {
            // Create a game object for the remote player
            var playerObj = new GameObject($"RemotePlayer_{playerId}");
            var remotePlayer = playerObj.AddComponent<RemotePlayer>();
            remotePlayer.Initialize(playerId, playerName);

            // Add to tracked remote players
            remotePlayers[playerId] = remotePlayer;
        }

        private T GetOrSpawnNetworkObject<T>(string networkId, string typeName = null) where T : Component
        {
            // Try to find existing networked object
            var existingObject = GameObject.Find($"NetworkObject_{networkId}");

            if (existingObject != null)
                return existingObject.GetComponent<T>();

            // If not found and we have type info, spawn a new one
            if (!string.IsNullOrEmpty(typeName))
            {
                // Create new game object
                var newObject = new GameObject($"NetworkObject_{networkId}");

                // Set up as networked object (add required components)
                var networkObject = newObject.AddComponent<NetworkObject>();
                networkObject.NetworkId = networkId;

                // Add type-specific components based on typeName
                // This would require mapping type names to prefabs or component setups
                if (typeof(T) == typeof(Identifiable))
                {
                    // Example: Spawn a slime or resource based on type
                    var identifiable = newObject.AddComponent<Identifiable>();

                    // Set ID based on typeName
                    Enum.TryParse(typeName, out Identifiable.Id id);
                    identifiable.id = id;

                    // Add required components for this type
                    // (This would need to be expanded based on the game's requirements)
                }

                return newObject.GetComponent<T>();
            }

            return null;
        }

        private string GetNetworkId(UnityEngine.Object obj)
        {
            // Get existing network ID or generate a new one
            var networkObject = obj.GetComponent<NetworkObject>();

            if (networkObject != null)
                return networkObject.NetworkId;

            // Generate a new ID
            string newId = Guid.NewGuid().ToString();

            // Add network object component
            networkObject = obj.gameObject.AddComponent<NetworkObject>();
            networkObject.NetworkId = newId;

            return newId;
        }

        private void OnGamePause(bool paused)
        {
            // Handle game pause/unpause
            if (paused)
            {
                // Pause network updates
                CancelInvoke("SyncGameState");
                CancelInvoke("SendPlayerUpdate");
            }
            else
            {
                // Resume network updates
                if (isHost)
                    InvokeRepeating("SyncGameState", 0f, SYNC_INTERVAL);
                else
                    InvokeRepeating("SendPlayerUpdate", 0f, SYNC_INTERVAL);
            }
        }

        #endregion
    }

    #region Network Components

    // Simple network manager
    public class NetworkManager : MonoBehaviour
    {
        // Network configuration
        public int DefaultPort = 7777;
        public float TimeoutSeconds = 15.0f;

        // Network statistics
        public int BytesSent { get; private set; }
        public int BytesReceived { get; private set; }
        public float Latency { get; private set; }

        // Reset stats
        public void ResetStats()
        {
            BytesSent = 0;
            BytesReceived = 0;
        }

        // Track sent data
        public void TrackSentData(int byteCount)
        {
            BytesSent += byteCount;
        }

        // Track received data
        public void TrackReceivedData(int byteCount)
        {
            BytesReceived += byteCount;
        }

        // Update latency measurement
        public void UpdateLatency(float latencyMs)
        {
            Latency = latencyMs;
        }
    }

    // Server implementation
    public class Server
    {
        private TcpListener listener;
        private List<ServerClient> clients = new List<ServerClient>();
        private int nextClientId = 1;
        private bool isRunning = false;

        // Events
        public event Action<int, string> OnPlayerConnected;
        public event Action<int> OnPlayerDisconnected;
        public event Action<int, byte[]> OnDataReceived;

        public Server(int port, int maxClients)
        {
            listener = new TcpListener(IPAddress.Any, port);
        }

        public void Start()
        {
            if (isRunning)
                return;

            listener.Start();
            isRunning = true;

            // Start accepting clients
            listener.BeginAcceptTcpClient(OnClientConnected, null);

            Debug.Log("Server started");
        }

        public void Stop()
        {
            if (!isRunning)
                return;

            // Disconnect all clients
            foreach (var client in clients.ToList())
            {
                DisconnectClient(client);
            }

            listener.Stop();
            isRunning = false;

            Debug.Log("Server stopped");
        }

        public void ProcessMessages()
        {
            if (!isRunning)
                return;

            // Process messages from all clients
            foreach (var client in clients.ToList())
            {
                try
                {
                    // Check if client is still connected
                    if (!client.TcpClient.Connected)
                    {
                        DisconnectClient(client);
                        continue;
                    }

                    // Read available data
                    if (client.TcpClient.Available > 0)
                    {
                        var stream = client.TcpClient.GetStream();

                        // Read message length (first 4 bytes)
                        if (client.DataBuffer.Count < 4 && client.TcpClient.Available >= 4 - client.DataBuffer.Count)
                        {
                            byte[] lengthBytes = new byte[4 - client.DataBuffer.Count];
                            stream.Read(lengthBytes, 0, lengthBytes.Length);
                            client.DataBuffer.AddRange(lengthBytes);
                        }

                        // If we have the length, read the rest of the message
                        if (client.DataBuffer.Count >= 4)
                        {
                            int messageLength = BitConverter.ToInt32(client.DataBuffer.ToArray(), 0);

                            if (client.DataBuffer.Count < 4 + messageLength && client.TcpClient.Available >= 4 + messageLength - client.DataBuffer.Count)
                            {
                                byte[] messageBytes = new byte[4 + messageLength - client.DataBuffer.Count];
                                stream.Read(messageBytes, 0, messageBytes.Length);
                                client.DataBuffer.AddRange(messageBytes);
                            }

                            // If we have the complete message, process it
                            if (client.DataBuffer.Count >= 4 + messageLength)
                            {
                                byte[] message = new byte[messageLength];
                                Array.Copy(client.DataBuffer.ToArray(), 4, message, 0, messageLength);

                                // Fire data received event
                                OnDataReceived?.Invoke(client.ClientId, message);

                                // Remove processed message from buffer
                                client.DataBuffer.RemoveRange(0, 4 + messageLength);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error processing messages for client {client.ClientId}: {ex.Message}");
                    DisconnectClient(client);
                }
            }
        }

        public void SendMessage(int clientId, byte[] data)
        {
            var client = clients.FirstOrDefault(c => c.ClientId == clientId);

            if (client != null)
            {
                try
                {
                    var stream = client.TcpClient.GetStream();

                    // Prepend message length
                    byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                    stream.Write(lengthBytes, 0, lengthBytes.Length);

                    // Send actual data
                    stream.Write(data, 0, data.Length);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error sending message to client {clientId}: {ex.Message}");
                    DisconnectClient(client);
                }
            }
        }

        public void BroadcastMessage(byte[] data)
        {
            foreach (var client in clients)
            {
                SendMessage(client.ClientId, data);
            }
        }

        private void OnClientConnected(IAsyncResult ar)
        {
            try
            {
                // Get the new client
                TcpClient tcpClient = listener.EndAcceptTcpClient(ar);

                // Create a new server client
                var client = new ServerClient
                {
                    ClientId = nextClientId++,
                    TcpClient = tcpClient,
                    DataBuffer = new List<byte>()
                };

                // Add to clients list
                clients.Add(client);

                // Wait for player name
                var stream = tcpClient.GetStream();
                byte[] nameBuffer = new byte[256];
                int nameLength = stream.Read(nameBuffer, 0, nameBuffer.Length);
                string playerName = System.Text.Encoding.UTF8.GetString(nameBuffer, 0, nameLength);

                client.PlayerName = playerName;

                // Send client ID
                byte[] idBytes = BitConverter.GetBytes(client.ClientId);
                stream.Write(idBytes, 0, idBytes.Length);

                // Fire client connected event
                OnPlayerConnected?.Invoke(client.ClientId, playerName);

                // Continue accepting new clients
                listener.BeginAcceptTcpClient(OnClientConnected, null);

                Debug.Log($"Client connected: {playerName} (ID: {client.ClientId})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error accepting client: {ex.Message}");

                // Continue accepting new clients
                if (isRunning)
                    listener.BeginAcceptTcpClient(OnClientConnected, null);
            }
        }

        private void DisconnectClient(ServerClient client)
        {
            try
            {
                // Close connection
                client.TcpClient.Close();

                // Remove from list
                clients.Remove(client);

                // Fire disconnected event
                OnPlayerDisconnected?.Invoke(client.ClientId);

                Debug.Log($"Client disconnected: {client.ClientId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error disconnecting client {client.ClientId}: {ex.Message}");
            }
        }

        private class ServerClient
        {
            public int ClientId { get; set; }
            public string PlayerName { get; set; }
            public TcpClient TcpClient { get; set; }
            public List<byte> DataBuffer { get; set; }
        }
    }

    // Client implementation
    public class Client
    {
        private TcpClient tcpClient;
        private List<byte> dataBuffer = new List<byte>();
        private bool isConnected = false;

        // Events
        public event Action<int> OnConnected;
        public event Action OnDisconnected;
        public event Action<int, byte[]> OnDataReceived;

        public void Connect(string address, int port, string playerName)
        {
            if (isConnected)
                return;

            try
            {
                // Create client and connect
                tcpClient = new TcpClient();
                tcpClient.Connect(address, port);

                // Send player name
                var stream = tcpClient.GetStream();
                byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(playerName);
                stream.Write(nameBytes, 0, nameBytes.Length);

                // Receive client ID
                byte[] idBuffer = new byte[4];
                stream.Read(idBuffer, 0, idBuffer.Length);
                int clientId = BitConverter.ToInt32(idBuffer, 0);

                isConnected = true;

                // Fire connected event
                OnConnected?.Invoke(clientId);

                Debug.Log($"Connected to server. Assigned ID: {clientId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error connecting to server: {ex.Message}");

                // Clean up
                if (tcpClient != null)
                {
                    tcpClient.Close();
                    tcpClient = null;
                }

                // Fire disconnected event
                OnDisconnected?.Invoke();
            }
        }

        public void Disconnect()
        {
            if (!isConnected)
                return;

            // Close connection
            tcpClient.Close();
            tcpClient = null;
            isConnected = false;

            // Fire disconnected event
            OnDisconnected?.Invoke();

            Debug.Log("Disconnected from server");
        }

        public void ProcessMessages()
        {
            if (!isConnected || tcpClient == null)
                return;

            try
            {
                // Check if still connected
                if (!tcpClient.Connected)
                {
                    Disconnect();
                    return;
                }

                // Read available data
                if (tcpClient.Available > 0)
                {
                    var stream = tcpClient.GetStream();

                    // Read message length (first 4 bytes)
                    if (dataBuffer.Count < 4 && tcpClient.Available >= 4 - dataBuffer.Count)
                    {
                        byte[] lengthBytes = new byte[4 - dataBuffer.Count];
                        stream.Read(lengthBytes, 0, lengthBytes.Length);
                        dataBuffer.AddRange(lengthBytes);
                    }

                    // If we have the length, read the rest of the message
                    if (dataBuffer.Count >= 4)
                    {
                        int messageLength = BitConverter.ToInt32(dataBuffer.ToArray(), 0);

                        if (dataBuffer.Count < 4 + messageLength && tcpClient.Available >= 4 + messageLength - dataBuffer.Count)
                        {
                            byte[] messageBytes = new byte[4 + messageLength - dataBuffer.Count];
                            stream.Read(messageBytes, 0, messageBytes.Length);
                            dataBuffer.AddRange(messageBytes);
                        }

                        // If we have the complete message, process it
                        if (dataBuffer.Count >= 4 + messageLength)
                        {
                            byte[] message = new byte[messageLength];
                            Array.Copy(dataBuffer.ToArray(), 4, message, 0, messageLength);

                            // Fire data received event
                            OnDataReceived?.Invoke(0, message); // Server ID is always 0

                            // Remove processed message from buffer
                            dataBuffer.RemoveRange(0, 4 + messageLength);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing messages: {ex.Message}");
                Disconnect();
            }
        }

        public void SendMessage(byte[] data)
        {
            if (!isConnected || tcpClient == null)
                return;

            try
            {
                var stream = tcpClient.GetStream();

                // Prepend message length
                byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                stream.Write(lengthBytes, 0, lengthBytes.Length);

                // Send actual data
                stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error sending message: {ex.Message}");
                Disconnect();
            }
        }
    }

    #endregion

    #region Network Messages

    public enum MessageType
    {
        PlayerUpdate,
        SlimeUpdate,
        ResourceUpdate,
        GameState,
        Chat
    }

    public abstract class NetworkMessage
    {
        public MessageType Type { get; protected set; }

        public abstract byte[] Serialize();

        public static NetworkMessage Deserialize(byte[] data)
        {
            // First byte is the message type
            MessageType type = (MessageType)data[0];

            // Deserialize based on type
            switch (type)
            {
                case MessageType.PlayerUpdate:
                    return PlayerUpdateMessage.Deserialize(data);

                case MessageType.SlimeUpdate:
                    return SlimeUpdateMessage.Deserialize(data);

                case MessageType.ResourceUpdate:
                    return ResourceUpdateMessage.Deserialize(data);

                case MessageType.GameState:
                    return GameStateMessage.Deserialize(data);

                case MessageType.Chat:
                    return ChatMessage.Deserialize(data);

                default:
                    Debug.LogError($"Unknown message type: {type}");
                    return null;
            }
        }
    }

    public class PlayerUpdateMessage : NetworkMessage
    {
        public Vector3 Position { get; set; }
        public float Rotation { get; set; }
        public float Health { get; set; }
        public float Energy { get; set; }
        public int EquippedSlot { get; set; }
        public List<InventoryItemData> InventoryItems { get; set; } = new List<InventoryItemData>();

        public PlayerUpdateMessage()
        {
            Type = MessageType.PlayerUpdate;
        }

        public override byte[] Serialize()
        {
            using (var ms = new System.IO.MemoryStream())
            {
                using (var writer = new System.IO.BinaryWriter(ms))
                {
                    // Write message type
                    writer.Write((byte)Type);

                    // Write position
                    writer.Write(Position.x);
                    writer.Write(Position.y);
                    writer.Write(Position.z);

                    // Write rotation
                    writer.Write(Rotation);

                    // Write stats
                    writer.Write(Health);
                    writer.Write(Energy);
                    writer.Write(EquippedSlot);

                    // Write inventory items
                    writer.Write(InventoryItems.Count);
                    foreach (var item in InventoryItems)
                    {
                        writer.Write(item.Slot);
                        writer.Write(item.ItemType);
                        writer.Write(item.Count);
                    }
                }

                return ms.ToArray();
            }
        }

        public static PlayerUpdateMessage Deserialize(byte[] data)
        {
            var message = new PlayerUpdateMessage();

            using (var ms = new System.IO.MemoryStream(data))
            {
                using (var reader = new System.IO.BinaryReader(ms))
                {
                    // Skip message type (already read)
                    reader.ReadByte();

                    // Read position
                    float x = reader.ReadSingle();
                    float y = reader.ReadSingle();
                    float z = reader.ReadSingle();
                    message.Position = new Vector3(x, y, z);

                    // Read rotation
                    message.Rotation = reader.ReadSingle();

                    // Read stats
                    message.Health = reader.ReadSingle();
                    message.Energy = reader.ReadSingle();
                    message.EquippedSlot = reader.ReadInt32();

                    // Read inventory items
                    int itemCount = reader.ReadInt32();
                    for (int i = 0; i < itemCount; i++)
                    {
                        var item = new InventoryItemData
                        {
                            Slot = reader.ReadInt32(),
                            ItemType = reader.ReadString(),
                            Count = reader.ReadInt32()
                        };
                        message.InventoryItems.Add(item);
                    }
                }
            }

            return message;
        }
    }

    public class SlimeUpdateMessage : NetworkMessage
    {
        public List<SlimeData> SlimeUpdates { get; set; } = new List<SlimeData>();

        public SlimeUpdateMessage()
        {
            Type = MessageType.SlimeUpdate;
        }

        public override byte[] Serialize()
        {
            using (var ms = new System.IO.MemoryStream())
            {
                using (var writer = new System.IO.BinaryWriter(ms))
                {
                    // Write message type
                    writer.Write((byte)Type);

                    // Write slime count
                    writer.Write(SlimeUpdates.Count);

                    // Write each slime
                    foreach (var slime in SlimeUpdates)
                    {
                        writer.Write(slime.NetworkId);
                        writer.Write(slime.Position.x);
                        writer.Write(slime.Position.y);
                        writer.Write(slime.Position.z);
                        writer.Write(slime.Rotation.x);
                        writer.Write(slime.Rotation.y);
                        writer.Write(slime.Rotation.z);
                        writer.Write(slime.Rotation.w);
                        writer.Write(slime.SlimeType);
                    }
                }

                return ms.ToArray();
            }
        }

        public static SlimeUpdateMessage Deserialize(byte[] data)
        {
            var message = new SlimeUpdateMessage();

            using (var ms = new System.IO.MemoryStream(data))
            {
                using (var reader = new System.IO.BinaryReader(ms))
                {
                    // Skip message type (already read)
                    reader.ReadByte();

                    // Read slime count
                    int slimeCount = reader.ReadInt32();

                    // Read each slime
                    for (int i = 0; i < slimeCount; i++)
                    {
                        var slime = new SlimeData
                        {
                            NetworkId = reader.ReadString(),
                            Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                            Rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                            SlimeType = reader.ReadString()
                        };
                        message.SlimeUpdates.Add(slime);
                    }
                }
            }

            return message;
        }
    }

    public class ResourceUpdateMessage : NetworkMessage
    {
        public List<ResourceData> ResourceUpdates { get; set; } = new List<ResourceData>();

        public ResourceUpdateMessage()
        {
            Type = MessageType.ResourceUpdate;
        }

        public override byte[] Serialize()
        {
            using (var ms = new System.IO.MemoryStream())
            {
                using (var writer = new System.IO.BinaryWriter(ms))
                {
                    // Write message type
                    writer.Write((byte)Type);

                    // Write resource count
                    writer.Write(ResourceUpdates.Count);

                    // Write each resource
                    foreach (var resource in ResourceUpdates)
                    {
                        writer.Write(resource.NetworkId);
                        writer.Write(resource.Position.x);
                        writer.Write(resource.Position.y);
                        writer.Write(resource.Position.z);
                        writer.Write(resource.Rotation.x);
                        writer.Write(resource.Rotation.y);
                        writer.Write(resource.Rotation.z);
                        writer.Write(resource.Rotation.w);
                        writer.Write(resource.ResourceType);
                    }
                }

                return ms.ToArray();
            }
        }

        public static ResourceUpdateMessage Deserialize(byte[] data)
        {
            var message = new ResourceUpdateMessage();

            using (var ms = new System.IO.MemoryStream(data))
            {
                using (var reader = new System.IO.BinaryReader(ms))
                {
                    // Skip message type (already read)
                    reader.ReadByte();

                    // Read resource count
                    int resourceCount = reader.ReadInt32();

                    // Read each resource
                    for (int i = 0; i < resourceCount; i++)
                    {
                        var resource = new ResourceData
                        {
                            NetworkId = reader.ReadString(),
                            Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                            Rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                            ResourceType = reader.ReadString()
                        };
                        message.ResourceUpdates.Add(resource);
                    }
                }
            }

            return message;
        }
    }

    public class GameStateMessage : NetworkMessage
    {
        public float GameTime { get; set; }
        public string Weather { get; set; }

        public GameStateMessage()
        {
            Type = MessageType.GameState;
        }

        public override byte[] Serialize()
        {
            using (var ms = new System.IO.MemoryStream())
            {
                using (var writer = new System.IO.BinaryWriter(ms))
                {
                    // Write message type
                    writer.Write((byte)Type);

                    // Write game state
                    writer.Write(GameTime);
                    writer.Write(Weather);
                }

                return ms.ToArray();
            }
        }

        public static GameStateMessage Deserialize(byte[] data)
        {
            var message = new GameStateMessage();

            using (var ms = new System.IO.MemoryStream(data))
            {
                using (var reader = new System.IO.BinaryReader(ms))
                {
                    // Skip message type (already read)
                    reader.ReadByte();

                    // Read game state
                    message.GameTime = reader.ReadSingle();
                    message.Weather = reader.ReadString();
                }
            }

            return message;
        }
    }

    public class ChatMessage : NetworkMessage
    {
        public string Text { get; set; }

        public ChatMessage()
        {
            Type = MessageType.Chat;
        }

        public override byte[] Serialize()
        {
            using (var ms = new System.IO.MemoryStream())
            {
                using (var writer = new System.IO.BinaryWriter(ms))
                {
                    // Write message type
                    writer.Write((byte)Type);

                    // Write text
                    writer.Write(Text);
                }

                return ms.ToArray();
            }
        }

        public static ChatMessage Deserialize(byte[] data)
        {
            var message = new ChatMessage();

            using (var ms = new System.IO.MemoryStream(data))
            {
                using (var reader = new System.IO.BinaryReader(ms))
                {
                    // Skip message type (already read)
                    reader.ReadByte();

                    // Read text
                    message.Text = reader.ReadString();
                }
            }

            return message;
        }
    }

    #endregion

    #region Data Structures

    public class InventoryItemData
    {
        public int Slot { get; set; }
        public string ItemType { get; set; }
        public int Count { get; set; }
    }

    public class SlimeData
    {
        public string NetworkId { get; set; }
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public string SlimeType { get; set; }
    }

    public class ResourceData
    {
        public string NetworkId { get; set; }
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public string ResourceType { get; set; }
    }

    #endregion

    #region UI Components

    public class MultiplayerUI : MonoBehaviour
    {
        // UI components
        private GameObject uiCanvas;
        private GameObject connectionPanel;
        private GameObject playerListPanel;
        private GameObject chatPanel;

        // Reference to main mod component
        private MultiplayerMod mod;

        void Awake()
        {
            // Get reference to main mod
            mod = GetComponent<MultiplayerMod>();

            // Create UI components
            CreateUI();
        }

        private void CreateUI()
        {
            // Create canvas
            uiCanvas = new GameObject("MultiplayerUI");
            var canvas = uiCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Add canvas scaler
            var scaler = uiCanvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // Add raycaster
            uiCanvas.AddComponent<GraphicRaycaster>();

            // Create connection panel
            CreateConnectionPanel();

            // Create player list panel
            CreatePlayerListPanel();

            // Create chat panel
            CreateChatPanel();
        }

        private void CreateConnectionPanel()
        {
            // Create panel
            connectionPanel = new GameObject("ConnectionPanel");
            connectionPanel.transform.SetParent(uiCanvas.transform, false);

            var rect = connectionPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1);
            rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -10);
            rect.sizeDelta = new Vector2(400, 60);

            // Add background
            var image = connectionPanel.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0.5f);

            // Add status text
            var statusText = new GameObject("StatusText");
            statusText.transform.SetParent(connectionPanel.transform, false);

            var statusRect = statusText.AddComponent<RectTransform>();
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = new Vector2(10, 5);
            statusRect.offsetMax = new Vector2(-10, -5);

            var text = statusText.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 16;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = "Multiplayer: Disconnected";

            // Hide panel initially
            connectionPanel.SetActive(false);
        }

        private void CreatePlayerListPanel()
        {
            // Create panel
            playerListPanel = new GameObject("PlayerListPanel");
            playerListPanel.transform.SetParent(uiCanvas.transform, false);

            var rect = playerListPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-10, -80);
            rect.sizeDelta = new Vector2(200, 300);

            // Add background
            var image = playerListPanel.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0.5f);

            // Add title
            var titleObj = new GameObject("Title");
            titleObj.transform.SetParent(playerListPanel.transform, false);

            var titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.sizeDelta = new Vector2(0, 30);

            var titleText = titleObj.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 18;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.text = "Players";

            // Add content panel
            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(playerListPanel.transform, false);

            var contentRect = contentObj.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 0);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.offsetMin = new Vector2(10, 10);
            contentRect.offsetMax = new Vector2(-10, -40);

            // Add vertical layout group
            var layout = contentObj.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 5;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            // Add content size fitter
            var fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Hide panel initially
            playerListPanel.SetActive(false);
        }

        private void CreateChatPanel()
        {
            // Create panel
            chatPanel = new GameObject("ChatPanel");
            chatPanel.transform.SetParent(uiCanvas.transform, false);

            var rect = chatPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(0.3f, 0.3f);
            rect.offsetMin = new Vector2(10, 10);
            rect.offsetMax = new Vector2(-10, -10);

            // Add background
            var image = chatPanel.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0.5f);

            // Add chat messages area
            var messagesObj = new GameObject("Messages");
            messagesObj.transform.SetParent(chatPanel.transform, false);

            var messagesRect = messagesObj.AddComponent<RectTransform>();
            messagesRect.anchorMin = new Vector2(0, 0.2f);
            messagesRect.anchorMax = new Vector2(1, 1);
            messagesRect.offsetMin = new Vector2(10, 10);
            messagesRect.offsetMax = new Vector2(-10, -10);

            // Add scroll rect
            var scrollRect = messagesObj.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            // Add content container
            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(messagesObj.transform, false);

            var contentRect = contentObj.AddComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.sizeDelta = new Vector2(0, 0);
            scrollRect.content = contentRect;

            // Add vertical layout group
            var layout = contentObj.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 5;
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            // Add content size fitter
            var fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Add input field area
            var inputObj = new GameObject("InputField");
            inputObj.transform.SetParent(chatPanel.transform, false);

            var inputRect = inputObj.AddComponent<RectTransform>();
            inputRect.anchorMin = Vector2.zero;
            inputRect.anchorMax = new Vector2(1, 0.2f);
            inputRect.offsetMin = new Vector2(10, 10);
            inputRect.offsetMax = new Vector2(-10, -5);

            // Add input field
            var inputField = inputObj.AddComponent<InputField>();
            var inputImage = inputObj.AddComponent<Image>();
            inputImage.color = new Color(1, 1, 1, 0.1f);
            inputField.targetGraphic = inputImage;

            // Add input text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(inputObj.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 5);
            textRect.offsetMax = new Vector2(-10, -5);

            var text = textObj.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 14;
            text.color = Color.white;
            inputField.textComponent = text;

            // Add placeholder
            var placeholderObj = new GameObject("Placeholder");
            placeholderObj.transform.SetParent(inputObj.transform, false);

            var placeholderRect = placeholderObj.AddComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(10, 5);
            placeholderRect.offsetMax = new Vector2(-10, -5);

            var placeholder = placeholderObj.AddComponent<Text>();
            placeholder.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            placeholder.fontSize = 14;
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.color = new Color(1, 1, 1, 0.5f);
            placeholder.text = "Press Enter to chat...";
            inputField.placeholder = placeholder;

            // Set up input field events
            inputField.onEndEdit.AddListener(OnChatInputEndEdit);

            // Hide panel initially
            chatPanel.SetActive(false);
        }

        private void OnChatInputEndEdit(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Get input field
            var inputField = chatPanel.transform.Find("InputField").GetComponent<InputField>();

            // Send chat message
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                var message = new ChatMessage
                {
                    Text = text
                };

                // Send to server
                mod.SendChatMessage(message);

                // Clear input field
                inputField.text = "";
                inputField.ActivateInputField();
            }
        }

        public void SetConnectionStatus(bool connected)
        {
            // Update connection panel
            connectionPanel.SetActive(true);

            var statusText = connectionPanel.transform.Find("StatusText").GetComponent<Text>();
            statusText.text = connected ? "Multiplayer: Connected" : "Multiplayer: Disconnected";
            statusText.color = connected ? Color.green : Color.red;

            // Show/hide other panels
            playerListPanel.SetActive(connected);
            chatPanel.SetActive(connected);
        }

        public void ShowDisconnectMessage()
        {
            // TODO: Show disconnect message popup
        }

        public void AddPlayerToList(int playerId, string playerName)
        {
            // Get content panel
            var content = playerListPanel.transform.Find("Content");

            // Check if player already exists
            var existingPlayer = content.Find($"Player_{playerId}");
            if (existingPlayer != null)
                return;

            // Create player item
            var playerObj = new GameObject($"Player_{playerId}");
            playerObj.transform.SetParent(content, false);

            var playerRect = playerObj.AddComponent<RectTransform>();
            playerRect.sizeDelta = new Vector2(0, 20);

            var playerText = playerObj.AddComponent<Text>();
            playerText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            playerText.fontSize = 14;
            playerText.color = Color.white;
            playerText.text = playerName;
        }

        public void RemovePlayerFromList(int playerId)
        {
            // Get content panel
            var content = playerListPanel.transform.Find("Content");

            // Find player item
            var playerObj = content.Find($"Player_{playerId}");
            if (playerObj != null)
                Destroy(playerObj.gameObject);
        }

        public void AddChatMessage(string sender, string message)
        {
            // Get content panel
            var content = chatPanel.transform.Find("Messages/Content");

            // Create message item
            var messageObj = new GameObject("ChatMessage");
            messageObj.transform.SetParent(content, false);

            var messageRect = messageObj.AddComponent<RectTransform>();
            messageRect.sizeDelta = new Vector2(0, 20);

            var messageText = messageObj.AddComponent<Text>();
            messageText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            messageText.fontSize = 14;
            messageText.color = Color.white;
            messageText.text = $"{sender}: {message}";

            // Scroll to bottom
            Canvas.ForceUpdateCanvases();
            var scrollRect = chatPanel.transform.Find("Messages").GetComponent<ScrollRect>();
            scrollRect.verticalNormalizedPosition = 0;
        }
    }

    #endregion

    #region Remote Player

    public class RemotePlayer : MonoBehaviour
    {
        // Player identification
        public int PlayerId { get; private set; }
        public string PlayerName { get; private set; }

        // Visual representation
        private GameObject playerModel;
        private TextMesh nameTag;

        // State data
        private Vector3 targetPosition;
        private float targetRotation;
        private List<InventoryItemData> inventory = new List<InventoryItemData>();

        // Interpolation settings
        private float positionLerpSpeed = 10f;
        private float rotationLerpSpeed = 10f;

        public void Initialize(int playerId, string playerName)
        {
            PlayerId = playerId;
            PlayerName = playerName;

            // Set initial position
            transform.position = Vector3.zero;
            targetPosition = Vector3.zero;

            // Create visual representation
            CreatePlayerModel();
        }

        private void CreatePlayerModel()
        {
            // Create model container
            playerModel = new GameObject("Model");
            playerModel.transform.SetParent(transform, false);

            // Create simple mesh
            var meshObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            meshObj.transform.SetParent(playerModel.transform, false);
            meshObj.transform.localPosition = Vector3.up;
            meshObj.transform.localScale = new Vector3(0.5f, 1, 0.5f);

            // Add material
            var renderer = meshObj.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = new Color(
                UnityEngine.Random.value,
                UnityEngine.Random.value,
                UnityEngine.Random.value
            );

            // Add collider
            var collider = meshObj.GetComponent<Collider>();
            collider.isTrigger = true;

            // Add name tag
            var nameObj = new GameObject("NameTag");
            nameObj.transform.SetParent(playerModel.transform, false);
            nameObj.transform.localPosition = new Vector3(0, 2.5f, 0);

            nameTag = nameObj.AddComponent<TextMesh>();
            nameTag.text = PlayerName;
            nameTag.fontSize = 30;
            nameTag.alignment = TextAlignment.Center;
            nameTag.anchor = TextAnchor.MiddleCenter;
            nameTag.color = Color.white;

            // Make name tag face camera
            nameObj.AddComponent<LookAtCamera>();
        }

        public void UpdateState(PlayerUpdateMessage message)
        {
            // Update target position and rotation
            targetPosition = message.Position;
            targetRotation = message.Rotation;

            // Update inventory
            inventory = message.InventoryItems;
        }

        public void Update()
        {
            // Interpolate position and rotation
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * positionLerpSpeed);

            // Rotate only the model
            var currentRotation = playerModel.transform.eulerAngles;
            playerModel.transform.eulerAngles = new Vector3(
                currentRotation.x,
                Mathf.LerpAngle(currentRotation.y, targetRotation, Time.deltaTime * rotationLerpSpeed),
                currentRotation.z
            );
        }
    }

    public class LookAtCamera : MonoBehaviour
    {
        private Transform cameraTransform;

        void Start()
        {
            // Find main camera
            var camera = GameObject.FindWithTag("MainCamera");
            if (camera != null)
                cameraTransform = camera.transform;
        }

        void Update()
        {
            if (cameraTransform != null)
                transform.LookAt(transform.position + cameraTransform.rotation * Vector3.forward, cameraTransform.rotation * Vector3.up);
        }
    }

    #endregion

    #region Helper Components

    public class NetworkObject : MonoBehaviour
    {
        public string NetworkId { get; set; }
    }

    #endregion
}