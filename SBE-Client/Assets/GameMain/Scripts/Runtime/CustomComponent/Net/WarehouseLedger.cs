using System;
using System.Collections.Generic;

namespace SepCore.Net
{
    /// <summary>
    /// 服务器侧仓库行在客户端的账本镜像。
    /// SaveData 不携带服务器 UID，账本负责保存 UID 与网格/穿戴状态的对应关系，
    /// 供经济差分（EconomyDiff）推导操作集。
    /// </summary>
    public sealed class WarehouseLedger
    {
        /// <summary>
        /// 账本行。
        /// </summary>
        public struct LedgerRow
        {
            public long Uid;
            public int ItemId;
            public int Count;
            public int X;
            public int Y;
            public bool Rotated;
            public int Wearer;
        }

        private readonly List<LedgerRow> _rows = new List<LedgerRow>();
        private readonly List<int> _characterIds = new List<int>();

        /// <summary>
        /// 全部行（含穿戴行）。
        /// </summary>
        public IReadOnlyList<LedgerRow> Rows => _rows;

        /// <summary>
        /// 花名册（保持服务器顺序）。
        /// </summary>
        public IReadOnlyList<int> CharacterIds => _characterIds;

        public bool HasSynced { get; private set; }

        /// <summary>
        /// 用服务器快照整体重建账本。
        /// </summary>
        public void Rebuild(NetSnapshot snapshot)
        {
            _rows.Clear();
            _characterIds.Clear();
            if (snapshot != null)
            {
                if (snapshot.items != null)
                {
                    foreach (NetSnapRow row in snapshot.items)
                    {
                        _rows.Add(new LedgerRow
                        {
                            Uid = row.uid,
                            ItemId = row.itemId,
                            Count = row.count,
                            X = row.x,
                            Y = row.y,
                            Rotated = row.r,
                            Wearer = row.wearer,
                        });
                    }
                }

                if (snapshot.characters != null)
                {
                    foreach (NetSnapChar c in snapshot.characters)
                    {
                        _characterIds.Add(c.cid);
                    }
                }
            }

            HasSynced = true;
        }

        /// <summary>
        /// 查询角色的全部穿戴行（武器与防具各至多一行，按 ItemConfig.EquipSlot 区分由调用方过滤）；无穿戴返回空列表。
        /// </summary>
        public List<LedgerRow> GetWornRows(int characterId)
        {
            List<LedgerRow> result = new List<LedgerRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Wearer == characterId)
                {
                    result.Add(_rows[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// 网格行（不含穿戴行）。
        /// </summary>
        public List<LedgerRow> GetGridRows()
        {
            List<LedgerRow> result = new List<LedgerRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Wearer == 0)
                {
                    result.Add(_rows[i]);
                }
            }

            return result;
        }
    }
}
