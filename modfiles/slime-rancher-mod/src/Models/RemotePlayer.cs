using UnityEngine;

namespace SlimeRancherMod.Models
{
    public class RemotePlayer : MonoBehaviour
    {
        public int PlayerId { get; private set; }
        public string PlayerName { get; private set; }

        public void Initialize(int playerId, string playerName)
        {
            PlayerId = playerId;
            PlayerName = playerName;
        }

        public void UpdateState(PlayerUpdateMessage message)
        {
            transform.position = message.Position;
            transform.rotation = Quaternion.Euler(0, message.Rotation, 0);
            // Update other player state properties as needed
        }
    }
}