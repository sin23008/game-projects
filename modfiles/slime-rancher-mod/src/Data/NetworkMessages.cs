using System;
using System.Collections.Generic;

namespace SlimeRancherMod.Data
{
    public enum MessageType
    {
        PlayerUpdate,
        SlimeUpdate,
        ResourceUpdate,
        GameState,
        Chat
    }

    [Serializable]
    public class NetworkMessage
    {
        public MessageType Type;

        public static NetworkMessage Deserialize(byte[] data)
        {
            // Implement deserialization logic here
            return new NetworkMessage();
        }

        public byte[] Serialize()
        {
            // Implement serialization logic here
            return new byte[0];
        }
    }

    [Serializable]
    public class PlayerUpdateMessage : NetworkMessage
    {
        public Vector3 Position;
        public float Rotation;
        public int Health;
        public int Energy;
        public int EquippedSlot;
        public List<InventoryItemData> InventoryItems;

        public PlayerUpdateMessage()
        {
            Type = MessageType.PlayerUpdate;
        }
    }

    [Serializable]
    public class SlimeUpdateMessage : NetworkMessage
    {
        public List<SlimeData> SlimeUpdates;

        public SlimeUpdateMessage()
        {
            Type = MessageType.SlimeUpdate;
            SlimeUpdates = new List<SlimeData>();
        }
    }

    [Serializable]
    public class ResourceUpdateMessage : NetworkMessage
    {
        public List<ResourceData> ResourceUpdates;

        public ResourceUpdateMessage()
        {
            Type = MessageType.ResourceUpdate;
            ResourceUpdates = new List<ResourceData>();
        }
    }

    [Serializable]
    public class GameStateMessage : NetworkMessage
    {
        public float GameTime;
        public string Weather;

        public GameStateMessage()
        {
            Type = MessageType.GameState;
        }
    }

    [Serializable]
    public class ChatMessage : NetworkMessage
    {
        public string Text;

        public ChatMessage()
        {
            Type = MessageType.Chat;
        }
    }

    [Serializable]
    public class SlimeData
    {
        public string NetworkId;
        public Vector3 Position;
        public Quaternion Rotation;
        public string SlimeType;
    }

    [Serializable]
    public class ResourceData
    {
        public string NetworkId;
        public Vector3 Position;
        public Quaternion Rotation;
        public string ResourceType;
    }

    [Serializable]
    public class InventoryItemData
    {
        public int Slot;
        public string ItemType;
        public int Count;
    }
}