using ArtifactOfSharing.Utils;
using BepInEx.Configuration;
using Newtonsoft.Json.Utilities;
using R2API;
using R2API.ScriptableObjects;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using RoR2;
using RoR2.ContentManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using static ArtifactOfSharing.Main;
using static R2API.ArtifactCodeAPI;
using Random = UnityEngine.Random;

namespace ArtifactOfSharing.Artifact
{
    enum SharingType
    {
        Swap_Full_Inventories,
        Swap_Partial_Inventories
    }

    class ArtifactOfSharing : ArtifactBase<ArtifactOfSharing>
    {
        public static ConfigEntry<bool> ChangeSinglePlayerInventory;
        public static ConfigEntry<SharingType> MultiPlayerSharingType;
        public static ConfigEntry<int> Threshold;

        public override string ArtifactName => "Artifact of Sharing";
        public override string ArtifactUnlockableName => "Sharing";

        public override string ArtifactLangTokenName => "ARTIFACT_OF_SHARING";

        public override string ArtifactDescription => "When enabled, swaps players' equipment and items every stage among themselves.";

        public override Sprite ArtifactEnabledIcon => MainAssets.LoadAsset<Sprite>("aosenabled.png");

        public override Sprite ArtifactDisabledIcon => MainAssets.LoadAsset<Sprite>("aosdisabled.png");

        public HashSet<NetworkUser> users;

        public override void Init(ConfigFile config)
        {
            ModLogger.LogInfo("INITIALIZING ARTIFACT");
            users = new HashSet<NetworkUser>();
            CreateConfig(config);
            CreateLang();
            CreateArtifact();
            CreateCode();
            Hooks();
        }

        private void CreateCode()
        {
            ArtifactCode = ScriptableObject.CreateInstance<ArtifactCode>();
            ArtifactCode.topRow = new Vector3Int(CompoundValues.Square, CompoundValues.Diamond, CompoundValues.Square);
            ArtifactCode.middleRow = new Vector3Int(CompoundValues.Circle, CompoundValues.Square, CompoundValues.Circle);
            ArtifactCode.bottomRow = new Vector3Int(CompoundValues.Triangle, CompoundValues.Diamond, CompoundValues.Triangle);

            ArtifactCodeAPI.AddCode(this.ArtifactDef, ArtifactCode);
        }

        private void CreateConfig(ConfigFile config)
        {
            ChangeSinglePlayerInventory = config.Bind<bool>("Artifact: " + ArtifactName, "Customize single player items", false, "Customize player's items if only 1 player?");
            MultiPlayerSharingType = config.Bind<SharingType>("Artifact: " + ArtifactName, "Multi-player sharing method", SharingType.Swap_Full_Inventories, 
                "Sharing method can either be set to partial inventory sharing or full inventory sharing.\n\n" +
                "Swap Partial Inventories: Partial Inventory Sharing works by rolling a chance roll for every item and if the roll exceeds the threshold set, " +
                "the item will be moved to the next player in the list.\n\nSwap Full Inventories: Full inventory sharing works by passing on the player's entire " +
                "inventory (items + equipment) over to the next player in the list.");
            Threshold = config.Bind<int>("Artifact: " + ArtifactName, "Partial Sharing Threshold", 50, "Threshold needed to exceed in order to shift " +
                "item over to the next player");

            ModSettingsManager.AddOption(new CheckBoxOption(ChangeSinglePlayerInventory));
            ModSettingsManager.AddOption(new ChoiceOption(MultiPlayerSharingType));
            ModSettingsManager.AddOption(new IntSliderOption(Threshold, new IntSliderConfig() { min = 1, max = 99, checkIfDisabled = IsThresholdDisabled, formatString="{0}%" }));
        }

        private bool IsThresholdDisabled()
        {
            return MultiPlayerSharingType.Value != SharingType.Swap_Partial_Inventories;
        }

        public override void Hooks()
        {
            Stage.onServerStageBegin += onServerStageBegin;
        }
        private void onServerStageBegin(Stage stage)
        {
            if(NetworkServer.active && ArtifactEnabled)
            {
                ModLogger.LogInfo("SERVER STAGE BEGAN: " + stage.ToString() + " - " + stage.sceneDef.ToString());
                SceneDef currentScene = SceneCatalog.GetSceneDefForCurrentScene();
                if (currentScene && !currentScene.isFinalStage && currentScene.sceneType == SceneType.Stage)
                {
                    if (Run.instance.stageClearCount > 0)
                    {
                        List<NetworkUser> instances = NetworkUser.instancesList;

                        // Moved on to second stage onwards
                        if (instances.Count > 1)
                        {
                            List<Inventory> inventoriesCache = new List<Inventory>();
                            foreach (NetworkUser instance in instances)
                            {
                                if (!instance.master)
                                    continue;

                                Inventory userInv = instance.master.inventory;

                                Inventory inventory = new Inventory();
                                inventory.CopyEquipmentFrom(userInv);
                                inventory.CopyItemsFrom(userInv);
                                inventoriesCache.Add(inventory);
                            }

                            if (MultiPlayerSharingType.Value == SharingType.Swap_Partial_Inventories)
                            {
                                Dictionary<NetworkUser, Dictionary<ItemDef, int>> userItemCounts = new Dictionary<NetworkUser, Dictionary<ItemDef, int>>();
                                Dictionary < NetworkUser, Dictionary<uint, EquipmentIndex>> userEquipments = new Dictionary<NetworkUser, Dictionary<uint, EquipmentIndex>>();

                                int threshold = Math.Max(Math.Min(Threshold.Value, 99), 1);

                                for (int i = 0; i < instances.Count; i++)
                                {
                                    NetworkUser networkUser = instances[i];
                                    if (networkUser.master == null)
                                        continue;

                                    int nextIndex = i == (instances.Count - 1) ? 0 : i + 1;

                                    Dictionary<ItemDef, int> itemCounts = new Dictionary<ItemDef, int>();

                                    Inventory nextUserInv = inventoriesCache[nextIndex];
                                    Dictionary<ItemDef, uint> userItems = nextUserInv.FilterCountNotZero(ContentManager.itemDefs);
                                    List<ItemDef> keys = new List<ItemDef>(userItems.Keys);
                                    foreach (ItemDef key in keys)
                                    {
                                        uint count = userItems[key];
                                        int newCount = 0;
                                        for (int k = 0; k < count; k++)
                                        {
                                            if (Random.Range(1, 99) > threshold)
                                                newCount++;
                                        }

                                        itemCounts[key] = newCount;
                                    }

                                    userItems = userItems.Where((keyPair) =>
                                    {
                                        return keyPair.Value > 0;
                                    }).ToDictionary(x => x.Key, x => x.Value);

                                    userItemCounts.Add(networkUser, itemCounts); // items they should get
                                    userEquipments.Add(networkUser, SetInventoryMessage.EquipmentData(nextUserInv)); // equipment they should get
                                }

                                for (int i = 0; i < userItemCounts.Count; i++)
                                {
                                    NetworkUser user = instances[i];
                                    if (user.master == null)
                                        continue;

                                    int prevIndex = i == 0 ? instances.Count - 1 : i - 1;
                                    Dictionary<ItemDef, int> prevUserSteal = userItemCounts[instances[prevIndex]];
                                    Dictionary<ItemDef, int> userSteal = userItemCounts[instances[i]];
                                    Dictionary<ItemDef, uint> userItems = inventoriesCache[i].FilterCountNotZero(ContentManager.itemDefs);

                                    List<ItemDef> keys = new List<ItemDef>(userItems.Keys);
                                    keys.AddRange(userSteal.Keys);

                                    Dictionary<ItemDef, int> finalUserItems = new Dictionary<ItemDef, int>();
                                    foreach (ItemDef key in keys.Distinct())
                                    {
                                        uint count = userItems.ContainsKey(key) ? userItems[key] : 0;
                                        int stolenByOthersCount = prevUserSteal.ContainsKey(key) ? prevUserSteal[key] : 0;
                                        int stolenFromOthersCount = userSteal.ContainsKey(key) ? userSteal[key] : 0;
                                        finalUserItems.Add(key, (int)count - stolenByOthersCount + stolenFromOthersCount);
                                    }

                                    Inventory inv = user.master.inventory;
                                    new SetInventoryMessage(inv, finalUserItems, userEquipments[user]).SendToServer();
                                }
                            }
                            else
                            {
                                int i = 0;
                                foreach (NetworkUser instance in instances)
                                {
                                    if (!instance.master)
                                    {
                                        i++;
                                        continue;
                                    }


                                    int nextIndex = i == (instances.Count - 1) ? 0 : i + 1;

                                    new SetInventoryMessage(instance.master.inventory, inventoriesCache[nextIndex]).SendToServer();
                                    i++;
                                }
                            }
                        }
                        else if (instances.Count == 1 && ChangeSinglePlayerInventory.Value)
                        {
                            NetworkUser instance = instances.First();
                            if (!instance.master)
                                return;

                            Inventory inv = instance.master.inventory;

                            int equipmentCount = inv.GetEquipmentSlotCount();
                            Dictionary<ItemTier, int> itemCounts = new Dictionary<ItemTier, int>();
                            foreach (ItemTier tier in Enum.GetValues(typeof(ItemTier)))
                            {
                                itemCounts[tier] = inv.GetTotalItemCountOfTier(tier);
                            }

                            Inventory inventory = new Inventory();
                            Dictionary<uint, EquipmentIndex> allEquipment = GetRandomEquipment(inv, equipmentCount);

                            Dictionary<ItemDef, int> allItems = new Dictionary<ItemDef, int>();
                            foreach (ItemTier tier in Enum.GetValues(typeof(ItemTier)))
                            {
                                Dictionary<ItemDef, int> items = GetRandomItems(itemCounts[tier], tier);
                                foreach (KeyValuePair<ItemDef, int> pair in items)
                                {
                                    allItems.Add(pair.Key, pair.Value);
                                }
                            }

                            foreach (ItemDef def in ContentManager.itemDefs)
                            {
                                int count = instance.master.inventory.GetItemCount(def);
                                int countToBe = (int)(allItems.ContainsKey(def) ? allItems[def] : 0);

                                if (count > countToBe)
                                    instance.master.inventory.RemoveItem(def, count - countToBe);
                                else if (countToBe > count)
                                    instance.master.inventory.GiveItem(def, countToBe - count);
                            }

                            foreach (var equipment in allEquipment)
                                instance.master.inventory.SetEquipmentIndexForSlot(equipment.Value, equipment.Key);
                        }
                    }
                }
            }
        }

        public Dictionary<uint, EquipmentIndex> GetRandomEquipment(Inventory playerInventory, int count)
        {
            Dictionary<uint, EquipmentIndex> result = new Dictionary<uint, EquipmentIndex>();
            if (count<1)
                return result;

            List<EquipmentDef> equipmentDefs = new List<EquipmentDef>(ContentManager.equipmentDefs);
            for (uint slotIndex = 0; slotIndex < count; slotIndex++)
            {
                if (playerInventory.GetEquipment(slotIndex).equipmentIndex != EquipmentIndex.None)
                {
                    int index = Random.Range(0, equipmentDefs.Count - 1);
                    EquipmentDef equipmentDef = equipmentDefs[index];
                    result.Add(slotIndex, equipmentDef.equipmentIndex);
                    equipmentDefs.RemoveAt(index);
                }
            }
            return result;
        }

        public Dictionary<ItemDef, int> GetRandomItems(int count, ItemTier tier)
        {
            var items = new Dictionary<ItemDef, int>();
            for (var i = 0; i < count; i++)
            {
                List<ItemDef> itemsToPickFrom = FilterTierItems(ContentManager.itemDefs, tier);
                ItemDef item = itemsToPickFrom[Random.Range(0, itemsToPickFrom.Count - 1)];
                if (item == null) continue;

                if (items.ContainsKey(item))
                    items[item] += 1;
                else
                    items.Add(item, 1);
            }

            return items;
        }

        private List<ItemDef> FilterTierItems(ItemDef[] itemDefs, ItemTier tier)
        {
            List<ItemDef> items = new List<ItemDef>();
            foreach (ItemDef itemDef in itemDefs) 
            {
                if(itemDef.tier == tier) items.Add(itemDef);
            }

            return items;
        }
    }
}
