package core

import (
	"fmt"

	"sbe-server/internal/protocol"
)

// Apply 将一批局外操作按顺序应用到状态上。
// 任何一条操作校验失败即整体失败：返回原状态与错误，不产生部分应用（原子性）。
// tid 解析仅限本批：先执行的 add 操作铸造的 UID 可被后续 wear 以 tid 引用。
func Apply(s State, ops []protocol.Op, items ItemTable, limits Limits) (State, error) {
	next := s.Clone()
	tidUID := make(map[int]int64)

	for i, op := range ops {
		var err error
		switch op.K {
		case protocol.OpAdd:
			err = applyAdd(&next, op, items, tidUID)
		case protocol.OpDel:
			err = applyDel(&next, op)
		case protocol.OpMov:
			err = applyMov(&next, op)
		case protocol.OpRel:
			err = applyRel(&next, op)
		case protocol.OpWear:
			err = applyWear(&next, op, items, tidUID)
		case protocol.OpRecruit:
			err = applyRecruit(&next, op)
		case protocol.OpParty:
			err = applyParty(&next, op, limits)
		case protocol.OpDiff:
			err = applyDifficulty(&next, op, limits)
		case protocol.OpCarry:
			err = applyCarry(&next, op, items, limits)
		default:
			err = fmt.Errorf("unknown op kind %q", op.K)
		}
		if err != nil {
			return s, fmt.Errorf("op %d (%s): %w", i, op.K, err)
		}
	}

	return next, nil
}

func applyAdd(s *State, op protocol.Op, items ItemTable, tidUID map[int]int64) error {
	if op.TID <= 0 {
		return fmt.Errorf("%w: add requires tid", ErrRejected)
	}
	if _, dup := tidUID[op.TID]; dup {
		return fmt.Errorf("%w: duplicate tid %d", ErrRejected, op.TID)
	}
	def, ok := items.Get(op.ItemID)
	if !ok {
		return fmt.Errorf("%w: unknown itemId %d", ErrRejected, op.ItemID)
	}
	if op.Count <= 0 || op.Count > def.StackLimit {
		return fmt.Errorf("%w: itemId %d count %d exceeds stackLimit %d", ErrRejected, op.ItemID, op.Count, def.StackLimit)
	}
	if op.X < 0 || op.Y < 0 {
		return fmt.Errorf("%w: negative position", ErrRejected)
	}
	s.NextUID++
	s.Rows = append(s.Rows, Row{
		UID:     s.NextUID,
		ItemID:  op.ItemID,
		Count:   op.Count,
		X:       op.X,
		Y:       op.Y,
		Rotated: op.R,
	})
	tidUID[op.TID] = s.NextUID
	return nil
}

func applyDel(s *State, op protocol.Op) error {
	idx := s.findRow(op.UID)
	if idx < 0 {
		return fmt.Errorf("%w: unknown uid %d", ErrRejected, op.UID)
	}
	if op.Count < 0 {
		return fmt.Errorf("%w: negative count", ErrRejected)
	}
	if op.Count == 0 || op.Count >= s.Rows[idx].Count {
		s.Rows = append(s.Rows[:idx], s.Rows[idx+1:]...)
		return nil
	}
	s.Rows[idx].Count -= op.Count
	return nil
}

func applyMov(s *State, op protocol.Op) error {
	idx := s.findRow(op.UID)
	if idx < 0 {
		return fmt.Errorf("%w: unknown uid %d", ErrRejected, op.UID)
	}
	if op.X < 0 || op.Y < 0 {
		return fmt.Errorf("%w: negative position", ErrRejected)
	}
	s.Rows[idx].X = op.X
	s.Rows[idx].Y = op.Y
	s.Rows[idx].Rotated = op.R
	return nil
}

func applyRel(s *State, op protocol.Op) error {
	idx := s.findRow(op.UID)
	if idx < 0 {
		return fmt.Errorf("%w: unknown uid %d", ErrRejected, op.UID)
	}
	if op.X < 0 || op.Y < 0 {
		return fmt.Errorf("%w: negative position", ErrRejected)
	}
	s.Rows[idx].Wearer = 0
	s.Rows[idx].X = op.X
	s.Rows[idx].Y = op.Y
	s.Rows[idx].Rotated = op.R
	return nil
}

func applyWear(s *State, op protocol.Op, items ItemTable, tidUID map[int]int64) error {
	if !s.hasCharacter(op.CID) {
		return fmt.Errorf("%w: unknown character %d", ErrRejected, op.CID)
	}
	if op.Slot != 1 && op.Slot != 2 {
		return fmt.Errorf("%w: invalid slot %d", ErrRejected, op.Slot)
	}

	refs := 0
	if op.UID != 0 {
		refs++
	}
	if op.TID != 0 {
		refs++
	}
	if op.ItemID != 0 {
		refs++
	}
	if refs != 1 {
		return fmt.Errorf("%w: wear must reference exactly one of uid/tid/itemId", ErrRejected)
	}

	var uid int64
	var itemID int
	switch {
	case op.UID != 0:
		idx := s.findRow(op.UID)
		if idx < 0 {
			return fmt.Errorf("%w: unknown uid %d", ErrRejected, op.UID)
		}
		uid, itemID = op.UID, s.Rows[idx].ItemID
	case op.TID != 0:
		mapped, ok := tidUID[op.TID]
		if !ok {
			return fmt.Errorf("%w: unknown tid %d", ErrRejected, op.TID)
		}
		idx := s.findRow(mapped)
		if idx < 0 {
			return fmt.Errorf("%w: tid %d resolved to missing uid %d", ErrRejected, op.TID, mapped)
		}
		uid, itemID = mapped, s.Rows[idx].ItemID
	default:
		def, ok := items.Get(op.ItemID)
		if !ok {
			return fmt.Errorf("%w: unknown itemId %d", ErrRejected, op.ItemID)
		}
		if def.EquipSlot != op.Slot {
			return fmt.Errorf("%w: itemId %d not wearable in slot %d", ErrRejected, op.ItemID, op.Slot)
		}
		s.NextUID++
		uid = s.NextUID
		s.Rows = append(s.Rows, Row{UID: uid, ItemID: op.ItemID, Count: 1, Wearer: op.CID})
		itemID = op.ItemID
	}

	def, ok := items.Get(itemID)
	if !ok || def.EquipSlot != op.Slot {
		return fmt.Errorf("%w: itemId %d not wearable in slot %d", ErrRejected, itemID, op.Slot)
	}

	// 同槽位旧装备自动卸回网格原点（客户端下次拉取/整理时自愈落位）。
	if old := s.wornRowInSlot(items, op.CID, op.Slot); old >= 0 && s.Rows[old].UID != uid {
		s.Rows[old].Wearer = 0
		s.Rows[old].X, s.Rows[old].Y, s.Rows[old].Rotated = 0, 0, false
	}

	if idx := s.findRow(uid); idx >= 0 {
		s.Rows[idx].Wearer = op.CID
	}
	return nil
}

func applyRecruit(s *State, op protocol.Op) error {
	if op.CID <= 0 {
		return fmt.Errorf("%w: invalid cid %d", ErrRejected, op.CID)
	}
	if s.hasCharacter(op.CID) {
		return fmt.Errorf("%w: duplicate character %d", ErrRejected, op.CID)
	}
	s.Characters = append(s.Characters, Character{CID: op.CID})
	return nil
}

// applyParty 整队替换出战队列：顺序即出战序。空队合法（未配置/编辑中间态）。
func applyParty(s *State, op protocol.Op, limits Limits) error {
	if limits.MaxPlayerPartySize <= 0 {
		return fmt.Errorf("%w: MaxPlayerPartySize not configured", ErrRejected)
	}
	if len(op.Party) > limits.MaxPlayerPartySize {
		return fmt.Errorf("%w: party size %d exceeds max %d", ErrRejected, len(op.Party), limits.MaxPlayerPartySize)
	}
	seen := make(map[int]bool, len(op.Party))
	for _, cid := range op.Party {
		if cid <= 0 {
			return fmt.Errorf("%w: invalid cid %d", ErrRejected, cid)
		}
		if !s.hasCharacter(cid) {
			return fmt.Errorf("%w: character %d not in roster", ErrRejected, cid)
		}
		if seen[cid] {
			return fmt.Errorf("%w: duplicate character %d", ErrRejected, cid)
		}
		seen[cid] = true
	}
	party := make([]int, len(op.Party))
	copy(party, op.Party)
	s.Party = party
	return nil
}

func applyDifficulty(s *State, op protocol.Op, limits Limits) error {
	if op.Difficulty < 0 {
		return fmt.Errorf("%w: negative difficulty %d", ErrRejected, op.Difficulty)
	}
	if op.Difficulty != 0 && !limits.Difficulties[op.Difficulty] {
		return fmt.Errorf("%w: unknown difficulty %d", ErrRejected, op.Difficulty)
	}
	s.Difficulty = op.Difficulty
	return nil
}

// applyCarry 携带计划整表替换：无格位无实例身份，逐条校验物品合法性。
func applyCarry(s *State, op protocol.Op, items ItemTable, limits Limits) error {
	if limits.BackpackSlotCount <= 0 {
		return fmt.Errorf("%w: BackpackSlotCount not configured", ErrRejected)
	}
	if len(op.Carry) > limits.BackpackSlotCount {
		return fmt.Errorf("%w: carry %d stacks exceeds backpack slots %d", ErrRejected, len(op.Carry), limits.BackpackSlotCount)
	}
	for _, st := range op.Carry {
		def, ok := items.Get(st.ItemID)
		if !ok {
			return fmt.Errorf("%w: unknown itemId %d", ErrRejected, st.ItemID)
		}
		if st.Count <= 0 || st.Count > def.StackLimit {
			return fmt.Errorf("%w: itemId %d count %d exceeds stackLimit %d", ErrRejected, st.ItemID, st.Count, def.StackLimit)
		}
	}
	carried := make([]Stack, len(op.Carry))
	for i, st := range op.Carry {
		carried[i] = Stack{ItemID: st.ItemID, Count: st.Count}
	}
	s.Carried = carried
	return nil
}

// SumAdds 按 itemId 汇总一批操作里 add 的 Count（结算对账的「带出物资」毛口径）。
// 只统计 OpAdd：mov/rel/wear 等不产生新的「取得」，不进入额度。
func SumAdds(ops []protocol.Op) map[int]int {
	sums := make(map[int]int)
	for _, op := range ops {
		if op.K == protocol.OpAdd {
			sums[op.ItemID] += op.Count
		}
	}
	return sums
}

// SubtractDels 按 itemId 从毛口径里扣掉 del 的件数，得到净增量。
// 客户端 diff 把「已有堆叠数量增长」表达为「删整行 + 铸新行」：
// 结算对账必须按净增核验，否则堆叠 20→21 会被误判为凭空获得 21 件而整批误拒。
// del 的件数归属按 uid 查应用前状态的行；uid 不存在的 del 由 Apply 拒绝，这里跳过。
func SubtractDels(sums map[int]int, ops []protocol.Op, state State) {
	for _, op := range ops {
		if op.K != protocol.OpDel {
			continue
		}
		idx := state.findRow(op.UID)
		if idx < 0 {
			continue
		}
		count := op.Count
		if count <= 0 || count >= state.Rows[idx].Count {
			// 与 applyDel 同语义：count=0 或不小于行存量即整行删除
			count = state.Rows[idx].Count
		}
		sums[state.Rows[idx].ItemID] -= count
	}
}

// SumStacks 按 itemId 汇总一批无实例身份的物品堆（携带计划、战斗所得）。
func SumStacks(stacks []Stack) map[int]int {
	sums := make(map[int]int, len(stacks))
	for _, st := range stacks {
		sums[st.ItemID] += st.Count
	}
	return sums
}
