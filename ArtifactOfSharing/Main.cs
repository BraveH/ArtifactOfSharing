using ArtifactOfSharing.Artifact;
using BepInEx;
using BepInEx.Configuration;
using HG;
using MonoMod.Cil;
using R2API;
using R2API.Utils;
using RiskOfOptions;
using RiskOfOptions.Options;
using RoR2;
using RoR2.Achievements.Artifacts;
using RoR2.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace ArtifactOfSharing
{
    [RegisterAchievement("ObtainArtifactSharing", "Artifacts.Sharing", null, null)]
    public class ArtifactOfSharingAchievement : BaseObtainArtifactAchievement
    {
        public override ArtifactDef artifactDef => Artifact.ArtifactOfSharing.instance.ArtifactDef;
    }

    [BepInPlugin(ModGuid, ModName, ModVer)]
    [BepInDependency(R2API.R2API.PluginGUID, R2API.R2API.PluginVersion)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    [R2APISubmoduleDependency(nameof(ArtifactCodeAPI), nameof(LanguageAPI))]
    public class Main : BaseUnityPlugin
    {
        public const string ModGuid = "com.amrothabet.ArtifactOfSharing";
        public const string ModName = "Artifact Of Sharing";
        public const string ModVer = "0.0.1";

        public static AssetBundle MainAssets;

        public List<ArtifactBase> Artifacts = new List<ArtifactBase>();

        //Provides a direct access to this plugin's logger for use in any of your other classes.
        public static BepInEx.Logging.ManualLogSource ModLogger;

        private void Awake()
        {
            ModLogger = Logger;

            var self = this;
            ModLogger.LogInfo("ARTIFACT MAIN");

            LanguageAPI.Add("ACHIEVEMENT_OBTAINARTIFACTSHARING_NAME", "Trial of Sharing");
            LanguageAPI.Add("ACHIEVEMENT_OBTAINARTIFACTSHARING_DESCRIPTION", "Complete the Trial of Sharing.");

            // Don't know how to create/use an asset bundle, or don't have a unity project set up?
            // Look here for info on how to set these up: https://github.com/KomradeSpectre/AetheriumMod/blob/rewrite-master/Tutorials/Item%20Mod%20Creation.md#unity-project
            // (This is a bit old now, but the information on setting the unity asset bundle should be the same.)

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ArtifactOfSharing.artifactofsharingbundle"))
            {
                MainAssets = AssetBundle.LoadFromStream(stream);
            }
            ModLogger.LogInfo("LOADED ASSETS");

            ModSettingsManager.SetModDescription("Adds Artifact of Sharing which swaps players' inventories (items & equipment) every stage");
            ModSettingsManager.SetModIcon(MainAssets.LoadAsset<Sprite>("aosenabled.png"));

            var ArtifactTypes = Assembly.GetExecutingAssembly().GetTypes().Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(ArtifactBase)));
            foreach (var artifactType in ArtifactTypes)
            {
                ArtifactBase artifact = (ArtifactBase)Activator.CreateInstance(artifactType);
                if (ValidateArtifact(artifact, Artifacts))
                {
                    artifact.Init(Config);
                    AddUnlockable(artifact.ArtifactDef, artifact.ArtifactUnlockableName);
                }
            }

            ModLogger.LogInfo("SETUP ARTIFACT");
        }

        public static void AddUnlockable(ArtifactDef def, string name)
        {
            var icon = MainAssets.LoadAsset<Sprite>("aosenabled.png");
            UnlockableDef unlockableDef = ScriptableObject.CreateInstance<UnlockableDef>();
            unlockableDef.cachedName = "Artifacts." + name;
            unlockableDef.nameToken = def.nameToken;
            unlockableDef.achievementIcon = icon;
            ContentAddition.AddUnlockableDef(unlockableDef);

            def.unlockableDef = unlockableDef;
            PickupIndex pickupIndex = PickupCatalog.FindPickupIndex(def.artifactIndex);
            PickupDef pickupDef = PickupCatalog.GetPickupDef(pickupIndex);
            if(pickupDef != null)
                pickupDef.unlockableDef = unlockableDef;

            RuleDef ruleDef = RuleCatalog.FindRuleDef("Artifacts." + def.cachedName);
            if(ruleDef == null)
            {
                ruleDef = new RuleDef("Artifacts." + def.cachedName, def.descriptionToken);
                ruleDef.AddChoice("On", null);
                ruleDef.FindChoice("On").requiredUnlockable = unlockableDef;
                RuleCatalog.AddRule(ruleDef);
            }
            else
            {
                ruleDef.FindChoice("On").requiredUnlockable = unlockableDef;
            }
        }


        /// <summary>
        /// A helper to easily set up and initialize an artifact from your artifact classes if the user has it enabled in their configuration files.
        /// </summary>
        /// <param name="artifact">A new instance of an ArtifactBase class."</param>
        /// <param name="artifactList">The list you would like to add this to if it passes the config check.</param>
        public bool ValidateArtifact(ArtifactBase artifact, List<ArtifactBase> artifactList)
        {
            ModLogger.LogInfo("ValidateArtifact " + artifact.ArtifactName);

            ConfigEntry<bool> enabled = Config.Bind<bool>("Artifact: " + artifact.ArtifactName, "Enable Artifact?", true, "Should this artifact appear for selection?");
            ModSettingsManager.AddOption(new CheckBoxOption(enabled));
            ModSettingsManager.AddOption(new GenericButtonOption("Force Enable/Disable", "Artifact: " + artifact.ArtifactName, "Enable/Disable the artifact without needing to unlock it by code.", "Toggle", ToggleUnlock));
            if (enabled.Value)
            {
                artifactList.Add(artifact);
            }
            return enabled.Value;
        }

        public UserProfile GetUserProfile() => LocalUserManager.readOnlyLocalUsersList.FirstOrDefault(v => v != null)?.userProfile;

        private void ToggleUnlock()
        {
            UserProfile userProfile = GetUserProfile();
            var unlockableDef = Artifact.ArtifactOfSharing.instance.ArtifactDef.unlockableDef;

            string achievementName = "ACHIEVEMENT_OBTAINARTIFACTSHARING_NAME";
            AchievementDef achievementDef = AchievementManager.achievementDefs.Where(x => x.nameToken.Equals(achievementName)).FirstOrDefault();
            bool userHasAchievement = achievementDef != null && userProfile.HasAchievement(achievementDef.identifier);
            bool userHasUnlockable = userProfile.HasUnlockable(unlockableDef);

            PickupIndex pickupIndex = PickupCatalog.FindPickupIndex(Artifact.ArtifactOfSharing.instance.ArtifactDef.artifactIndex);

            if (!userHasUnlockable)
            {
                if (!userHasAchievement)
                    userProfile.AddAchievement(achievementDef.identifier, true);

                userProfile.GrantUnlockable(unlockableDef);
                
                if(pickupIndex != null)
                    userProfile.SetPickupDiscovered(pickupIndex, true);
                
                userProfile.RequestEventualSave();
            }
            else
            {
                if(userHasAchievement)
                    userProfile.RevokeAchievement(achievementDef.identifier);
                
                userProfile.RevokeUnlockable(unlockableDef);

                if (pickupIndex != null)
                    userProfile.SetPickupDiscovered(pickupIndex, false);

                userProfile.RequestEventualSave();
            }
        }
    }
}
