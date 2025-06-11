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
        public const string VERSION = "1.1.0";

        static ManualLogSource NestLogger;

        private static readonly NavMeshPath TempPath = new();

        private void Awake()
        {
            NestLogger = Logger;
            var harmony = new Harmony(GUID);
            harmony.PatchAll(typeof(Plugin));
        }

        private static Vector4 Vec4Position(Vector3 position)
        {
            return new Vector4(position.x, position.y, position.z, 1);
        }

        private static bool NestSpawnPositionIsValid(EnemyType enemyType, Vector3 position, Quaternion rotation)
        {
            const float distanceLimit = 0.5f;

            if (enemyType.name == "BaboonHawk")
            {
                var nest = enemyType.nestSpawnPrefab.transform;
                var spikesParent = nest.Find("WoodPikes");
                // Create a matrix to transform one of the prefab's spike positions into the destination position.
                var transformMatrix = Matrix4x4.TRS(position, rotation, nest.localScale) * nest.worldToLocalMatrix;
                if (spikesParent != null)
                {
                    var spikeColliders = spikesParent.GetComponentsInChildren<BoxCollider>();
                    foreach (var spikeCollider in spikeColliders)
                    {
                        var spike = spikeCollider.transform;
                        Vector4 spikeCenter = Vec4Position(spikeCollider.center);
                        Vector4 spikeBottom = transformMatrix * spike.localToWorldMatrix * (spikeCenter - new Vector4(0, 0, spikeCollider.size.z / 2));
                        Vector4 spikeTop = transformMatrix * spike.localToWorldMatrix * (spikeCenter + new Vector4(0, 0, spikeCollider.size.z / 2));

                        if (!Physics.Linecast(spikeTop, spikeBottom, out var linecastHit, 0x100))
                            return false;
                        if (!NavMesh.SamplePosition(linecastHit.point, out var navMeshHit, distanceLimit, -1))
                            return false;
                        if (!NavMesh.CalculatePath(position, navMeshHit.position, -1, TempPath)
                            || TempPath.status != NavMeshPathStatus.PathComplete)
                            return false;
                    }
                }
            }
            else
            {
                /*const int sampleCount = 8;
                var offsetVector = new Vector3(enemyType.nestSpawnPrefabWidth, 0, 0);

                for (var i = 0; i < sampleCount; i++)
                {
                    var angle = 360f * i / sampleCount;
                    var samplePoint = position + (Quaternion.Euler(0, angle, 0) * offsetVector);
                    if (!NavMesh.SamplePosition(samplePoint, out _, distanceLimit, -1))
                        return false;
                }*/
            }

            return true;
        }

        //debugging tool to spawn nests
        private static void SpawnNest()
        {
            foreach (SpawnableEnemyWithRarity spawnableEnemy in RoundManager.Instance.currentLevel.OutsideEnemies)
            {
                EnemyType enemyType = spawnableEnemy.enemyType;
                if (enemyType.name == "BaboonHawk" && enemyType.nestSpawnPrefab != null)
                {
                    var random = new System.Random();
                    for (var i = 0; i < 100; i++)
                        RoundManager.Instance.SpawnNestObjectForOutsideEnemy(enemyType, random);
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.SpawnNestObjectForOutsideEnemy))]
        private static bool SpawnNestObjectForOutsideEnemyPrefix(RoundManager __instance, EnemyType enemyType, System.Random randomSeed)
        {
            if (enemyType.name != "BaboonHawk")
            {
                return true;
            }
            var nodes = GameObject.FindGameObjectsWithTag("OutsideAINode");
            var tries = 32;

            Vector3? spawnPosition = null;
            Quaternion? spawnRotation = null;
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
                    candidatePosition = __instance.PositionWithDenialPointsChecked(candidatePosition, nodes, enemyType, enemyType.nestDistanceFromShip);
                    candidatePosition = __instance.PositionEdgeCheck(candidatePosition, enemyType.nestSpawnPrefabWidth);

                    var candidateRotation = Quaternion.Euler(0, randomSeed.Next(-180, 180), 0);

                    if (!candidatePosition.Equals(Vector3.zero) && NestSpawnPositionIsValid(enemyType, candidatePosition, candidateRotation))
                    {
                        spawnPosition = candidatePosition;
                        spawnRotation = candidateRotation;
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

            GameObject gameObject = Instantiate(enemyType.nestSpawnPrefab, spawnPosition.Value, Quaternion.Euler(Vector3.zero));
            gameObject.transform.localRotation = spawnRotation.Value * gameObject.transform.localRotation;
            if (!gameObject.gameObject.GetComponentInChildren<NetworkObject>())
                Debug.LogError("Error: No NetworkObject found in enemy nest spawn prefab that was just spawned on the host: '" + gameObject.name + "'");
            else
                gameObject.gameObject.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene: true);

            if (!gameObject.GetComponent<EnemyAINestSpawnObject>())
                Debug.LogError("Error: No EnemyAINestSpawnObject component in nest object prefab that was just spawned on the host: '" + gameObject.name + "'");
            else
                __instance.enemyNestSpawnObjects.Add(gameObject.GetComponent<EnemyAINestSpawnObject>());

            enemyType.nestsSpawned++;

            return false;
        }
    }
}