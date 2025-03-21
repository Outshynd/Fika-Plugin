﻿using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using Fika.Core.Coop.GameMode;
using Fika.Core.Coop.Players;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Fika.Core.Coop.Utils
{
    public static class FikaGlobals
    {
        public const string TransitTraderId = "656f0f98d80a697f855d34b1";
        public const string TransiterTraderName = "BTR";
        public const string DefaultTransitId = "66f5750951530ca5ae09876d";

        public const int PingRange = 1000;

        private static readonly ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource("FikaGlobals");

        internal static readonly List<EInteraction> BlockedInteractions =
        [
            EInteraction.DropBackpack, EInteraction.NightVisionOffGear, EInteraction.NightVisionOnGear,
            EInteraction.FaceshieldOffGear, EInteraction.FaceshieldOnGear, EInteraction.BipodForwardOn,
            EInteraction.BipodForwardOff, EInteraction.BipodBackwardOn, EInteraction.BipodBackwardOff
        ];

        internal static readonly List<EquipmentSlot> WeaponSlots =
        [
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster
        ];

        public static ISearchController SearchControllerSerializer
        {
            get
            {
                return GClass1971.Instance;
            }
        }

        internal static float GetOtherPlayerSensitivity()
        {
            return 1f;
        }

        internal static float GetLocalPlayerSensitivity()
        {
            return Singleton<SharedGameSettingsClass>.Instance.Control.Settings.MouseSensitivity;
        }

        internal static float GetLocalPlayerAimingSensitivity()
        {
            return Singleton<SharedGameSettingsClass>.Instance.Control.Settings.MouseAimingSensitivity;
        }

        public static float GetApplicationTime()
        {
            return Time.time;
        }

        internal static bool LampControllerNetIdNot0(LampController controller)
        {
            return controller.NetId != 0;
        }

        internal static int LampControllerGetNetId(LampController controller)
        {
            return controller.NetId;
        }

        internal static bool WindowBreakerAvailableToSync(WindowBreaker breaker)
        {
            return breaker.AvailableToSync;
        }

        internal static Item GetLootItemPositionItem(LootItemPositionClass positionClass)
        {
            return positionClass.Item;
        }

        internal static EBodyPart GetBodyPartFromCollider(BodyPartCollider collider)
        {
            return collider.BodyPartType;
        }

        internal static string FormatFileSize(long bytes)
        {
            int unit = 1024;
            if (bytes < unit) { return $"{bytes} B"; }

            int exp = (int)(Math.Log(bytes) / Math.Log(unit));
            return $"{bytes / Math.Pow(unit, exp):F2} {("KMGTPE")[exp - 1]}B";
        }

        internal static void SpawnItemInWorld(Item item, CoopPlayer player)
        {
            StaticManager.BeginCoroutine(SpawnItemRoutine(item, player));
        }

        private static IEnumerator SpawnItemRoutine(Item item, CoopPlayer player)
        {
            List<ResourceKey> collection = [];
            IEnumerable<Item> items = item.GetAllItems();
            foreach (Item subItem in items)
            {
                foreach (ResourceKey resourceKey in subItem.Template.AllResources)
                {
                    collection.Add(resourceKey);
                }
            }
            Task loadTask = Singleton<PoolManager>.Instance.LoadBundlesAndCreatePools(PoolManager.PoolsCategory.Raid, PoolManager.AssemblyType.Online,
                [.. collection], JobPriority.Immediate, null, default);

            while (!loadTask.IsCompleted)
            {
                yield return new WaitForEndOfFrame();
            }

            Singleton<GameWorld>.Instance.SetupItem(item, player,
                player.Transform.Original.position + player.Transform.Original.forward + (player.Transform.Original.up / 2), Quaternion.identity);

            if (player.IsYourPlayer)
            {
                ConsoleScreen.Log("Spawned item: " + item.ShortName.Localized());
                yield break;
            }
            ConsoleScreen.Log($"{player.Profile.Info.Nickname} has spawned item: {item.ShortName.Localized()}");
        }

        /// <summary>
        /// Checks whether the game client is in a raid
        /// </summary>
        /// <returns></returns>
        public static bool IsInRaid()
        {
            return Singleton<AbstractGame>.Instance is CoopGame coopGame && coopGame.InRaid;
        }

        /// <summary>
        /// Returns true if the profile is a dedicated user in game
        /// </summary>
        /// <param name="profile"></param>
        /// <returns><see cref="bool"/></returns>
        public static bool IsDedicatedProfile(this Profile profile)
        {
            return profile.Info.GroupId.ToLower() == "dedicated";
        }

        /// <summary>
        /// Forces the <see cref="InfoClass.MainProfileNickname"/> to be set on a profile
        /// </summary>
        /// <param name="infoClass"></param>
        /// <param name="nickname"></param>
        public static void SetProfileNickname(this InfoClass infoClass, string nickname)
        {
            Traverse.Create(infoClass).Field<string>("MainProfileNickname").Value = nickname;
        }

        /// <summary>
        /// Checks whether a profile belongs to a player or an AI
        /// </summary>
        /// <param name="profile"></param>
        /// <returns>True if the profile belongs to a player, false if it belongs to an AI</returns>
        public static bool IsPlayerProfile(this Profile profile)
        {
            return !string.IsNullOrEmpty(profile.PetId) || profile.Info.RegistrationDate > 0 || !string.IsNullOrEmpty(profile.Info.MainProfileNickname);
        }

        /// <summary>
        /// Gets the current <see cref="ISession"/>
        /// </summary>
        /// <returns><see cref="ISession"/> of the application</returns>
        public static ISession GetSession()
        {
            if (TarkovApplication.Exist(out TarkovApplication tarkovApplication))
            {
                return tarkovApplication.Session;
            }

            logger.LogError("GetSession: Could not find TarkovApplication!");
            return null;
        }

        /// <summary>
        /// Gets the current PMC or scav profile
        /// </summary>
        /// <param name="scav">If the scav profile should be returned</param>
        /// <returns><see cref="Profile"/> of chosen side</returns>
        public static Profile GetProfile(bool scav)
        {
            ISession session = GetSession();
            if (session == null)
            {
                logger.LogError("GetProfile: Session was null!");
                return null;
            }

            if (!scav)
            {
                return session.Profile;
            }

            return session.ProfileOfPet;
        }

        /// <summary>
        /// Gets the current PMC or scav profile with trimmed data
        /// </summary>
        /// <param name="scav">If the scav profile should be returned</param>
        /// <returns>A trimmed <see cref="Profile"/> of chosen side</returns>
        public static Profile GetLiteProfile(bool scav)
        {
            Profile profile = GetProfile(scav);
            GClass1962 liteDescriptor = new(profile, SearchControllerSerializer)
            {
                Encyclopedia = [],
                InsuredItems = [],
                TaskConditionCounters = []
            };
            return new(liteDescriptor);
        }
    }
}
