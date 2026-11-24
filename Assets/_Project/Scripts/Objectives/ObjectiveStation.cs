using System;
using MaidCafe.Audio;
using MaidCafe.Core;
using MaidCafe.Interaction;
using MaidCafe.Player;
using UnityEngine;

namespace MaidCafe.Objectives
{
    /// <summary>
    /// Generic objective interactable (breaker box, lever, desk, safe, table, bell, music box...).
    /// When not ready, pressing interact shows a hint explaining what is missing.
    /// </summary>
    public class ObjectiveStation : MonoBehaviour, IInteractable
    {
        public string ReadyPromptKey = "interact.use";
        public string LockedPromptKey = "interact.inspect";
        public string DisplayNameKey;
        public string LockedHintKey;
        public float Hold = 1f;
        public float NoiseRadius;
        public bool SingleUse = true;

        /// <summary>Ray-traced sound played on use; heard further the louder the station is (NoiseRadius).</summary>
        public Sfx Sound = Sfx.Interact;
        public float SoundVolume = 0.8f;

        public Func<bool> IsReady;
        public Action OnUsed;

        public bool Used { get; private set; }

        private bool Ready => IsReady == null || IsReady();

        public string PromptKey => Ready ? ReadyPromptKey : LockedPromptKey;
        public string NameKey => DisplayNameKey;
        public float HoldDuration => Ready ? Hold : 0f;

        public bool CanInteract(PlayerContext player) => !Used;

        public void Interact(PlayerContext player)
        {
            if (!Ready)
            {
                if (!string.IsNullOrEmpty(LockedHintKey)) GameEvents.ShowHint(LockedHintKey);
                return;
            }
            if (NoiseRadius > 0f) NoiseSystem.Emit(transform.position, NoiseRadius, NoiseType.Interaction);
            GameAudio.PlayAt(Sound, transform.position, SoundVolume, Mathf.Clamp(NoiseRadius * 1.6f, 12f, 45f));
            if (SingleUse) Used = true;
            OnUsed?.Invoke();
        }
    }
}
