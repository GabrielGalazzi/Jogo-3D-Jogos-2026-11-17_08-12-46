using MaidCafe.Audio;
using MaidCafe.Core;
using MaidCafe.Player;
using UnityEngine;

namespace MaidCafe.Interaction
{
    public enum PickupKind
    {
        Battery,
        Item,

        /// <summary>Sweets for the watcher maid (see WatcherMaid).</summary>
        Gift
    }

    /// <summary>Something the player can pick up: a phone battery or an objective item.</summary>
    public class Pickup : MonoBehaviour, IInteractable
    {
        [SerializeField] private PickupKind kind = PickupKind.Item;
        [SerializeField] private string itemId;
        [SerializeField] private string nameKey;
        [SerializeField] private float batteryAmount = 0.5f;
        [SerializeField] private float spinSpeed = 40f;

        public PickupKind Kind => kind;
        public string ItemId => itemId;

        public string PromptKey => "interact.pickup";
        public string NameKey => nameKey;
        public float HoldDuration => 0f;

        public void Configure(PickupKind pickupKind, string id, string displayNameKey)
        {
            kind = pickupKind;
            itemId = id;
            nameKey = displayNameKey;
        }

        public bool CanInteract(PlayerContext player) => true;

        public void Interact(PlayerContext player)
        {
            NoiseSystem.Emit(transform.position, 1.5f, NoiseType.Interaction);
            GameAudio.PlayAt(Sfx.Pickup, transform.position, 0.7f, 12f);
            if (kind == PickupKind.Battery)
            {
                player.Phone.AddBattery(batteryAmount);
                GameEvents.ShowHint("hint.batteryFound");
            }
            else if (kind == PickupKind.Gift)
            {
                player.Gifts++;
                GameEvents.ShowHint("hint.giftFound");
            }
            else
            {
                GameEvents.RaiseItemPickedUp(itemId);
            }
            Destroy(gameObject);
        }

        private void Update()
        {
            if (spinSpeed != 0f) transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }
    }
}
