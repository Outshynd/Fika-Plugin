﻿using System;
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using BepInEx;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.UI;
using EFT.UI.Matchmaker;
using Fika.Core;
using Fika.Core.Models;
using Fika.Core.UI.Custom;
using Fika.Dedicated.Patches;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.SinglePlayer.Patches.MainMenu;
using UnityEngine;
using Fika.Core.Networking.Http;
using SPT.Common.Http;
using System.Threading.Tasks;
using Fika.Core.Coop.Utils;
using Fika.Core.Networking;

namespace Fika.Dedicated
{
    [BepInPlugin("com.project-fika.dedicated", "Dedicated", "1.0.0")]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.SPT.custom", BepInDependency.DependencyFlags.HardDependency)]
    public class FikaDedicatedPlugin : BaseUnityPlugin
    {        
        public static FikaDedicatedPlugin Instance { get; private set; }
        //private static FieldInfo _hydrationField = typeof(ActiveHealthController).GetField("healthValue_1", BindingFlags.NonPublic | BindingFlags.Instance);
        //private static FieldInfo _energyField = typeof(ActiveHealthController).GetField("healthValue_0", BindingFlags.NonPublic | BindingFlags.Instance);
        private static DedicatedRaidWebSocketServer fikaDedicatedWebSocket;

        public Coroutine setDedicatedStatusRoutine;

        private void Awake()
        {
            Instance = this;
            FikaPlugin.AutoExtract.Value = true;
            new DLSSPatch1().Enable();
            new DLSSPatch2().Enable();
            new DLSSPatch3().Enable();
            new DLSSPatch4().Enable();
            new VRAMPatch1().Enable();
            new VRAMPatch2().Enable();
            new VRAMPatch3().Enable();
            new VRAMPatch4().Enable();
            new BetaLogoPatch().Disable();
            new SessionResultExitStatusPatch().Enable();
            new MenuScreenPatch().Enable();
            new HealthTreamentScreenPatch().Enable();
            new CreateMovementContextPatch().Enable();
            new HealthControllerPlayerAfterInitPatch().Enable();
            //InvokeRepeating("ClearRenderables", 1f, 1f);

            Logger.LogInfo("Fika.Dedicated loaded!");

            fikaDedicatedWebSocket = new DedicatedRaidWebSocketServer();
            fikaDedicatedWebSocket.Connect();
        }

        // Done every second as a way to minimize processing time
        private void ClearRenderables()
        {
            Stopwatch sw = Stopwatch.StartNew();
            var renderers = FindObjectsOfType<Renderer>();
            foreach (var renderer in renderers)
            {
                Destroy(renderer);
            }

            Logger.LogInfo($"ClearRenderables: ${sw.ElapsedMilliseconds}");
        }

        /*private void FixedUpdate()
        {
            if (!Singleton<GameWorld>.Instantiated)
            {
                return;
            }

            Player localPlayer = Singleton<GameWorld>.Instance.MainPlayer;
            if (localPlayer == null)
            {
                return;
            }

            *//*MovementContext localPlayerMovementContext = localPlayer.MovementContext;
            if (localPlayerMovementContext != null)
            {
                *//*
                 * Disables gravity. Even though we are teleporting ourselves high
                 * eventually the fall time will become great enough to cause us to fall
                *//*
                localPlayerMovementContext.FreefallTime = 0f;
            }*/

            /*ActiveHealthController localPlayerHealthController = localPlayer.ActiveHealthController;
            if (localPlayerHealthController != null)
            {
                // Make headless immune to damage
                localPlayerHealthController.DamageCoeff = 0f;
                localPlayerHealthController.DisableMetabolism();

                // This bit is required because even with coefficient at 0
                // you will still take damage from dehydration and starvation
                {
                    // Keep hydration at max
                    HealthValue healthValue = _hydrationField.GetValue(localPlayerHealthController) as HealthValue;
                    if (healthValue != null)
                    {
                        healthValue.Current = healthValue.Maximum;
                    }

                    // Keep energy at max
                    healthValue = _energyField.GetValue(localPlayerHealthController) as HealthValue;
                    if (healthValue != null)
                    {
                        healthValue.Current = healthValue.Maximum;
                    }
                }
            }*//*

            // Keep dedicated client underground - putting the client high up in the air seems to mess with AI/spawn mods
            BifacialTransform localPlayerTransform = localPlayer.Transform;
            localPlayerTransform.position = localPlayerTransform.position.WithY(-50f);
        }*/

        public void OnFikaStartRaid(StartDedicatedRequest request)
        {
            if (!Singleton<ClientApplication<ISession>>.Instantiated)
            {
                Logger.LogError("We have not finished loading the main menu");
                return;
            }

            TarkovApplication tarkovApplication = (TarkovApplication)Singleton<ClientApplication<ISession>>.Instance;
            ISession session = tarkovApplication.GetClientBackEndSession();
            if (!session.LocationSettings.locations.TryGetValue(request.LocationId, out var location))
            {
                Logger.LogError($"Failed to find location {request.LocationId}");
                return;
            }

            Logger.LogInfo($"Starting on location {location.Name}");
            RaidSettings raidSettings = Traverse.Create(tarkovApplication).Field<RaidSettings>("_raidSettings").Value;
            Logger.LogInfo("Initialized raid settings");
            StartCoroutine(BeginFikaStartRaid(request, session, raidSettings, location));
        }

        private IEnumerator BeginFikaStartRaid(StartDedicatedRequest request, ISession session, RaidSettings raidSettings, LocationSettingsClass.Location location)
        {
            /*
             * Runs through the menus. Eventually this can be replaced
             * but it works for now and I was getting a CTD with other method
            */

            Task.Run(async () =>
            {
                StopCoroutine(setDedicatedStatusRoutine);
                SetDedicatedStatusRequest setDedicatedStatusRequest = new SetDedicatedStatusRequest(RequestHandler.SessionId, "inraid");

                await FikaRequestHandler.SetDedicatedStatus(setDedicatedStatusRequest);
            });

            ConsoleScreen.Log($"request: {JsonConvert.SerializeObject(request)}");

            MenuScreen menuScreen;
            do
            {
                yield return StaticManager.Instance.WaitFrames(5, null);
                menuScreen = FindObjectOfType<MenuScreen>();
            } while (menuScreen == null);
            yield return null;
            menuScreen.method_9(); // main menu -> faction selection screen
            //menuScreen.method_8();

            MatchMakerSideSelectionScreen sideSelectionScreen;
            do
            {
                yield return StaticManager.Instance.WaitFrames(5, null);
                sideSelectionScreen = FindObjectOfType<MatchMakerSideSelectionScreen>();
            } while (sideSelectionScreen == null);
            yield return null;

            Action<bool> targetFactionCallback = raidSettings.Side == ESideType.Pmc ?
                sideSelectionScreen.method_12 :
                sideSelectionScreen.method_13;
            targetFactionCallback(true); // select scav/pmc
            yield return null;

            //sideSelectionScreen.UpdateSideSelection(request.Side);
            sideSelectionScreen.method_11(request.Side);

            yield return null;

            sideSelectionScreen.method_17(); // faction selection screen -> location selection screen
            yield return null;

            MatchMakerSelectionLocationScreen locationSelectionScreen;
            do
            {
                yield return StaticManager.Instance.WaitFrames(5, null);
                locationSelectionScreen = FindObjectOfType<MatchMakerSelectionLocationScreen>();
            } while (locationSelectionScreen == null);
            yield return null;

            locationSelectionScreen.Location_0 = session.LocationSettings.locations[request.LocationId];
            locationSelectionScreen.method_6(request.Time);
            //locationSelectionScreen.method_7(locationSelectionScreen.Location_0, request.Time);
            locationSelectionScreen.method_10(); // location selection screen -> offline raid screen
            //locationSelectionScreen.method_8();

            MatchmakerOfflineRaidScreen offlineRaidScreen;
            do
            {
                yield return StaticManager.Instance.WaitFrames(5, null);
                offlineRaidScreen = FindObjectOfType<MatchmakerOfflineRaidScreen>();
            } while (offlineRaidScreen == null);
            yield return null;
            offlineRaidScreen.method_10(); // offline raid screen -> insurance screen

            MatchmakerInsuranceScreen insuranceScreen;
            do
            {
                yield return StaticManager.Instance.WaitFrames(5, null);
                insuranceScreen = FindObjectOfType<MatchmakerInsuranceScreen>();
            } while (insuranceScreen == null);
            yield return null;
            insuranceScreen.method_8(); // insurance screen -> accept screen

            yield return null;

            raidSettings.PlayersSpawnPlace = request.SpawnPlace;
            raidSettings.MetabolismDisabled = request.MetabolismDisabled;
            raidSettings.BotSettings = request.BotSettings;
            raidSettings.WavesSettings = request.WavesSettings;

            MatchMakerAcceptScreen acceptScreen;
            do
            {
                yield return StaticManager.Instance.WaitFrames(5, null);
                acceptScreen = FindObjectOfType<MatchMakerAcceptScreen>();
            } while (acceptScreen == null);
            yield return null;

            yield return new WaitForSeconds(1f);
            MatchMakerUIScript fikaMatchMakerScript;
            do
            {
                yield return StaticManager.Instance.WaitFrames(5, null);
                fikaMatchMakerScript = FindObjectOfType<MatchMakerUIScript>();
            } while (fikaMatchMakerScript == null);
            yield return null;

            //Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.ButtonClick);
            if (FikaPlugin.ForceIP.Value != "")
            {
                // We need to handle DNS entries as well
                string ip = FikaPlugin.ForceIP.Value;
                try
                {
                    IPAddress[] dnsAddress = Dns.GetHostAddresses(FikaPlugin.ForceIP.Value);
                    if (dnsAddress.Length > 0)
                    {
                        ip = dnsAddress[0].ToString();
                    }
                }
                catch
                {

                }

                if (!IPAddress.TryParse(ip, out _))
                {
                    Singleton<PreloaderUI>.Instance.ShowCriticalErrorScreen("ERROR FORCING IP",
                        $"'{ip}' is not a valid IP address to connect to! Check your 'Force IP' setting.",
                        ErrorScreen.EButtonType.OkButton, 10f, null, null);
                    yield break;
                }
            }

            if (FikaPlugin.ForceBindIP.Value != "Disabled")
            {
                if (!IPAddress.TryParse(FikaPlugin.ForceBindIP.Value, out _))
                {
                    Singleton<PreloaderUI>.Instance.ShowCriticalErrorScreen("ERROR BINDING",
                        $"'{FikaPlugin.ForceBindIP.Value}' is not a valid IP address to bind to! Check your 'Force Bind IP' setting.",
                        ErrorScreen.EButtonType.OkButton, 10f, null, null);
                    yield break;
                }
            }

            Logger.LogInfo($"Starting with: {JsonConvert.SerializeObject(request)}");

            FikaBackendUtils.HostExpectedNumberOfPlayers = request.ExpectedNumPlayers + 1;
            FikaBackendUtils.CreateMatch(session.Profile.ProfileId, session.Profile.Info.Nickname, raidSettings);
            FikaBackendUtils.IsDedicated = true;

            fikaMatchMakerScript.AcceptButton.OnClick.Invoke();
        }

        public IEnumerator SetDedicatedStatus()
        {
            while(true)
            {
                SetDedicatedStatusRequest setDedicatedStatusRequest = new SetDedicatedStatusRequest(RequestHandler.SessionId, "ready");

                Task.Run(async () =>
                {
                    await FikaRequestHandler.SetDedicatedStatus(setDedicatedStatusRequest);
                });

                yield return new WaitForSeconds(15.0f);
            }
        }

        public void StartSetDedicatedStatusRoutine()
        {
            setDedicatedStatusRoutine = StartCoroutine(SetDedicatedStatus());
        }
    }
}
