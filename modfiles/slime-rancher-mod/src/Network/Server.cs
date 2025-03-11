using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace SlimeRancherMod.Network
{
    public class Server
    {
        private TcpListener listener;
        private List<ServerClient> clients = new List<ServerClient>();
        private int nextClientId = 1;
        private bool isRunning = false;

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
            listener.BeginAcceptTcpClient(OnClientConnected, null);
        }

        public void Stop()
        {
            if (!isRunning)
                return;

            foreach (var client in clients)
            {
                DisconnectClient(client);
            }

            listener.Stop();
            isRunning = false;
        }

        public void ProcessMessages()
        {
            if (!isRunning)
                return;

            foreach (var client in clients)
            {
                try
                {
                    if (!client.TcpClient.Connected)
                    {
                        DisconnectClient(client);
                        continue;
                    }

                    if (client.TcpClient.Available > 0)
                    {
                        var stream = client.TcpClient.GetStream();
                        byte[] lengthBytes = new byte[4];
                        stream.Read(lengthBytes, 0, lengthBytes.Length);
                        int messageLength = BitConverter.ToInt32(lengthBytes, 0);

                        byte[] messageBytes = new byte[messageLength];
                        stream.Read(messageBytes, 0, messageLength);
                        OnDataReceived?.Invoke(client.ClientId, messageBytes);
                    }
                }
                catch (Exception)
                {
                    DisconnectClient(client);
                }
            }
        }

        public void SendMessage(int clientId, byte[] data)
        {
            var client = clients.Find(c => c.ClientId == clientId);
            if (client != null)
            {
                try
                {
                    var stream = client.TcpClient.GetStream();
                    byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                    stream.Write(lengthBytes, 0, lengthBytes.Length);
                    stream.Write(data, 0, data.Length);
                }
                catch (Exception)
                {
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
            TcpClient tcpClient = listener.EndAcceptTcpClient(ar);
            var client = new ServerClient
            {
                ClientId = nextClientId++,
                TcpClient = tcpClient,
                DataBuffer = new List<byte>()
            };

            clients.Add(client);
            OnPlayerConnected?.Invoke(client.ClientId, "Player " + client.ClientId);
            listener.BeginAcceptTcpClient(OnClientConnected, null);
        }

        private void DisconnectClient(ServerClient client)
        {
            client.TcpClient.Close();
            clients.Remove(client);
            OnPlayerDisconnected?.Invoke(client.ClientId);
        }

        private class ServerClient
        {
            public int ClientId { get; set; }
            public TcpClient TcpClient { get; set; }
            public List<byte> DataBuffer { get; set; }
        }
    }
}