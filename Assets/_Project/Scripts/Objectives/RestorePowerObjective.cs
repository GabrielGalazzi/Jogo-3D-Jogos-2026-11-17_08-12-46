using System.Collections.Generic;
using MaidCafe.Audio;
using MaidCafe.Core;
using MaidCafe.Generation;
using MaidCafe.Localization;
using UnityEngine;

namespace MaidCafe.Objectives
{
    /// <summary>Collect fuses → insert them in the breaker box → pull the (loud) main lever.</summary>
    public class RestorePowerObjective : Objective
    {
        private int _needed;
        private int _collected;

        public override ObjectiveType Type => ObjectiveType.RestorePower;
        public override string TitleKey => "obj.power.title";
        public override int StepCount => 3;

        protected override void Spawn()
        {
            // Breaker box in a service room.
            var breakerRooms = ObjectiveProps.Rooms(Floor, Rng, new[] { RoomType.Storage, RoomType.StaffRoom, RoomType.Kitchen });
            ObjectiveProps.TakeWall(Floor, breakerRooms, out var breakerSlot);

            // Wall-mounted fuse box (door swung open beside it) with a knife switch on its right as the main lever;
            // greybox box + lever when the models are missing.
            float depth = ObjectiveProps.Depth("FuseBox", 0.25f);
            var root = ObjectiveProps.WallRoot(Floor, "BreakerBox", breakerSlot, depth);
            var body = PropLibrary.Solid("FuseBox", root, "Breaker", new Vector3(0f, 0.98f, 0f), Quaternion.identity);
            GameObject lever;
            GameObject leverClosed = null;
            if (body != null)
            {
                // The open door lies on the box's +X side, which is the left as seen from the room: the switch goes on
                // the other side.
                float x = -(PropLibrary.Size("FuseBox").x * 0.5f + 0.12f);
                lever = PropLibrary.Solid("KnifeSwitchOpen", root, "Lever",
                    new Vector3(x, 1.2f, ObjectiveProps.AgainstWall(depth, ObjectiveProps.Depth("KnifeSwitchOpen", depth))), Quaternion.identity);
                if (lever != null)
                {
                    var closed = PropLibrary.Visual("KnifeSwitchClosed", root,
                        new Vector3(x, 1.2f, ObjectiveProps.AgainstWall(depth, ObjectiveProps.Depth("KnifeSwitchClosed", depth))),
                        Quaternion.identity, 1f, Layers.Default);
                    leverClosed = closed != null ? closed.gameObject : null;
                    if (leverClosed != null) leverClosed.SetActive(false);
                }
                else
                {
                    lever = LeverBox(root, new Vector3(x, 1.4f, 0.05f));
                }
            }
            else
            {
                body = FloorBuilder.LocalBox(root, "Breaker", new Vector3(0f, 1.4f, 0f), new Vector3(0.7f, 0.9f, 0.25f),
                    MaterialLibrary.Surface("Breaker", "MetalSheetRusty", new Color(0.62f, 0.66f, 0.68f), new Color(0.35f, 0.37f, 0.38f), 0.6f));
                FloorBuilder.LocalBox(root, "Warning", new Vector3(0f, 1.75f, 0.13f), new Vector3(0.3f, 0.1f, 0.01f),
                    ObjectiveProps.ItemMat("Warning", new Color(0.9f, 0.75f, 0.1f), 0.6f), collider: false);
                lever = LeverBox(root, new Vector3(0.48f, 1.4f, 0.05f));
            }

            ObjectiveProps.AddStation(body, "interact.insertFuses", "object.breaker", 1.2f, 6f, "hint.needFuses",
                () => Step == 1, AdvanceStep);
            var leverStation = ObjectiveProps.AddStation(lever, "interact.pull", "object.lever", 2f, 22f, "hint.leverNoPower",
                () => Step == 2, () =>
                {
                    if (leverClosed != null)
                    {
                        // Knife switch thrown: swap the open switch for the closed one.
                        PropLibrary.SetModelVisible(lever, false);
                        leverClosed.SetActive(true);
                    }
                    else
                    {
                        lever.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
                    }
                    AdvanceStep();
                });
            leverStation.Sound = Sfx.Lever;
            leverStation.SoundVolume = 1f;
            Targets.Add(root.position);

            // Fuses spread over far rooms, not in the breaker room.
            int wanted = IsDeepFloor ? 3 : 2;
            var fuseMat = ObjectiveProps.ItemMat("Fuse", new Color(0.95f, 0.8f, 0.2f), 0.5f);
            foreach (var slot in ObjectiveProps.Spread(Floor, Rng, wanted, new HashSet<int> { breakerSlot.Room }))
            {
                var p = ObjectiveProps.ItemOnCabinet(Floor, slot, "fuse", "object.fuse", "Fuse", fuseMat, new Vector3(0.06f, 0.16f, 0.06f));
                Targets.Add(p.transform.position);
                _needed++;
            }
            if (_needed == 0) AdvanceStep(); // degenerate floor: nothing to collect
        }

        private static GameObject LeverBox(Transform root, Vector3 localCentre)
        {
            return FloorBuilder.LocalBox(root, "Lever", localCentre, new Vector3(0.08f, 0.35f, 0.1f),
                ObjectiveProps.ItemMat("Lever", new Color(0.7f, 0.1f, 0.1f), 0.4f));
        }

        public override void OnItemPicked(string itemId)
        {
            if (itemId != "fuse" || Step != 0) return;
            _collected++;
            if (_collected >= _needed) AdvanceStep();
            else NotifyProgress();
        }

        public override string StepText()
        {
            switch (Step)
            {
                case 0: return Loc.Format("obj.power.fuses", _collected, _needed);
                case 1: return Loc.Get("obj.power.insert");
                case 2: return Loc.Get("obj.power.lever");
                default: return Loc.Get("obj.reachElevator");
            }
        }
    }
}
