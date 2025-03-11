using System;
using UnityEngine;

namespace SlimeRancherMod.Network
{
    public class NetworkManager : MonoBehaviour
    {
        public int DefaultPort = 7777;
        public float TimeoutSeconds = 15.0f;

        public int BytesSent { get; private set; }
        public int BytesReceived { get; private set; }
        public float Latency { get; private set; }

        public void ResetStats()
        {
            BytesSent = 0;
            BytesReceived = 0;
        }

        public void TrackSentData(int byteCount)
        {
            BytesSent += byteCount;
        }

        public void TrackReceivedData(int byteCount)
        {
            BytesReceived += byteCount;
        }

        public void UpdateLatency(float latencyMs)
        {
            Latency = latencyMs;
        }
    }
}