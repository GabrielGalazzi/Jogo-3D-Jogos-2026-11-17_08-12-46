using MaidCafe.Player;

namespace MaidCafe.Interaction
{
    /// <summary>Anything the player can use with the interact key.</summary>
    public interface IInteractable
    {
        /// <summary>Localization key of the prompt verb, e.g. "interact.pickup".</summary>
        string PromptKey { get; }

        /// <summary>Optional localization key of the object name shown with the prompt (may be null).</summary>
        string NameKey { get; }

        /// <summary>Seconds the key must be held. 0 = press.</summary>
        float HoldDuration { get; }

        bool CanInteract(PlayerContext player);

        void Interact(PlayerContext player);
    }
}
