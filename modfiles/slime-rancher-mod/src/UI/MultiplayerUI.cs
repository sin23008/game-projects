using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace SlimeRancherMod.UI
{
    public class MultiplayerUI : MonoBehaviour
    {
        private List<PlayerEntry> playerEntries = new List<PlayerEntry>();
        private GameObject playerListContainer;
        private GameObject chatContainer;
        private InputField chatInputField;
        private Text chatDisplay;

        void Awake()
        {
            // Initialize UI components
            playerListContainer = new GameObject("PlayerList");
            chatContainer = new GameObject("ChatContainer");

            chatInputField = chatContainer.AddComponent<InputField>();
            chatDisplay = chatContainer.AddComponent<Text>();

            // Set up chat input field
            chatInputField.onEndEdit.AddListener(OnChatSubmit);
        }

        public void AddPlayerToList(int playerId, string playerName)
        {
            var entry = new PlayerEntry(playerId, playerName);
            playerEntries.Add(entry);
            UpdatePlayerList();
        }

        public void RemovePlayerFromList(int playerId)
        {
            playerEntries.RemoveAll(entry => entry.PlayerId == playerId);
            UpdatePlayerList();
        }

        public void AddChatMessage(string senderName, string message)
        {
            chatDisplay.text += $"{senderName}: {message}\n";
        }

        private void OnChatSubmit(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                // Send chat message to server
                // Implement chat message sending logic here
                chatInputField.text = string.Empty; // Clear input field
            }
        }

        private void UpdatePlayerList()
        {
            // Update the UI to reflect the current player list
            // Implement player list updating logic here
        }

        private class PlayerEntry
        {
            public int PlayerId { get; }
            public string PlayerName { get; }

            public PlayerEntry(int playerId, string playerName)
            {
                PlayerId = playerId;
                PlayerName = playerName;
            }
        }
    }
}