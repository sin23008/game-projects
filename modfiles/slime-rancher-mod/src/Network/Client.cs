using System;
using System.Net.Sockets;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeRancherMod.Network
{
    public class Client
    {
        private TcpClient tcpClient;
        private List<byte> dataBuffer = new List<byte>();
        private bool isConnected = false;

        public event Action<int> OnConnected;
        public event Action OnDisconnected;
        public event Action<int, byte[]> OnDataReceived;

        public void Connect(string address, int port, string playerName)
        {
            if (isConnected)
                return;

            try
            {
                tcpClient = new TcpClient(address, port);
                isConnected = true;

                // Send player name to server
                SendMessage(System.Text.Encoding.UTF8.GetBytes(playerName));

                OnConnected?.Invoke(GetAssignedPlayerId());
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to connect to server: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            if (!isConnected)
                return;

            tcpClient.Close();
            isConnected = false;
            OnDisconnected?.Invoke();
        }

        public void SendMessage(byte[] data)
        {
            if (!isConnected)
                return;

            try
            {
                var stream = tcpClient.GetStream();
                byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                stream.Write(lengthBytes, 0, lengthBytes.Length);
                stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to send message: {ex.Message}");
            }
        }

        public void ProcessMessages()
        {
            if (!isConnected)
                return;

            try
            {
                var stream = tcpClient.GetStream();

                // Read available data
                while (tcpClient.Available > 0)
                {
                    // Read message length
                    if (dataBuffer.Count < 4)
                    {
                        byte[] lengthBytes = new byte[4 - dataBuffer.Count];
                        stream.Read(lengthBytes, 0, lengthBytes.Length);
                        dataBuffer.AddRange(lengthBytes);
                    }

                    if (dataBuffer.Count >= 4)
                    {
                        int messageLength = BitConverter.ToInt32(dataBuffer.ToArray(), 0);
                        if (dataBuffer.Count >= 4 + messageLength)
                        {
                            byte[] messageData = dataBuffer.GetRange(4, messageLength).ToArray();
                            dataBuffer.RemoveRange(0, 4 + messageLength);
                            OnDataReceived?.Invoke(GetAssignedPlayerId(), messageData);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing messages: {ex.Message}");
            }
        }

        private int GetAssignedPlayerId()
        {
            // Placeholder for getting assigned player ID logic
            return 0;
        }
    }
}