using System.Collections.Generic;
using MaidCafe.Audio;
using MaidCafe.Generation;
using MaidCafe.Localization;
using UnityEngine;

namespace MaidCafe.Objectives
{
    /// <summary>
    /// Collect the order (tea, cake, sugar) → serve it at the reserved table → ring the service bell
    /// (very loud: it calls the maids).
    /// </summary>
    public class ServeCustomerObjective : Objective
    {
        private readonly HashSet<string> _collected = new HashSet<string>();
        private int _needed;

        public override ObjectiveType Type => ObjectiveType.ServeCustomer;
        public override string TitleKey => "obj.serve.title";
        public override int StepCount => 3;

        protected override void Spawn()
        {
            // Reserved table (centre of a dining room if possible).
            var tableRooms = ObjectiveProps.Rooms(Floor, Rng, new[] { RoomType.DiningHall, RoomType.Stage, RoomType.StaffRoom });
            Transform table = null;
            foreach (int room in tableRooms)
            {
                if (!Floor.Slots.TryTakeCenter(room, out var cell)) continue;
                table = ObjectiveProps.Root("ReservedTable", Floor.DynamicRoot, FloorLayout.CellCenter(cell), Quaternion.Euler(0f, Rng.Chance(0.5f) ? 0f : 90f, 0f));
                break;
            }
            if (table == null)
            {
                ObjectiveProps.TakeWall(Floor, tableRooms, out var slot);
                table = ObjectiveProps.WallRoot(Floor, "ReservedTable", slot, 1.6f);
            }

            var top = FloorBuilder.LocalBox(table, "TableTop", new Vector3(0f, 0.75f, 0f), new Vector3(1.2f, 0.06f, 0.8f),
                MaterialLibrary.Surface("TableCloth", "GinghamRose", new Color(1f, 0.97f, 0.97f), new Color(0.92f, 0.9f, 0.88f), 0.1f));
            FloorBuilder.LocalBox(table, "TableLeg", new Vector3(0f, 0.37f, 0f), new Vector3(0.12f, 0.74f, 0.12f), FloorBuilder.Palette.Wood);
            FloorBuilder.LocalBox(table, "TableFoot", new Vector3(0f, 0.03f, 0f), new Vector3(0.6f, 0.06f, 0.6f), FloorBuilder.Palette.Wood);
            // Table setting: an empty plate waiting for the order and the customer's teacup (porcelain tea set).
            if (PropLibrary.Visual("Plate", table, new Vector3(0f, 0.78f, 0.1f), Quaternion.identity) == null)
            {
                FloorBuilder.LocalBox(table, "Plate", new Vector3(0f, 0.79f, 0.1f), new Vector3(0.25f, 0.01f, 0.25f),
                    MaterialLibrary.Get("Plate", Color.white, 0.8f), collider: false);
            }
            PropLibrary.Visual("TeaCup", table, new Vector3(-0.3f, 0.78f, -0.18f), Quaternion.Euler(0f, 35f, 0f));
            ObjectiveProps.PorcelainDoll(table, new Vector3(0f, 0f, -0.65f));

            ObjectiveProps.AddStation(top, "interact.serve", "object.reservedTable", 2.5f, 5f, "hint.needOrder",
                () => Step == 1, AdvanceStep);

            var bell = PropLibrary.Solid("ServiceBell", table, "ServiceBell", new Vector3(0.4f, 0.78f, 0.2f), Quaternion.identity);
            if (bell == null)
            {
                bell = FloorBuilder.LocalBox(table, "ServiceBell", new Vector3(0.4f, 0.82f, 0.2f), new Vector3(0.1f, 0.08f, 0.1f),
                    ObjectiveProps.ItemMat("Bell", new Color(0.85f, 0.7f, 0.3f), 0.5f));
            }
            var bellStation = ObjectiveProps.AddStation(bell, "interact.ringBell", "object.bell", 0f, 30f, "hint.serveFirst",
                () => Step == 2, AdvanceStep);
            bellStation.Sound = Sfx.ServiceBell;
            bellStation.SoundVolume = 1f;
            Targets.Add(table.position);

            // The order: tea, cake, sugar in different rooms.
            var used = new HashSet<int>();
            // Teapot and sugar bowl from the same porcelain tea set, a strawberry chocolate cake.
            Place("tea", "object.tea", "Teapot", new[] { RoomType.Kitchen, RoomType.StaffRoom }, new Color(0.9f, 0.9f, 0.95f), new Vector3(0.09f, 0.1f, 0.09f), used);
            Place("cake", "object.cake", "Cake", new[] { RoomType.Kitchen, RoomType.Storage }, new Color(0.95f, 0.6f, 0.7f), new Vector3(0.16f, 0.1f, 0.16f), used);
            Place("sugar", "object.sugar", "SugarBowl", new[] { RoomType.Storage, RoomType.Restroom }, new Color(0.95f, 0.95f, 0.9f), new Vector3(0.1f, 0.12f, 0.08f), used);
            if (_needed == 0) AdvanceStep();
        }

        private void Place(string id, string nameKey, string model, RoomType[] prefer, Color color, Vector3 size, HashSet<int> used)
        {
            var rooms = ObjectiveProps.Rooms(Floor, Rng, prefer, used);
            if (!ObjectiveProps.TakeWall(Floor, rooms, out var slot)) return;
            used.Add(slot.Room);
            var p = ObjectiveProps.ItemOnCabinet(Floor, slot, id, nameKey, model, ObjectiveProps.ItemMat(id, color, 0.35f), size);
            Targets.Add(p.transform.position);
            _needed++;
        }

        public override void OnItemPicked(string itemId)
        {
            if (Step != 0 || (itemId != "tea" && itemId != "cake" && itemId != "sugar")) return;
            _collected.Add(itemId);
            if (_collected.Count >= _needed) AdvanceStep();
            else NotifyProgress();
        }

        public override string StepText()
        {
            switch (Step)
            {
                case 0: return Loc.Format("obj.serve.collect", _collected.Count, _needed);
                case 1: return Loc.Get("obj.serve.table");
                case 2: return Loc.Get("obj.serve.bell");
                default: return Loc.Get("obj.reachElevator");
            }
        }
    }
}
