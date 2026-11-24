using System.Collections.Generic;
using MaidCafe.Audio;
using MaidCafe.Core;
using MaidCafe.Generation;
using MaidCafe.Localization;
using UnityEngine;

namespace MaidCafe.Objectives
{
    /// <summary>
    /// Silence the music boxes scattered around the floor (each one is noisy to break), then silence
    /// the gramophone that starts playing once they are all quiet.
    /// </summary>
    public class MusicBoxObjective : Objective
    {
        private int _total;
        private int _silenced;
        private Transform _gramophone;
        private RaytracedSource _gramophoneSound;

        public override ObjectiveType Type => ObjectiveType.SilenceMusicBoxes;
        public override string TitleKey => "obj.music.title";
        public override int StepCount => 2;

        /// <summary>Music box transforms (for audio sources in the art pass).</summary>
        public readonly List<Transform> Boxes = new List<Transform>();

        protected override void Spawn()
        {
            // Gramophone on the stage / dining hall.
            var gramRooms = ObjectiveProps.Rooms(Floor, Rng, new[] { RoomType.Stage, RoomType.DiningHall });
            ObjectiveProps.TakeWall(Floor, gramRooms, out var gramSlot);
            // Horn gramophone on a painted nightstand (greybox stand, player and horn when the models are missing).
            GameObject body;
            Vector3 hornMouth;
            if (PropLibrary.Has("Gramophone") && PropLibrary.Has("NightstandB"))
            {
                Vector3 stand = PropLibrary.Size("NightstandB");
                Vector3 player = PropLibrary.Size("Gramophone");
                float depth = Mathf.Max(stand.z, player.z);
                _gramophone = ObjectiveProps.WallRoot(Floor, "Gramophone", gramSlot, depth);
                PropLibrary.Solid("NightstandB", _gramophone, "Stand", new Vector3(0f, 0f, ObjectiveProps.AgainstWall(depth, stand.z)), Quaternion.identity);
                float z = ObjectiveProps.AgainstWall(depth, player.z);
                body = PropLibrary.Solid("Gramophone", _gramophone, "Player", new Vector3(0f, stand.y, z), Quaternion.identity);
                hornMouth = new Vector3(0f, stand.y + player.y * 0.8f, z + player.z * 0.3f);
            }
            else
            {
                _gramophone = ObjectiveProps.WallRoot(Floor, "Gramophone", gramSlot, 0.5f);
                FloorBuilder.LocalBox(_gramophone, "Stand", new Vector3(0f, 0.4f, 0f), new Vector3(0.6f, 0.8f, 0.5f), FloorBuilder.Palette.Wood);
                body = FloorBuilder.LocalBox(_gramophone, "Player", new Vector3(0f, 0.9f, 0f), new Vector3(0.45f, 0.2f, 0.4f),
                    ObjectiveProps.ItemMat("Gramophone", new Color(0.45f, 0.25f, 0.12f), 0.15f));
                FloorBuilder.LocalBox(_gramophone, "Horn", new Vector3(0.05f, 1.25f, 0.1f), new Vector3(0.35f, 0.35f, 0.35f),
                    ObjectiveProps.ItemMat("Horn", new Color(0.8f, 0.65f, 0.3f), 0.3f), collider: false);
                hornMouth = new Vector3(0.05f, 1.25f, 0.3f);
            }
            _gramophoneSound = RaytracedSource.Create(_gramophone, "GramophoneSound", hornMouth, 38f);
            var gramStation = ObjectiveProps.AddStation(body, "interact.silence", "object.gramophone", 3f, 25f, "hint.gramophoneSilent",
                () => Step == 1, () =>
                {
                    _gramophoneSound.Stop();
                    AdvanceStep();
                });
            gramStation.Sound = Sfx.NeedleScratch;
            Targets.Add(_gramophone.position);

            // Music boxes in different far rooms.
            int wanted = IsDeepFloor ? 4 : 3;
            var lidMat = ObjectiveProps.ItemMat("MusicBoxLid", new Color(0.95f, 0.55f, 0.7f), 0.45f);
            var boxMat = MaterialLibrary.Surface("MusicBox", "WoodWorn", new Color(1f, 0.82f, 0.74f), new Color(0.45f, 0.28f, 0.18f), 0.5f);
            foreach (var slot in ObjectiveProps.Spread(Floor, Rng, wanted, new HashSet<int> { gramSlot.Room }))
            {
                // A wooden music box with its lid open (mirror, glass over the comb) on an item stand.
                Vector3 top = FloorBuilder.BuildItemStand(slot, Floor.DynamicRoot);
                var root = ObjectiveProps.Root("MusicBox", Floor.DynamicRoot, top, Quaternion.LookRotation(-ObjectiveProps.Dir3(slot)));
                var box = PropLibrary.Solid("MusicBox", root, "Box", Vector3.zero, Quaternion.identity);
                if (box == null)
                {
                    box = FloorBuilder.LocalBox(root, "Box", new Vector3(0f, 0.09f, 0f), new Vector3(0.3f, 0.18f, 0.22f), boxMat);
                    FloorBuilder.LocalBox(root, "Lid", new Vector3(0f, 0.19f, 0f), new Vector3(0.31f, 0.03f, 0.23f), lidMat, collider: false);
                }
                // Each box plays the lullaby (slightly out of tune with the others) until it is silenced:
                // ray-traced, so the player can find them by ear, muffled through walls and around corners.
                var tune = RaytracedSource.Create(root, "MusicBoxSound", new Vector3(0f, 0.15f, 0.25f), 24f);
                // UnityEngine.Random, not the floor RNG: the floor layout must stay identical per seed.
                tune.Source.pitch = 1f + Random.Range(-0.03f, 0.03f);
                tune.Play(SoundBank.Get(Sfx.MusicBoxLoop), 0.6f, loop: true, startTime: Random.Range(0f, 12f));
                var station = ObjectiveProps.AddStation(box, "interact.silence", "object.musicBox", 1.5f, 12f, null, null, () =>
                {
                    tune.Stop();
                    OnBoxSilenced();
                });
                station.Sound = Sfx.MusicBoxBreak;
                Boxes.Add(root);
                Targets.Add(root.position);
                _total++;
            }
            if (_total == 0) AdvanceStep();
        }

        private void OnBoxSilenced()
        {
            _silenced++;
            if (_silenced >= _total)
            {
                AdvanceStep();
                GameEvents.ShowHint("hint.gramophoneStarts");
                _gramophoneSound.Play(SoundBank.Get(Sfx.GramophoneLoop), 0.85f, loop: true);
                NoiseSystem.Emit(_gramophone.position, 18f, NoiseType.Alarm, fromPlayer: false);
            }
            else
            {
                NotifyProgress();
            }
        }

        public override string StepText()
        {
            switch (Step)
            {
                case 0: return Loc.Format("obj.music.boxes", _silenced, _total);
                case 1: return Loc.Get("obj.music.gramophone");
                default: return Loc.Get("obj.reachElevator");
            }
        }
    }
}
