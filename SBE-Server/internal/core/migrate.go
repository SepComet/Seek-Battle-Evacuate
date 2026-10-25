package core

import (
	"fmt"

	"sbe-server/internal/protocol"
)

// Migrate 用上传的本地数据构建初始状态。仅允许作用于空状态；
// 是否已初始化的最终裁决在存储层（initialized 标志），此处对非空状态直接拒绝。
func Migrate(s State, p protocol.MigratePayload, items ItemTable) (State, error) {
	if len(s.Rows) > 0 || len(s.Characters) > 0 {
		return s, fmt.Errorf("%w: migrate onto non-empty state", ErrRejected)
	}
	if p.Items == nil {
		p.Items = []protocol.MigrateItem{}
	}
	if p.Characters == nil {
		p.Characters = []protocol.MigrateChar{}
	}

	next := s.Clone()
	next.Rows = make([]Row, 0, len(p.Items)+2*len(p.Characters))
	next.Characters = make([]Character, 0, len(p.Characters))

	// 1. 花名册与穿戴装备（装备铸为 wearer=cid 的行）
	for _, c := range p.Characters {
		if c.CID <= 0 {
			return s, fmt.Errorf("%w: invalid cid %d", ErrRejected, c.CID)
		}
		next.Characters = append(next.Characters, Character{CID: c.CID})

		if c.WeaponItemID != 0 {
			def, ok := items.Get(c.WeaponItemID)
			if !ok || def.EquipSlot != 1 {
				return s, fmt.Errorf("%w: character %d weapon %d invalid", ErrRejected, c.CID, c.WeaponItemID)
			}
			next.NextUID++
			next.Rows = append(next.Rows, Row{UID: next.NextUID, ItemID: c.WeaponItemID, Count: 1, Wearer: c.CID})
		}
		if c.ArmorItemID != 0 {
			def, ok := items.Get(c.ArmorItemID)
			if !ok || def.EquipSlot != 2 {
				return s, fmt.Errorf("%w: character %d armor %d invalid", ErrRejected, c.CID, c.ArmorItemID)
			}
			next.NextUID++
			next.Rows = append(next.Rows, Row{UID: next.NextUID, ItemID: c.ArmorItemID, Count: 1, Wearer: c.CID})
		}
	}

	// 2. 仓库网格物品
	for _, it := range p.Items {
		def, ok := items.Get(it.ItemID)
		if !ok {
			return s, fmt.Errorf("%w: unknown itemId %d", ErrRejected, it.ItemID)
		}
		if it.Count <= 0 || it.Count > def.StackLimit {
			return s, fmt.Errorf("%w: itemId %d count %d exceeds stackLimit %d", ErrRejected, it.ItemID, it.Count, def.StackLimit)
		}
		if it.X < 0 || it.Y < 0 {
			return s, fmt.Errorf("%w: negative position", ErrRejected)
		}
		next.NextUID++
		next.Rows = append(next.Rows, Row{
			UID: next.NextUID, ItemID: it.ItemID, Count: it.Count,
			X: it.X, Y: it.Y, Rotated: it.R,
		})
	}

	next.Initialized = true
	return next, nil
}
