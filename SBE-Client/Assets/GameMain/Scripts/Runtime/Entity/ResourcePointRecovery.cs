using System;
using System.Collections.Generic;
using SepCore.Net;

namespace SepCore.Entity
{
    /// <summary>
    /// 断线重连恢复的消耗状态应用（半纯逻辑，可静态测）。
    /// 把服务器 start_run 回放的已开箱清单（looted）应用到本局物资点数据上：
    /// 内容未就绪的点一次性灌入清单并填满掉落计数（IsCompleted，不可再交互）；
    /// 内容已就绪（本地已开过）的点校验清单一致后不动；任何不一致都抛错，不做静默兜底。
    /// </summary>
    public static class ResourcePointRecovery
    {
        /// <summary>
        /// 按 <see cref="ResourcePointData.PointId"/> 匹配并应用服务器回放的已开箱清单。
        /// </summary>
        /// <param name="points">本局全部物资点数据（联网装配时每点都带 PointId）。</param>
        /// <param name="looted">服务器回放的已开箱清单（含每点完整 itemIds）。</param>
        /// <exception cref="InvalidOperationException">服务器回放清单与本地状态不一致：
        /// 记录匹配不到实体、同一记录重复出现、或已就绪点的清单与回放不同。</exception>
        public static void Apply(IReadOnlyList<ResourcePointData> points, List<NetLootRecord> looted)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (looted == null || looted.Count == 0)
            {
                return;
            }

            foreach (NetLootRecord record in looted)
            {
                if (record == null || string.IsNullOrEmpty(record.pointId))
                {
                    throw new InvalidOperationException("Loot record is missing pointId.");
                }

                ResourcePointData point = FindByPointId(points, record.pointId);
                if (point == null)
                {
                    // 服务器只回放本局生成出的点，本地同种子必然装配出同一批实体；匹配不到即是装配缺陷。
                    throw new InvalidOperationException(
                        "Looted point '" + record.pointId + "' has no matching resource point entity.");
                }

                if (!point.ContentsReady)
                {
                    point.ApplyItems(record.itemIds);
                    // 填满掉落计数：这些物品在断线前已经掉到地图上并被玩家拾走，
                    // 恢复后不能再由交互重新掉一遍 → 不可再交互。
                    point.DroppedItemCount = point.ItemCount;
                    continue;
                }

                // 本地已开过箱（loot 响应已灌入）后重连：校验回放一致即不动本地状态。
                if (!ItemsMatch(point.ItemIds, record.itemIds))
                {
                    throw new InvalidOperationException(
                        "Resource point '" + record.pointId + "' contents mismatch between local and server replay.");
                }
            }
        }

        /// <summary>
        /// 在服务器回放的已开箱清单中查找某点的清单（开局幂等落入活跃 run 时装配"已开箱"数据用）。
        /// 找不到返回 false；找到时 <paramref name="itemIds"/> 为该点完整掉落清单（可能为空清单）。
        /// </summary>
        public static bool TryGetLootedItems(List<NetLootRecord> looted, string pointId, out List<int> itemIds)
        {
            if (looted != null && !string.IsNullOrEmpty(pointId))
            {
                for (int i = 0; i < looted.Count; i++)
                {
                    NetLootRecord record = looted[i];
                    if (record != null && record.pointId == pointId)
                    {
                        itemIds = record.itemIds ?? new List<int>();
                        return true;
                    }
                }
            }

            itemIds = null;
            return false;
        }

        private static ResourcePointData FindByPointId(IReadOnlyList<ResourcePointData> points, string pointId)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] != null && points[i].PointId == pointId)
                {
                    return points[i];
                }
            }

            return null;
        }

        private static bool ItemsMatch(IReadOnlyList<int> local, List<int> server)
        {
            if (local.Count != server.Count)
            {
                return false;
            }

            for (int i = 0; i < local.Count; i++)
            {
                if (local[i] != server[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
