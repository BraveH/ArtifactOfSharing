using System;
using System.Collections.Generic;
using System.Reflection;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace ArtifactOfSharing.Utils
{
    public static class Extensions
    {
        public static void Write(this NetworkWriter writer, Dictionary<ItemDef, uint> itemCounts)
        {
            writer.WritePackedUInt32((uint)itemCounts.Count);
            foreach (var keyValuePair in itemCounts)
            {
                writer.WritePackedUInt32((uint)(int)keyValuePair.Key.itemIndex);
                writer.WritePackedUInt32(keyValuePair.Value);
            }
        }

        public static void Write(this NetworkWriter writer, Dictionary<uint, EquipmentIndex> equipments)
        {
            writer.WritePackedUInt32((uint)equipments.Count);
            foreach (var keyValuePair in equipments)
            {
                writer.WritePackedUInt32(keyValuePair.Key);
                writer.WritePackedUInt32((uint)(int)keyValuePair.Value);
            }
        }

        public static Dictionary<ItemDef, uint> ReadItemAmounts(this NetworkReader reader)
        {
            var itemCounts = new Dictionary<ItemDef, uint>();
            var length = reader.ReadPackedUInt32();
            for (var i = 0; i < length; i++)
            {
                var def = ItemCatalog.GetItemDef((ItemIndex)(int)reader.ReadPackedUInt32());
                itemCounts.Add(def, reader.ReadPackedUInt32());
            }

            return itemCounts;
        }

        public static Dictionary<uint, EquipmentIndex> ReadEquipmentIndices(this NetworkReader reader)
        {
            var equipments = new Dictionary<uint, EquipmentIndex>();
            var length = reader.ReadPackedUInt32();
            for (var i = 0; i < length; i++)
            {
                equipments.Add(reader.ReadPackedUInt32(), (EquipmentIndex)(int)reader.ReadPackedUInt32());
            }

            return equipments;
        }

        public static Dictionary<ItemDef, uint> FilterCountNotZero(this Inventory invToSet, ItemDef[] defs)
        {
            Dictionary<ItemDef, uint> items = new Dictionary<ItemDef, uint>();

            foreach (ItemDef def in defs)
            {
                int count = invToSet.GetItemCount(def);
                if (count > 0)
                    items.Add(def, (uint)count);
            }

            return items;
        }

        public static T GetField<T>(string fieldName, BindingFlags bindingFlags)
        {
            try
            {
                FieldInfo memberInfos = typeof(ServerAuthManager).GetField("fieldName", bindingFlags);
                T result = (T)memberInfos?.GetValue(null);
                return result;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            return default(T);
        }
    }
}