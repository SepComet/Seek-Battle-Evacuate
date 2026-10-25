using System;
using System.Collections.Generic;
using SepCore.Definition;

namespace SepCore.Net
{
    /// <summary>
    /// 经济差分器：比较服务器账本与本地 SaveData，推导出等价的操作集。
    /// 大厅穿卸/布局整理与局内结算共用同一套推导，保证服务器终态与本地终态一致。
    /// 纯函数，不产生任何 IO。
    /// </summary>
    public static class EconomyDiff
    {
        /// <summary>
        /// 计算把服务器状态迁移到 save 当前状态所需的操作集。
        /// </summary>
        /// <param name="ledger">最近一次同步的服务器账本。</param>
        /// <param name="save">本地内存存档（已应用玩家操作/结算结果）。</param>
        /// <param name="itemGetter">物品配置查询（判断装备槽位）。</param>
        public static List<NetOp> Compute(WarehouseLedger ledger, SaveData save, Func<int, ItemConfig> itemGetter)
        {
            List<NetOp> ops = new List<NetOp>();
            if (save == null || !ledger.HasSynced)
            {
                return ops;
            }

            List<NetOp> rels = new List<NetOp>();
            List<NetOp> dels = new List<NetOp>();
            List<NetOp> adds = new List<NetOp>();
            List<NetOp> movs = new List<NetOp>();
            List<NetOp> wears = new List<NetOp>();
            int nextTid = 1;

            // 0. 花名册新增
            HashSet<int> knownCids = new HashSet<int>(ledger.CharacterIds);
            if (save.characters != null)
            {
                foreach (CharacterSave c in save.characters)
                {
                    if (c.characterId > 0 && !knownCids.Contains(c.characterId))
                    {
                        ops.Add(NetOp.Recruit(c.characterId));
                        knownCids.Add(c.characterId);
                    }
                }
            }

            List<WarehouseLedger.LedgerRow> ledgerGrid = ledger.GetGridRows();
            bool[] ledgerGridUsed = new bool[ledgerGrid.Count];
            List<GridItemStack> saveStacks = save.mainWarehouse != null
                ? new List<GridItemStack>(save.mainWarehouse)
                : new List<GridItemStack>();
            bool[] saveStackUsed = new bool[saveStacks.Count];

            // 1. 精确匹配：同物品同数量同位置视为未变化
            for (int i = 0; i < ledgerGrid.Count; i++)
            {
                if (ledgerGridUsed[i])
                {
                    continue;
                }

                for (int j = 0; j < saveStacks.Count; j++)
                {
                    if (saveStackUsed[j])
                    {
                        continue;
                    }

                    GridItemStack s = saveStacks[j];
                    if (s.itemId == ledgerGrid[i].ItemId && s.count == ledgerGrid[i].Count &&
                        s.x == ledgerGrid[i].X && s.y == ledgerGrid[i].Y && s.isRotated == ledgerGrid[i].Rotated)
                    {
                        ledgerGridUsed[i] = true;
                        saveStackUsed[j] = true;
                        break;
                    }
                }
            }

            // 2. 穿戴槽位变化
            if (save.characters != null)
            {
                foreach (CharacterSave c in save.characters)
                {
                    AnalyzeSlot(ledger, itemGetter, c.characterId, NetSlot.Weapon, c.weaponItemId,
                        ledgerGrid, ledgerGridUsed, saveStacks, saveStackUsed,
                        rels, dels, wears);
                    AnalyzeSlot(ledger, itemGetter, c.characterId, NetSlot.Armor, c.armorItemId,
                        ledgerGrid, ledgerGridUsed, saveStacks, saveStackUsed,
                        rels, dels, wears);
                }
            }

            // 3. 剩余网格差分
            for (int i = 0; i < ledgerGrid.Count; i++)
            {
                if (ledgerGridUsed[i])
                {
                    continue;
                }

                WarehouseLedger.LedgerRow row = ledgerGrid[i];
                int paired = FindPairForLedgerRow(saveStacks, saveStackUsed, row);
                if (paired >= 0)
                {
                    GridItemStack s = saveStacks[paired];
                    saveStackUsed[paired] = true;
                    if (s.count < row.Count)
                    {
                        dels.Add(NetOp.Del(row.Uid, row.Count - s.count));
                        if (s.x != row.X || s.y != row.Y || s.isRotated != row.Rotated)
                        {
                            movs.Add(NetOp.Mov(row.Uid, s.x, s.y, s.isRotated));
                        }
                    }
                    else
                    {
                        // 数量增加视作“删旧行 + 铸新行”，与服务器行语义保持一致
                        dels.Add(NetOp.Del(row.Uid, 0));
                        adds.Add(NetOp.Add(nextTid++, s.itemId, s.count, s.x, s.y, s.isRotated));
                    }
                }
                else
                {
                    dels.Add(NetOp.Del(row.Uid, 0));
                }
            }

            for (int j = 0; j < saveStacks.Count; j++)
            {
                if (!saveStackUsed[j])
                {
                    GridItemStack s = saveStacks[j];
                    adds.Add(NetOp.Add(nextTid++, s.itemId, s.count, s.x, s.y, s.isRotated));
                }
            }

            // 4. 固定输出顺序：rel 需先于 wear（先腾出槽位），add 需先于对它的引用
            ops.AddRange(rels);
            ops.AddRange(dels);
            ops.AddRange(adds);
            ops.AddRange(movs);
            ops.AddRange(wears);
            return ops;
        }

        private static void AnalyzeSlot(
            WarehouseLedger ledger,
            Func<int, ItemConfig> itemGetter,
            int characterId,
            int slot,
            int saveItemId,
            List<WarehouseLedger.LedgerRow> ledgerGrid,
            bool[] ledgerGridUsed,
            List<GridItemStack> saveStacks,
            bool[] saveStackUsed,
            List<NetOp> rels,
            List<NetOp> dels,
            List<NetOp> wears)
        {
            // 账本中该角色该槽位的穿戴行
            WarehouseLedger.LedgerRow? worn = null;
            foreach (WarehouseLedger.LedgerRow row in ledger.GetWornRows(characterId))
            {
                ItemConfig config = itemGetter(row.ItemId);
                if (config != null && (int)config.EquipSlot == slot)
                {
                    worn = row;
                    break;
                }
            }

            bool slotUnchanged = worn.HasValue && worn.Value.ItemId == saveItemId;
            if (slotUnchanged)
            {
                return;
            }

            // 旧装备去处：回网格（rel）或销毁（del）
            if (worn.HasValue)
            {
                int target = FindAppearedStack(saveStacks, saveStackUsed, worn.Value.ItemId);
                if (target >= 0)
                {
                    GridItemStack s = saveStacks[target];
                    saveStackUsed[target] = true;
                    rels.Add(NetOp.Rel(worn.Value.Uid, s.x, s.y, s.isRotated));
                }
                else
                {
                    dels.Add(NetOp.Del(worn.Value.Uid, 0));
                }
            }

            // 新装备来源：网格行（wear by uid）或局内新获得（wear by itemId 铸造）
            if (saveItemId > 0)
            {
                long sourceUid = 0;
                for (int i = 0; i < ledgerGrid.Count; i++)
                {
                    if (!ledgerGridUsed[i] && ledgerGrid[i].ItemId == saveItemId)
                    {
                        sourceUid = ledgerGrid[i].Uid;
                        ledgerGridUsed[i] = true;
                        break;
                    }
                }

                if (sourceUid > 0)
                {
                    wears.Add(NetOp.WearByUid(characterId, slot, sourceUid));
                }
                else
                {
                    wears.Add(NetOp.WearByItemId(characterId, slot, saveItemId));
                }
            }
        }

        /// <summary>
        /// 为剩余账本网格行寻找同物品的可配对 save 栈（数量不超过账本行的优先）。
        /// </summary>
        private static int FindPairForLedgerRow(List<GridItemStack> saveStacks, bool[] saveStackUsed, WarehouseLedger.LedgerRow row)
        {
            int best = -1;
            for (int j = 0; j < saveStacks.Count; j++)
            {
                if (saveStackUsed[j] || saveStacks[j].itemId != row.ItemId)
                {
                    continue;
                }

                if (best < 0 || Math.Abs(saveStacks[j].count - row.Count) < Math.Abs(saveStacks[best].count - row.Count))
                {
                    best = j;
                }
            }

            return best;
        }

        /// <summary>
        /// 查找“新出现”的同物品 save 栈（未被精确匹配占用），作为卸下装备的落点。
        /// </summary>
        private static int FindAppearedStack(List<GridItemStack> saveStacks, bool[] saveStackUsed, int itemId)
        {
            for (int j = 0; j < saveStacks.Count; j++)
            {
                if (!saveStackUsed[j] && saveStacks[j].itemId == itemId)
                {
                    return j;
                }
            }

            return -1;
        }
    }
}
