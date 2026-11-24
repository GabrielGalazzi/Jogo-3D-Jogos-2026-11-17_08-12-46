using System.Collections.Generic;
using MaidCafe.Core;
using MaidCafe.Generation;
using MaidCafe.Localization;
using UnityEngine;

namespace MaidCafe.Objectives
{
    /// <summary>
    /// Find the manager's key → unlock the office desk (reveals the safe code) → open the safe
    /// (gives the keycard) → swipe the keycard at the elevator.
    /// </summary>
    public class FindKeycardObjective : Objective
    {
        private string _code;

        public override ObjectiveType Type => ObjectiveType.FindKeycard;
        public override string TitleKey => "obj.keycard.title";
        public override int StepCount => 4;

        protected override void Spawn()
        {
            _code = Rng.Range(1000, 10000).ToString();

            // Desk in the manager's office.
            var officeRooms = ObjectiveProps.Rooms(Floor, Rng, new[] { RoomType.ManagerOffice, RoomType.StaffRoom });
            ObjectiveProps.TakeWall(Floor, officeRooms, out var deskSlot);
            // Old metal office desk with the manager's notebook (the safe code) on it.
            var desk = ObjectiveProps.WallRoot(Floor, "ManagerDesk", deskSlot, ObjectiveProps.Depth("Desk", 0.75f));
            var deskBody = ObjectiveProps.SolidOrBox(desk, "Desk", "Desk", Vector3.zero, new Vector3(1.5f, 0.76f, 0.75f), FloorBuilder.Palette.Wood);
            float deskTop = PropLibrary.Has("Desk") ? PropLibrary.Size("Desk").y : 0.76f;
            if (PropLibrary.Visual("Notebook", desk, new Vector3(0.3f, deskTop, 0.08f), Quaternion.Euler(0f, 12f, 0f)) == null)
            {
                FloorBuilder.LocalBox(desk, "Papers", new Vector3(0.3f, 0.77f, 0.05f), new Vector3(0.3f, 0.01f, 0.22f),
                    ObjectiveProps.ItemMat("Paper", new Color(0.9f, 0.88f, 0.8f), 0.15f), collider: false);
            }
            ObjectiveProps.AddStation(deskBody, "interact.unlockDesk", "object.desk", 1.5f, 4f, "hint.deskLocked",
                () => Step == 1, () =>
                {
                    AdvanceStep();
                    GameEvents.ShowHint("hint.safeCodeFound");
                });
            Targets.Add(desk.position);

            // Safe in another room.
            var safeRooms = ObjectiveProps.Rooms(Floor, Rng, new[] { RoomType.Storage, RoomType.StaffRoom, RoomType.Stage },
                new HashSet<int> { deskSlot.Room });
            ObjectiveProps.TakeWall(Floor, safeRooms, out var safeSlot);
            var safe = ObjectiveProps.WallRoot(Floor, "Safe", safeSlot, ObjectiveProps.Depth("Safe", 0.6f));
            var safeBody = PropLibrary.Solid("Safe", safe, "SafeBody", Vector3.zero, Quaternion.identity);
            if (safeBody == null)
            {
                safeBody = FloorBuilder.LocalBox(safe, "SafeBody", new Vector3(0f, 0.4f, 0f), new Vector3(0.65f, 0.8f, 0.6f),
                    MaterialLibrary.Surface("Safe", "MetalSheetRusty", new Color(0.34f, 0.36f, 0.4f), new Color(0.18f, 0.2f, 0.22f), 0.7f));
                FloorBuilder.LocalBox(safe, "Dial", new Vector3(0.15f, 0.5f, 0.31f), new Vector3(0.12f, 0.12f, 0.03f),
                    ObjectiveProps.ItemMat("Dial", new Color(0.8f, 0.7f, 0.3f), 0.4f), collider: false);
            }
            ObjectiveProps.AddStation(safeBody, "interact.enterCode", "object.safe", 2.5f, 8f, "hint.safeNeedsCode",
                () => Step == 2, () =>
                {
                    AdvanceStep();
                    GameEvents.ShowHint("hint.keycardFound");
                });
            Targets.Add(safe.position);

            // Manager's key somewhere far, away from the office and the safe.
            var keySlots = ObjectiveProps.Spread(Floor, Rng, 1, new HashSet<int> { deskSlot.Room, safeSlot.Room });
            if (keySlots.Count > 0)
            {
                var key = ObjectiveProps.ItemOnCabinet(Floor, keySlots[0], "office_key", "object.officeKey", "OfficeKey",
                    ObjectiveProps.ItemMat("Key", new Color(0.95f, 0.75f, 0.25f), 0.5f), new Vector3(0.14f, 0.03f, 0.06f));
                Targets.Add(key.transform.position);
            }
            else
            {
                AdvanceStep(); // no room for a key: skip straight to the desk
            }

            // Final step: swipe the keycard at the exit elevator.
            Floor.ExitCar.OverridePromptKey = "interact.swipeCard";
            Floor.ExitCar.OverrideHold = 0.8f;
            Floor.ExitCar.OverrideReady = () => Step == 3;
            Floor.ExitCar.OverrideUse = AdvanceStep;
        }

        public override void OnItemPicked(string itemId)
        {
            if (itemId == "office_key" && Step == 0) AdvanceStep();
        }

        protected override void OnCompleted()
        {
            Floor.ExitCar.OverrideReady = null;
            base.OnCompleted();
        }

        public override string StepText()
        {
            switch (Step)
            {
                case 0: return Loc.Get("obj.keycard.key");
                case 1: return Loc.Get("obj.keycard.desk");
                case 2: return Loc.Format("obj.keycard.safe", _code);
                case 3: return Loc.Get("obj.keycard.elevator");
                default: return Loc.Get("obj.reachElevator");
            }
        }
    }
}
