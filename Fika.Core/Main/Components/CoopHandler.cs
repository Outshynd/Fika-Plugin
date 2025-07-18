﻿using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using Fika.Core.Main.GameMode;
using Fika.Core.Main.ObservedClasses;
using Fika.Core.Main.Players;
using Fika.Core.Main.Utils;
using Fika.Core.Networking;
using Fika.Core.Networking.Packets.Communication;
using Fika.Core.Utils;
using LiteNetLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fika.Core.Main.Components
{
    /// <summary>
    /// CoopHandler handles most of the spawning logic in the raid, e.g. other players or AI. It also handles the extraction of the local player.
    /// </summary>
    public class CoopHandler : MonoBehaviour
    {
        #region Fields/Properties
        /// <summary>
        /// Reference to the local <see cref="CoopGame"/> instance
        /// </summary>
        public IFikaGame LocalGameInstance { get; internal set; }
        /// <summary>
        /// The ID of the raid session
        /// </summary>
        public string ServerId { get; internal set; }
        /// <summary>
        /// Reference to the local <see cref="FikaPlayer"/> player
        /// </summary>
        public FikaPlayer MyPlayer { get; internal set; }
        /// <summary>
        /// If the <see cref="CoopHandler"/> should sync and spawn profiles during <see cref="Update"/>
        /// </summary>
        public bool ShouldSync { get; set; }
        /// <summary>
        /// Dictionary of key = <see cref="FikaPlayer.NetId"/>, value = <see cref="FikaPlayer"/>
        /// </summary>
        public Dictionary<int, FikaPlayer> Players { get; internal set; }
        /// <summary>
        /// All human players in the form of <see cref="FikaPlayer"/>
        /// </summary>
        public List<FikaPlayer> HumanPlayers { get; internal set; }
        /// <summary>
        /// The amount of human players
        /// </summary>
        public int AmountOfHumans
        {
            get
            {
                return HumanPlayers.Count;
            }
        }
        /// <summary>
        /// List of <see cref="FikaPlayer.NetId"/>s that have extracted
        /// </summary>
        public List<int> ExtractedPlayers { get; internal set; }

        private ManualLogSource _logger;
        /// <summary>
        /// List of all queued players by <see cref="FikaPlayer.NetId"/>
        /// </summary>
        private List<int> _queuedPlayers;
        /// <summary>
        /// Queue of <see cref="SpawnObject"/> containing all players to spawn
        /// </summary>
        private Queue<SpawnObject> _spawnQueue;
        private bool _isClient;
        private float _charSyncCounter;
        private bool _requestQuitGame;
        #endregion

        public static bool TryGetCoopHandler(out CoopHandler coopHandler)
        {
            coopHandler = null;
            IFikaNetworkManager networkManager = Singleton<IFikaNetworkManager>.Instance;
            if (networkManager != null)
            {
                coopHandler = networkManager.CoopHandler;
                return true;
            }

            return false;
        }

        public static string GetServerId()
        {
            IFikaNetworkManager networkManager = Singleton<IFikaNetworkManager>.Instance;
            if (networkManager != null && networkManager.CoopHandler != null)
            {
                return networkManager.CoopHandler.ServerId;
            }

            return FikaBackendUtils.GroupId;
        }

        public void CleanUpForTransit()
        {
            ShouldSync = false;
            Players.Clear();
            HumanPlayers.Clear();
            ExtractedPlayers.Clear();
            _queuedPlayers.Clear();
            _spawnQueue.Clear();
            LocalGameInstance = null;
            _requestQuitGame = false;
            if (_isClient)
            {
                Singleton<FikaClient>.Instance.FikaClientWorld = null;
            }
        }

        protected void Awake()
        {
            _logger = BepInEx.Logging.Logger.CreateLogSource("CoopHandler");
            _spawnQueue = new(50);
            _queuedPlayers = [];
            Players = [];
            HumanPlayers = [];
            ExtractedPlayers = [];
            ShouldSync = false;
        }

        protected void Start()
        {
            if (FikaBackendUtils.IsClient)
            {
                _isClient = true;
                _charSyncCounter = 0f;
                return;
            }

            _isClient = false;
        }

        protected void Update()
        {
            if (LocalGameInstance == null)
            {
                return;
            }

            if (ShouldSync)
            {
                if (_spawnQueue.Count > 0)
                {
                    SpawnPlayer(_spawnQueue.Dequeue());
                }

                if (!_isClient)
                {
                    _charSyncCounter += Time.unscaledDeltaTime;
                    int waitTime = LocalGameInstance.GameController.GameInstance.Status == GameStatus.Started ? 20 : 5;
                    if (_charSyncCounter > waitTime)
                    {
                        _charSyncCounter = 0f;
                        if (Players == null)
                        {
                            return;
                        }

                        SyncPlayersWithClients();
                    }
                }
            }

            ProcessQuitting();
        }

        private void SyncPlayersWithClients()
        {
            CharacterSyncPacket characterSyncPacket = new(Players);
            Singleton<FikaServer>.Instance.SendDataToAll(ref characterSyncPacket, DeliveryMethod.ReliableOrdered);
        }

        protected void OnDestroy()
        {
            Players.Clear();
            HumanPlayers.Clear();
        }

        public void ReInitInteractables()
        {
            Singleton<GameWorld>.Instance.World_0.method_0(null);
        }

        public EQuitState GetQuitState()
        {
            // error?
            if (LocalGameInstance == null || MyPlayer == null)
            {
                return EQuitState.None;
            }

            // are we alive
            if (!MyPlayer.HealthController.IsAlive)
            {
                return EQuitState.Dead;
            }

            // have we extracted
            if (LocalGameInstance.ExtractedPlayers.Contains(MyPlayer.NetId))
            {
                return EQuitState.Extracted;
            }

            return EQuitState.None;
        }

        /// <summary>
        /// This handles the ways of exiting the active game session
        /// </summary>
        private void ProcessQuitting()
        {
            if (!FikaPlugin.ExtractKey.Value.IsDown())
            {
                return;
            }

            EQuitState quitState = GetQuitState();
            if (quitState == EQuitState.None || _requestQuitGame)
            {
                return;
            }

            string keyName = FikaPlugin.ExtractKey.Value.ToString();
            ConsoleScreen.Log($"{keyName} pressed, attempting to extract!");
            _logger.LogInfo($"{keyName} pressed, attempting to extract!");

            _requestQuitGame = true;
            IFikaGame fikaGame = LocalGameInstance;

            bool isPlayerAlive = MyPlayer.ActiveHealthController.IsAlive;
            string exitLocation = isPlayerAlive ? fikaGame.ExitLocation : null;

            // client logic
            if (_isClient)
            {
                fikaGame.Stop(MyPlayer.ProfileId, fikaGame.ExitStatus, exitLocation, 0);
                return;
            }

            // host logic
            FikaServer server = Singleton<FikaServer>.Instance;
            int peers = server.NetServer.ConnectedPeersCount;

            if (fikaGame.ExitStatus == ExitStatus.Transit && HumanPlayers.Count <= 1)
            {
                fikaGame.Stop(MyPlayer.ProfileId, fikaGame.ExitStatus, exitLocation, 0);
                return;
            }

            if (peers > 0)
            {
                NotificationManagerClass.DisplayWarningNotification(LocaleUtils.HOST_CANNOT_EXTRACT.Localized());
                _requestQuitGame = false;
                return;
            }

            bool recentDisconnect = server.TimeSinceLastPeerDisconnected > DateTime.Now.AddSeconds(-5);
            if (server.HasHadPeer && recentDisconnect)
            {
                NotificationManagerClass.DisplayWarningNotification(LocaleUtils.HOST_WAIT_5_SECONDS.Localized());
                _requestQuitGame = false;
                return;
            }

            fikaGame.Stop(MyPlayer.ProfileId, fikaGame.ExitStatus, exitLocation, 0);
        }

        private async void SpawnPlayer(SpawnObject spawnObject)
        {
            if (spawnObject.Profile == null)
            {
                _logger.LogError("SpawnPlayer: Profile was null!");
                _queuedPlayers.Remove(spawnObject.NetId);
                return;
            }

            foreach (IPlayer player in Singleton<GameWorld>.Instance.AllPlayersEverExisted)
            {
                if (player.ProfileId == spawnObject.Profile.ProfileId)
                {
                    return;
                }
            }

            ResourceKey[] allPrefabPaths = spawnObject.Profile.GetAllPrefabPaths(!spawnObject.IsAI).ToArray();
            if (allPrefabPaths.Length == 0)
            {
                _logger.LogError($"SpawnPlayer::{spawnObject.Profile.Info.Nickname}::PrefabPaths are empty!");
                return;
            }

            await Singleton<PoolManagerClass>.Instance.LoadBundlesAndCreatePools(PoolManagerClass.PoolsCategory.Raid,
                PoolManagerClass.AssemblyType.Local, allPrefabPaths, JobPriorityClass.Low).ContinueWith(x =>
                {
                    if (x.IsFaulted)
                    {
                        _logger.LogError($"SpawnPlayer::{spawnObject.Profile.Info.Nickname}::Load Failed");
                    }
                    else if (x.IsCanceled)
                    {
                        _logger.LogError($"SpawnPlayer::{spawnObject.Profile.Info.Nickname}::Load Cancelled");
                    }
                });

            ObservedPlayer otherPlayer = SpawnObservedPlayer(spawnObject);

            if (!spawnObject.IsAlive)
            {
                otherPlayer.OnDead(EDamageType.Undefined);
                otherPlayer.NetworkHealthController.IsAlive = false;
            }

            if (FikaBackendUtils.IsServer)
            {
                if (LocalGameInstance != null)
                {
                    BotsController botController = (Singleton<IFikaGame>.Instance.GameController as HostGameController).BotsController;
                    if (botController != null)
                    {
                        // Start Coroutine as botController might need a while to start sometimes...
#if DEBUG
                        _logger.LogInfo("Starting AddClientToBotEnemies routine.");
#endif
                        StartCoroutine(AddClientToBotEnemies(botController, otherPlayer));
                    }
                    else
                    {
                        _logger.LogError("botController was null when trying to add player to enemies!");
                    }
                }
                else
                {
                    _logger.LogError("LocalGameInstance was null when trying to add player to enemies!");
                }
            }

            _queuedPlayers.Remove(spawnObject.NetId);
        }

        public void QueueProfile(Profile profile, byte[] healthByteArray, Vector3 position, int netId, bool isAlive, bool isAI, MongoID firstId, ushort firstOperationId, bool isZombie,
            EHandsControllerType controllerType = EHandsControllerType.None, string itemId = null)
        {
            GameWorld gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null)
            {
                return;
            }

            foreach (IPlayer player in gameWorld.AllPlayersEverExisted)
            {
                if (player.ProfileId == profile.ProfileId)
                {
                    return;
                }
            }

            if (_queuedPlayers.Contains(netId))
            {
                return;
            }

            _queuedPlayers.Add(netId);
#if DEBUG
            _logger.LogInfo($"Queueing profile: {profile.Nickname}, {profile.ProfileId}");
#endif
            SpawnObject spawnObject = new(profile, position, isAlive, isAI, netId, firstId, firstOperationId, isZombie);
            if (controllerType != EHandsControllerType.None)
            {
                spawnObject.ControllerType = controllerType;
                if (!string.IsNullOrEmpty(itemId))
                {
                    spawnObject.ItemId = itemId;
                }
            }
            if (healthByteArray != null)
            {
                spawnObject.HealthBytes = healthByteArray;
            }
            _spawnQueue.Enqueue(spawnObject);
        }

        private ObservedPlayer SpawnObservedPlayer(SpawnObject spawnObject)
        {
            bool isAi = spawnObject.IsAI;
            Profile profile = spawnObject.Profile;
            Vector3 position = spawnObject.Position;
            int netId = spawnObject.NetId;
            MongoID firstId = spawnObject.CurrentId;
            ushort firstOperationId = spawnObject.FirstOperationId;
            byte[] healthBytes = spawnObject.HealthBytes;
            bool isZombie = spawnObject.IsZombie;

            // Handle null bytes on players
            if (!isAi && (spawnObject.HealthBytes == null || spawnObject.HealthBytes.Length == 0))
            {
                healthBytes = profile.Health.SerializeHealthInfo();
            }

            GameWorld gameWorld = Singleton<GameWorld>.Instance;

            // Check for GClass increments on filter
            ObservedPlayer otherPlayer = ObservedPlayer.CreateObservedPlayer(gameWorld, netId, position, Quaternion.identity, "Player",
                isAi ? "Bot_" : $"Player_{profile.Nickname}_", EPointOfView.ThirdPerson, profile, healthBytes, isAi,
                EUpdateQueue.Update, Player.EUpdateMode.Manual, Player.EUpdateMode.Auto,
                BackendConfigAbstractClass.Config.CharacterController.ObservedPlayerMode,
                FikaGlobals.GetOtherPlayerSensitivity, FikaGlobals.GetOtherPlayerSensitivity,
                ObservedViewFilter.Default, firstId, firstOperationId, isZombie)
                .GetAwaiter()
                .GetResult();

            if (otherPlayer == null)
            {
                return null;
            }

            Singleton<IFikaNetworkManager>.Instance.ObservedCoopPlayers.Add(otherPlayer);

            otherPlayer.NetId = netId;
#if DEBUG
            _logger.LogInfo($"SpawnObservedPlayer: {profile.Nickname} spawning with NetId {netId}");
#endif

            if (!Players.ContainsKey(netId))
            {
                Players.Add(netId, otherPlayer);
            }
            else
            {
                _logger.LogError($"Trying to add {otherPlayer.Profile.Nickname} to list of players but it was already there!");
            }

            if (!isAi && !HumanPlayers.Contains(otherPlayer))
            {
                HumanPlayers.Add(otherPlayer);
            }

            foreach (FikaPlayer player in Players.Values)
            {
                if (player is not ObservedPlayer)
                {
                    continue;
                }

                Collider playerCollider = otherPlayer.GetCharacterControllerCommon().GetCollider();
                Collider otherCollider = player.GetCharacterControllerCommon().GetCollider();

                if (playerCollider != null && otherCollider != null)
                {
                    EFTPhysicsClass.IgnoreCollision(playerCollider, otherCollider);
                }
            }

            if (isAi)
            {
                if (profile.Info.Side is EPlayerSide.Bear or EPlayerSide.Usec)
                {
                    Item backpack = profile.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;
                    if (backpack != null)
                    {
                        foreach (Item backpackItem in backpack.GetAllItems())
                        {
                            if (backpackItem != backpack)
                            {
                                backpackItem.SpawnedInSession = true;
                            }
                        }
                    }

                    // We still want DogTags to be 'FiR'
                    Item item = otherPlayer.Inventory.Equipment.GetSlot(EquipmentSlot.Dogtag).ContainedItem;
                    if (item != null)
                    {
                        item.SpawnedInSession = true;
                    }
                }
            }
            else if (profile.Info.Side != EPlayerSide.Savage)// Make Player PMC items are all not 'FiR'
            {
                profile.SetSpawnedInSession(false);
            }

            otherPlayer.InitObservedPlayer();

#if DEBUG
            _logger.LogInfo($"CreateLocalPlayer::{profile.GetCorrectedNickname()}::Spawned.");
#endif

            EHandsControllerType controllerType = spawnObject.ControllerType;
            string itemId = spawnObject.ItemId;
            bool isStationary = spawnObject.IsStationary;
            if (controllerType != EHandsControllerType.None)
            {
                if (controllerType != EHandsControllerType.Empty && string.IsNullOrEmpty(itemId))
                {
                    _logger.LogError($"CreateLocalPlayer: ControllerType was not Empty but itemId was null! ControllerType: {controllerType}");
                }
                else
                {
                    otherPlayer.SpawnHandsController(controllerType, itemId, isStationary);
                }
            }
            return otherPlayer;
        }

        private IEnumerator AddClientToBotEnemies(BotsController botController, LocalPlayer playerToAdd)
        {
            IFikaGame coopGame = LocalGameInstance;
            _logger.LogInfo($"AddClientToBotEnemies: " + playerToAdd.Profile.GetCorrectedNickname());
            while (coopGame.GameController.GameInstance.Status != GameStatus.Running && !botController.IsEnable)
            {
                yield return null;
            }

            while (botController.BotSpawner == null)
            {
                yield return null;
            }

#if DEBUG
            _logger.LogInfo($"Adding Client {playerToAdd.Profile.GetCorrectedNickname()} to enemy list");
#endif
            botController.AddActivePLayer(playerToAdd);

            bool found = false;

            for (int i = 0; i < botController.BotSpawner.PlayersCount; i++)
            {
                if (botController.BotSpawner.GetPlayer(i) == playerToAdd)
                {
                    found = true;
                    break;
                }
            }

            if (found)
            {
#if DEBUG
                _logger.LogInfo($"Verified that {playerToAdd.Profile.GetCorrectedNickname()} was added to the enemy list.");
#endif
                yield break;
            }

            _logger.LogError($"Failed to add {playerToAdd.Profile.GetCorrectedNickname()} to the enemy list.");
        }

        public void CheckIds(List<int> playerIds, List<int> missingIds)
        {
            foreach (int netId in playerIds)
            {
                if (!_queuedPlayers.Contains(netId) && !Players.ContainsKey(netId))
                {
                    missingIds.Add(netId);
                }
            }
        }

        /// <summary>
        /// The state your character or game is in to Quit.
        /// </summary>
        public enum EQuitState
        {
            None = -1,
            Dead,
            Extracted
        }

        public class SpawnObject(Profile profile, Vector3 position, bool isAlive, bool isAI, int netId, MongoID currentId, ushort firstOperationId, bool isZombie)
        {
            public Profile Profile = profile;
            public Vector3 Position = position;
            public bool IsAlive = isAlive;
            public bool IsAI = isAI;
            public int NetId = netId;
            public MongoID CurrentId = currentId;
            public ushort FirstOperationId = firstOperationId;
            public EHandsControllerType ControllerType;
            public string ItemId;
            public bool IsStationary;
            public byte[] HealthBytes;
            public bool IsZombie = isZombie;
        }
    }
}