using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Net;
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
        [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.Start))]
        private static void MenuManagerStartPostfix()
        {
            NestLogger.LogInfo("AwakePostfix called");
            var baboonAI = Resources.FindObjectsOfTypeAll<BaboonBirdAI>();


            if (baboonAI.Length == 0)
            {
                NestLogger.LogInfo("No BaboonHawkAI found");
                return;
            }
            var enemyType = baboonAI[0].enemyType;
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
            NestLogger.LogInfo("SpawnNestObjectForOutsideEnemyPrefix was called");
            GameObject[] array = GameObject.FindGameObjectsWithTag("OutsideAINode");
            int num = randomSeed.Next(0, array.Length);
            Vector3 position = Vector3.zero;
            for (int i = 0; i < array.Length; i++)
            {
                position = array[num].transform.position;
                position = __instance.GetRandomNavMeshPositionInBoxPredictable(position, 15f, default(NavMeshHit), randomSeed, __instance.GetLayermaskForEnemySizeLimit(enemyType));
                position = __instance.PositionWithDenialPointsChecked(position, array, enemyType);
                Vector3 vector = __instance.PositionEdgeCheck(position, enemyType.nestSpawnPrefabWidth);
                array = ArrayWithRemoved(array, num);
                if (vector == Vector3.zero)
                {
                    num = (num + 1) % array.Length;
                    num = randomSeed.Next(0, array.Length);
                }
                else
                {
                    position = vector;
                    break;
                }
            }
            GameObject gameObject = UnityEngine.Object.Instantiate(enemyType.nestSpawnPrefab, position, Quaternion.Euler(Vector3.zero));
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