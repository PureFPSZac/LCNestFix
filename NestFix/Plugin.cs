using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace NestFix
{
    [BepInPlugin(GUID, NAME, VERSION)]

    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "PureFPSZac.NestFix";
        public const string NAME = "NestFix";
        public const string VERSION = "0.0.1";

        static ManualLogSource NestLogger;
        private void Awake()
        {
            NestLogger = Logger;
            var harmony = new Harmony(GUID);
            harmony.PatchAll(typeof(Plugin));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.SceneManager_OnLoadComplete1))]
        private static void StartOfRound_SceneManager_OnLoadComplete1()
        {
            NestLogger.LogInfo("OnLoadComplete1 called");

            var enemyType = Resources.FindObjectsOfTypeAll<EnemyType>().FirstOrDefault(e => e.name == "BaboonHawk");
            if (enemyType == null)
            {
                NestLogger.LogError("No Baboon hawk enemy type was found");
                return;
            }

            enemyType.nestSpawnPrefabWidth = 3.5f;
            if(enemyType.nestSpawnPrefab is null)
            {
                NestLogger.LogInfo("Prefab is null");
            }
            else if(enemyType.nestSpawnPrefab == null)
            {
                NestLogger.LogInfo("Prefab is destroyed");
            }
            var nestTransform = enemyType.nestSpawnPrefab.transform;
            NestLogger.LogInfo(new System.Diagnostics.StackTrace());
            nestTransform.GetChild(0).localPosition = new Vector3(-1.5f, -2.7f, 1.6f);
            NestLogger.LogInfo("Nest width set");
        }

        private static bool NestSpawnPositionIsValid(Vector3 position, float width)
        {
            const int sampleCount = 8;
            const float distanceLimit = 0.1f;
            var offsetVector = new Vector3(width, 0, 0);

            for (var i = 0; i < sampleCount; i++)
            {
                var angle = 360f * i / sampleCount;
                var samplePoint = position + (Quaternion.Euler(0, angle, 0) * offsetVector);
                if (!NavMesh.SamplePosition(samplePoint, out _, distanceLimit, -1))
                    return false;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.PositionEdgeCheck))]
        private static void PositionEdgeCheckPostfix(float width, ref Vector3 __result)
        {
            if (!NestSpawnPositionIsValid(__result, width))
                __result = Vector3.zero;
        }

        private static T[] ArrayWithRemoved<T>(T[] array, int index)
        {
            return [.. array[0..index], .. array[(index+1)..]];
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.SpawnNestObjectForOutsideEnemy))]
        private static bool SpawnNestObjectForOutsideEnemyPrefix(RoundManager __instance, EnemyType enemyType, System.Random randomSeed)
        {
            var nodes = GameObject.FindGameObjectsWithTag("OutsideAINode");
            var tries = 32;

            Vector3? spawnPosition = null;
            while (spawnPosition == null && tries > 0)
            {
                var nodesLeft = new List<GameObject>(nodes);
                NestLogger.LogInfo($"SpawnNestObjectForOutsideEnemyPrefix was called with {nodes.Length} nodes, {tries} tries left");
                while (nodesLeft.Count > 0)
                {
                    var randomIndex = randomSeed.Next(0, nodesLeft.Count);
                    var candidatePosition = nodesLeft[randomIndex].transform.position;
                    nodesLeft.RemoveAt(randomIndex);

                    candidatePosition = __instance.GetRandomNavMeshPositionInBoxPredictable(candidatePosition, 15f, default, randomSeed, __instance.GetLayermaskForEnemySizeLimit(enemyType));
                    candidatePosition = __instance.PositionWithDenialPointsChecked(candidatePosition, nodes, enemyType);
                    candidatePosition = __instance.PositionEdgeCheck(candidatePosition, enemyType.nestSpawnPrefabWidth);

                    if (!candidatePosition.Equals(Vector3.zero))
                    {
                        spawnPosition = candidatePosition;
                        break;
                    }
                }

                tries--;
            }

            if (spawnPosition == null)
            {
                NestLogger.LogWarning($"Failed to find a spawn position for the {enemyType.name} nest.");
                return false;
            }

            GameObject gameObject = UnityEngine.Object.Instantiate(enemyType.nestSpawnPrefab, spawnPosition.Value, Quaternion.Euler(Vector3.zero));
            gameObject.transform.Rotate(Vector3.up, randomSeed.Next(-180, 180), Space.World);
            if (!gameObject.gameObject.GetComponentInChildren<NetworkObject>())
            {
                Debug.LogError("Error: No NetworkObject found in enemy nest spawn prefab that was just spawned on the host: '" + gameObject.name + "'");
            }
            else
            {
                gameObject.gameObject.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene: true);
            }
            if (!gameObject.GetComponent<EnemyAINestSpawnObject>())
            {
                Debug.LogError("Error: No EnemyAINestSpawnObject component in nest object prefab that was just spawned on the host: '" + gameObject.name + "'");
            }
            else
            {
                __instance.enemyNestSpawnObjects.Add(gameObject.GetComponent<EnemyAINestSpawnObject>());
            }
            enemyType.nestsSpawned++;

            return false;
        }
    }
}