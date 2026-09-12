using System;
using System.Collections;
using System.Collections.Generic;
using Process = System.Diagnostics.Process;
using Stopwatch = System.Diagnostics.Stopwatch;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPDungeonGeometrySpike
    {
        static readonly string[] Regions = { "Daggerfall", "Wayrest", "Sentinel" };
        static readonly string[] Locations = { "Privateer's Hold", "Wayrest", "Sentinel" };

        public static IEnumerator Run(int requestedCount)
        {
            int count = Mathf.Clamp(requestedCount, 1, Regions.Length);
            while (DaggerfallUnity.Instance == null || !DaggerfallUnity.Instance.IsReady)
                yield return null;

            Debug.Log($"[DFMP Dungeon Spike] Starting native geometry probe: requestedCount={count}, importEnemies=false.");
            var generatedDungeons = new List<GameObject>();
            long managedBefore = GC.GetTotalMemory(false);
            long processBefore = GetProcessMemoryBytes();
            Stopwatch stopwatch = Stopwatch.StartNew();
            int generatedBlockCount = 0;

            for (int index = 0; index < count; index++)
            {
                DFLocation location = DaggerfallUnity.Instance.ContentReader.MapFileReader.GetLocation(Regions[index], Locations[index]);
                if (!location.Loaded || !location.HasDungeon)
                {
                    Debug.LogError($"[DFMP Dungeon Spike] Location unavailable: region='{Regions[index]}', location='{Locations[index]}', loaded={location.Loaded}, hasDungeon={location.HasDungeon}.");
                    continue;
                }

                GameObject dungeonObject = new GameObject($"DFMP_DungeonSpike_{index + 1}_{Locations[index]}");
                try
                {
                    DaggerfallDungeon dungeon = dungeonObject.AddComponent<DaggerfallDungeon>();
                    dungeon.DungeonTextureUse = DungeonTextureUse.Disabled;
                    dungeon.SetDungeon(location, false);
                    generatedDungeons.Add(dungeonObject);
                    generatedBlockCount += location.Dungeon.Blocks != null ? location.Dungeon.Blocks.Length : 0;
                    Debug.Log($"[DFMP Dungeon Spike] Generated dungeon: index={index + 1}, region='{Regions[index]}', location='{Locations[index]}', blocks={dungeon.Summary.LocationData.Dungeon.Blocks.Length}, children={dungeonObject.transform.childCount}.");
                }
                catch (Exception exception)
                {
                    UnityEngine.Object.Destroy(dungeonObject);
                    Debug.LogError($"[DFMP Dungeon Spike] Generation failed: index={index + 1}, region='{Regions[index]}', location='{Locations[index]}', exception={exception}");
                }
            }

            stopwatch.Stop();
            long managedAfter = GC.GetTotalMemory(false);
            long processAfter = GetProcessMemoryBytes();
            Debug.Log($"[DFMP Dungeon Spike] Result: requested={count}, generated={generatedDungeons.Count}, blocks={generatedBlockCount}, elapsedMs={stopwatch.ElapsedMilliseconds}, managedDeltaMb={(managedAfter - managedBefore) / (1024f * 1024f):F1}, processDeltaMb={GetMemoryDeltaMb(processBefore, processAfter):F1}.");
        }

        static long GetProcessMemoryBytes()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                    return process.WorkingSet64;
            }
            catch
            {
                return 0;
            }
        }

        static float GetMemoryDeltaMb(long before, long after)
        {
            if (before == 0 || after == 0)
                return 0f;

            return (after - before) / (1024f * 1024f);
        }
    }
}
