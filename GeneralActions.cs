using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AutoAct
{
    [HarmonyPatch]
    static class OnCreateProgress_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TaskHarvest), "OnCreateProgress")]
        static void TaskHarvest_Patch(TaskHarvest __instance)
        {
            AutoAct.UpdateState(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TaskDig), "OnCreateProgress")]
        static void TaskDig_Patch(TaskDig __instance)
        {
            AutoAct.UpdateState(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TaskMine), "OnCreateProgress")]
        static void TaskMine_Patch(TaskMine __instance)
        {
            AutoAct.UpdateState(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TaskPlow), "OnCreateProgress")]
        static void TaskPlow_Patch(TaskPlow __instance)
        {
            AutoAct.UpdateState(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI_Shear), "Run")]
        static void AIShear_Patch(AI_Shear __instance)
        {
            AutoAct.UpdateState(__instance);
        }
    }

    [HarmonyPatch(typeof(Progress_Custom), "OnProgressComplete")]
    static class Progress_Custom_OnProgressComplete_Patch
    {

        [HarmonyPostfix]
        static void Postfix()
        {
            if (!AutoAct.active)
            {
                return;
            }

            // Debug.Log($"Prev AI {EClass.pc.ai}, status {EClass.pc.ai.status}");

            bool lastActionSucceed = EClass.pc.ai != null && EClass.pc.ai.status == AIAct.Status.Running;
            if (!lastActionSucceed)
            {
                return;
            }

            AIAct ai = EClass.pc.ai;
            if (ai is AI_Shear)
            {
                ContinueShear();
            }
            else if (ai is TaskPlow)
            {
                ContinuePlow();
            }
            else if (ai is TaskDig)
            {
                ContinueDig();
            }
            else if (ai is TaskMine)
            {
                ContinueMine();
            }
            else if (ai is TaskHarvest)
            {
                ContinueHarvest();
            }
        }

        static void ContinueShear()
        {
            (Chara target, int _) =
            EClass._map.charas
                .Select((Chara chara) =>
                {
                    if (!chara.CanBeSheared())
                    {
                        return (chara, -1);
                    }

                    PathProgress path = EClass.pc.path;
                    path.RequestPathImmediate(EClass.pc.pos, chara.pos, 1, false, -1);
                    if (path.state == PathProgress.State.Fail)
                    {
                        return (chara, -1);
                    }

                    return (chara, path.nodes.Count);
                })
                .Where(Tuple => Tuple.Item2 != -1)
                .OrderBy(Tuple => Tuple.Item2)
                .FirstOrDefault();

            if (target == null)
            {
                return;
            }

            AutoAct.SetNextTask(new AI_Shear { target = target });
        }

        static void ContinueHarvest()
        {
            TaskHarvest task;
            Point targetPoint = null;
            BaseTaskHarvest lastTask = EClass.pc.ai as BaseTaskHarvest;
            if (lastTask.harvestType == BaseTaskHarvest.HarvestType.Thing)
            {
                Thing thing = GetNextThingTarget();

                if (thing == null)
                {
                    return;
                }

                task = new TaskHarvest
                {
                    pos = thing.pos.Copy(),
                    mode = BaseTaskHarvest.HarvestType.Thing,
                    target = thing
                };

                AutoAct.SetNextTask(task);
                return;
            }
            else if (Settings.SameFarmfieldOnly && (lastTask.pos.IsFarmField || (lastTask.pos.sourceObj.id == 88 && lastTask.pos.IsWater)))
            {
                if (!AutoAct.curtFarmfield.Contains(lastTask.pos))
                {
                    AutoAct.InitFarmfield(lastTask.pos, lastTask.pos.IsWater);
                }
                targetPoint = GetNextFarmfieldTarget();
            }
            else
            {
                targetPoint = GetNextTarget();
            }


            if (targetPoint == null)
            {
                return;
            }

            task = TaskHarvest.TryGetAct(EClass.pc, targetPoint);

            if (task == null)
            {
                return;
            }

            AutoAct.SetNextTask(task);

        }

        static void ContinueDig()
        {
            TaskDig t = EClass.pc.ai as TaskDig;
            if (EClass._zone.IsRegion)
            {
                TaskDig repeatedTaskDig = new TaskDig
                {
                    pos = EClass.pc.pos.Copy(),
                    mode = TaskDig.Mode.RemoveFloor
                };
                AutoAct.SetNextTask(repeatedTaskDig);
                return;
            }

            Point targetPoint = GetNextDigTarget();

            if (targetPoint == null)
            {
                return;
            }

            TaskDig task = new TaskDig
            {
                pos = targetPoint,
                mode = TaskDig.Mode.RemoveFloor
            };
            AutoAct.SetNextTask(task);
        }

        static void ContinueMine()
        {
            Point targetPoint = GetNextTarget();

            if (targetPoint == null)
            {
                return;
            }

            TaskMine task = new TaskMine { pos = targetPoint };
            AutoAct.SetNextTask(task);
        }

        static void ContinuePlow()
        {
            Point targetPoint = GetNextPlowTarget();

            if (targetPoint == null)
            {
                return;
            }

            TaskPlow task = new TaskPlow { pos = targetPoint };
            AutoAct.SetNextTask(task);
        }

        static Point GetNextTarget()
        {
            List<(Point, int, int)> list = new List<(Point, int, int)>();
            EClass._map.bounds.ForeachCell((Cell cell) =>
            {
                if (cell.sourceObj.id != AutoAct.targetType || !(cell.HasObj || cell.HasBlock))
                {
                    return;
                }

                if (cell.growth != null)
                {
                    if (cell.growth.CanHarvest() != AutoAct.targetCanHarvest)
                    {
                        return;
                    }

                    // Check if is withered
                    if (AutoAct.targetGrowth == 4 && cell.growth.stage.idx != AutoAct.targetGrowth)
                    {
                        return;

                    }
                }

                Point p = cell.GetPoint();
                int dist2 = Utils.Dist2(EClass.pc.pos, p);
                if (dist2 > Settings.DetRangeSq)
                {
                    return;
                }

                if (dist2 <= 2)
                {
                    list.Add((p, dist2 - 2, dist2));
                    return;
                }

                PathProgress path = EClass.pc.path;
                path.RequestPathImmediate(EClass.pc.pos, p, 1, false, -1);
                if (path.state == PathProgress.State.Fail)
                {
                    return;
                }

                list.Add((p, path.nodes.Count, dist2));
            });

            (Point targetPoint, int _, int _) = list.OrderBy(tuple => tuple.Item2).ThenBy(tuple => tuple.Item3).FirstOrDefault();

            // if (targetPoint != null && targetPoint.cell.growth != null)
            // {
            //     Debug.Log($"Target stage: {targetPoint.cell.growth.stage.idx}, original target stage: {AutoAct.targetGrowth}");
            //     Debug.Log($"Target: {targetPoint?.cell.sourceObj.id} | {targetPoint?.cell.sourceObj.name} | {targetPoint}");
            // }

            return targetPoint;
        }

        static Thing GetNextThingTarget()
        {
            List<(Thing, int, int)> list = new List<(Thing, int, int)>();
            EClass._map.bounds.ForeachCell((Cell cell) =>
            {
                Point p = cell.GetPoint();
                if (!p.HasThing)
                {
                    return;
                }

                int dist2 = Utils.Dist2(EClass.pc.pos, p);
                if (dist2 > Settings.DetRangeSq)
                {
                    return;
                }

                Thing thing = p.Things.Find((Thing t) => t.Name == AutoAct.targetThingType);
                if (thing == null)
                {
                    return;
                }

                if (dist2 <= 2)
                {
                    list.Add((thing, dist2 - 2, dist2));
                    return;
                }

                PathProgress path = EClass.pc.path;
                path.RequestPathImmediate(EClass.pc.pos, p, 1, false, -1);
                if (path.state == PathProgress.State.Fail)
                {
                    return;
                }

                list.Add((thing, path.nodes.Count, dist2));
            });

            (Thing target, int _, int _) = list.OrderBy(tuple => tuple.Item2).ThenBy(tuple => tuple.Item3).FirstOrDefault();
            // if (target == null) {
            //     Debug.Log("Target: null");
            // } else {
            //     Debug.Log($"Target: {target.id} | {target.Name} | {target.pos}");
            // }
            return target;
        }

        static Point GetNextDigTarget()
        {
            return GetNextTarget2((Cell cell) => cell._floor == AutoAct.targetType && !cell.HasBlock && !cell.HasObj, Settings.DigRange);
        }

        static Point GetNextPlowTarget()
        {
            return GetNextTarget2(
                (Cell cell) => !cell.HasBlock && !cell.HasObj && cell.Installed == null && !cell.IsTopWater && !cell.IsFarmField && (cell.HasBridge ? cell.sourceBridge : cell.sourceFloor).tag.Contains("soil"),
                Settings.PlowRange
            );
        }

        static Point GetNextTarget2(Func<Cell, bool> filter, int range = 2)
        {
            List<(Point, int, int, int)> list = new List<(Point, int, int, int)>();
            EClass._map.bounds.ForeachCell((Cell cell) =>
            {
                if (!filter(cell))
                {
                    return;
                }

                if (AutoAct.startPoint == null)
                {
                    Debug.LogWarning("AutoAct StartPoint: null");
                    return;
                }

                Point p = cell.GetPoint();
                // Range 2 => Max range 5x5
                int dx = Math.Abs(AutoAct.startPoint.x - p.x);
                int dz = Math.Abs(AutoAct.startPoint.z - p.z);
                int max = Math.Max(dx, dz);
                if (max > range)
                {
                    return;
                }

                int dist2 = Utils.Dist2(EClass.pc.pos, p);
                if (dist2 <= 2)
                {
                    list.Add((p, max, dist2 - 2, dist2));
                    return;
                }

                PathProgress path = EClass.pc.path;
                path.RequestPathImmediate(EClass.pc.pos, p, 1, false, -1);
                if (path.state == PathProgress.State.Fail)
                {
                    return;
                }

                list.Add((p, max, path.nodes.Count, dist2));
            });

            (Point targetPoint, int _, int _, int _) = list
                .OrderBy(tuple => tuple.Item2)
                .ThenBy(tuple => tuple.Item3)
                .ThenBy(tuple => tuple.Item4)
                .FirstOrDefault();
            return targetPoint;
        }

        static Point GetNextFarmfieldTarget()
        {
            List<(Point, int, int)> list = new List<(Point, int, int)>();
            foreach (Point p in AutoAct.curtFarmfield)
            {
                Cell cell = p.cell;
                if (cell.sourceObj.id != AutoAct.targetType || !(cell.HasObj || cell.HasBlock))
                {
                    continue;
                }

                if (cell.growth != null)
                {
                    if (cell.growth.CanHarvest() != AutoAct.targetCanHarvest)
                    {
                        continue;
                    }

                    // Check if is withered
                    if (AutoAct.targetGrowth == 4 && cell.growth.stage.idx != AutoAct.targetGrowth)
                    {
                        continue;

                    }
                }
                else
                {
                    continue;
                }


                int dist2 = Utils.Dist2(EClass.pc.pos, p);
                if (dist2 <= 2)
                {
                    list.Add((p, dist2 - 2, dist2));
                    continue;
                }

                PathProgress path = EClass.pc.path;
                path.RequestPathImmediate(EClass.pc.pos, p, 1, false, -1);
                if (path.state == PathProgress.State.Fail)
                {
                    continue;
                }

                list.Add((p, path.nodes.Count, dist2));
            }

            (Point targetPoint, int _, int _) = list
                .OrderBy(tuple => tuple.Item2)
                .ThenBy(tuple => tuple.Item3)
                .FirstOrDefault();
            return targetPoint;
        }
    }
}