using System;
using System.Collections.Generic;
using System.Linq;
using ArtifactOfSharing.Utils;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine.Networking;

namespace ArtifactOfSharing
{
    public class SetInventoryMessage : ArtifactMessageBase
    {
        private Inventory inventory;
        private Dictionary<ItemDef, uint> itemCounts;
        private Dictionary<uint, EquipmentIndex> equipments;

        public SetInventoryMessage()
        {
        }
        public SetInventoryMessage(Inventory inventory, Inventory invToSet)
        {
            this.inventory = inventory;
            this.itemCounts = invToSet.FilterCountNotZero(ContentManager.itemDefs);
            this.equipments = EquipmentData(invToSet);
        }

        public static Dictionary<uint, EquipmentIndex> EquipmentData(Inventory invToSet)
        {
            Dictionary<uint, EquipmentIndex> equipments = new Dictionary<uint, EquipmentIndex>();

            for (uint i = 0; i < invToSet.GetEquipmentSlotCount(); i++)
            {
                equipments.Add(i, invToSet.GetEquipment(i).equipmentIndex);
            }

            return equipments;
        }

        public SetInventoryMessage(Inventory inventory, Dictionary<ItemDef, int> itemCounts, Dictionary<uint, EquipmentIndex> equipments)
        {
            this.inventory = inventory;
            this.itemCounts = itemCounts.ToDictionary(x => x.Key, x => (uint)x.Value);
            this.equipments = equipments.ToDictionary(x => x.Key, x => (EquipmentIndex)x.Value);
        }

        public override void Serialize(NetworkWriter writer)
        {
            base.Serialize(writer);
            writer.Write(inventory.netId);
            writer.Write(itemCounts);
            writer.Write(equipments);
        }

        public override void Deserialize(NetworkReader reader)
        {
            base.Deserialize(reader);
            inventory = Util.FindNetworkObject(reader.ReadNetworkId())?.GetComponent<Inventory>();
            itemCounts = reader.ReadItemAmounts();
            equipments = reader.ReadEquipmentIndices();
        }

        public override void Handle()
        {
            base.Handle();
            foreach (ItemDef def in ContentManager.itemDefs)
            {
                int count = inventory.GetItemCount(def);
                int countToBe = (int)(itemCounts.ContainsKey(def) ? itemCounts[def] : 0);

                if (count > countToBe)
                    inventory.RemoveItem(def, count - countToBe);
                else if (countToBe > count)
                    inventory.GiveItem(def, countToBe - count);
            }

            foreach (var equipment in equipments)
                inventory.SetEquipmentIndexForSlot(equipment.Value, equipment.Key);
        }
    }
}