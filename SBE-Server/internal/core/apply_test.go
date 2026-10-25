package core

import (
	"testing"

	"sbe-server/internal/protocol"
)

// fakeItems 内存物品表：5001 消耗品堆叠 99；5101 武器；5201 防具；5301 堆叠 5。
type fakeItems map[int]ItemDef

func (f fakeItems) Get(id int) (ItemDef, bool) {
	d, ok := f[id]
	return d, ok
}

func testItems() fakeItems {
	return fakeItems{
		5001: {StackLimit: 99, EquipSlot: 0},
		5101: {StackLimit: 1, EquipSlot: 1},
		5102: {StackLimit: 1, EquipSlot: 1},
		5201: {StackLimit: 1, EquipSlot: 2},
		5301: {StackLimit: 5, EquipSlot: 0},
	}
}

func testLimits() Limits {
	return Limits{
		MaxPlayerPartySize: 4,
		BackpackSlotCount:  3,
		Difficulties:       map[int]bool{1: true, 2: true, 3: true},
	}
}

func stateWithRecruits(cids ...int) State {
	s := State{}
	for _, cid := range cids {
		s.Characters = append(s.Characters, Character{CID: cid})
	}
	return s
}

func TestApplyAddMintsUID(t *testing.T) {
	s := stateWithRecruits(1001)
	next, err := Apply(s, []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: 5001, Count: 5, X: 0, Y: 0}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Rows) != 1 {
		t.Fatalf("want 1 row, got %d", len(next.Rows))
	}
	row := next.Rows[0]
	if row.UID != 1 || row.ItemID != 5001 || row.Count != 5 || row.Wearer != 0 {
		t.Fatalf("unexpected row: %+v", row)
	}
	if next.NextUID != 1 {
		t.Fatalf("want NextUID 1, got %d", next.NextUID)
	}
	// 输入状态不被修改（原子性）
	if len(s.Rows) != 0 {
		t.Fatalf("input state mutated")
	}
}

func TestApplyAddRejectsUnknownItem(t *testing.T) {
	s := stateWithRecruits(1001)
	_, err := Apply(s, []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: 9999, Count: 1}}, testItems(), testLimits())
	if err == nil {
		t.Fatal("want error for unknown item")
	}
	if len(s.Rows) != 0 {
		t.Fatal("input state mutated on error")
	}
}

func TestApplyAddRejectsOverStackLimit(t *testing.T) {
	s := stateWithRecruits(1001)
	_, err := Apply(s, []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: 5001, Count: 100}}, testItems(), testLimits())
	if err == nil {
		t.Fatal("want error for count over stack limit")
	}
}

func TestApplyAddRejectsMissingTempID(t *testing.T) {
	s := stateWithRecruits(1001)
	if _, err := Apply(s, []protocol.Op{{K: protocol.OpAdd, ItemID: 5001, Count: 1}}, testItems(), testLimits()); err == nil {
		t.Fatal("want error for missing tid")
	}
}

func TestApplyDelWholeAndPartial(t *testing.T) {
	s := State{NextUID: 10, Rows: []Row{{UID: 7, ItemID: 5001, Count: 9, X: 1, Y: 1}}}
	next, err := Apply(s, []protocol.Op{{K: protocol.OpDel, UID: 7, Count: 4}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Rows) != 1 || next.Rows[0].Count != 5 {
		t.Fatalf("partial del failed: %+v", next.Rows)
	}
	next, err = Apply(next, []protocol.Op{{K: protocol.OpDel, UID: 7}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Rows) != 0 {
		t.Fatalf("whole del failed: %+v", next.Rows)
	}
}

func TestApplyDelRejectsMissingUID(t *testing.T) {
	s := State{NextUID: 2}
	if _, err := Apply(s, []protocol.Op{{K: protocol.OpDel, UID: 99}}, testItems(), testLimits()); err == nil {
		t.Fatal("want error for missing uid")
	}
}

func TestApplyMovUpdatesPosition(t *testing.T) {
	s := State{NextUID: 2, Rows: []Row{{UID: 1, ItemID: 5001, Count: 3, X: 0, Y: 0}}}
	next, err := Apply(s, []protocol.Op{{K: protocol.OpMov, UID: 1, X: 4, Y: 5, R: true}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if next.Rows[0].X != 4 || next.Rows[0].Y != 5 || !next.Rows[0].Rotated {
		t.Fatalf("mov failed: %+v", next.Rows[0])
	}
}

func TestApplyRelReturnsWornToGrid(t *testing.T) {
	s := stateWithRecruits(1001)
	s.NextUID = 2
	s.Rows = []Row{{UID: 1, ItemID: 5101, Count: 1, Wearer: 1001}}
	next, err := Apply(s, []protocol.Op{{K: protocol.OpRel, UID: 1, X: 2, Y: 3}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if next.Rows[0].Wearer != 0 || next.Rows[0].X != 2 || next.Rows[0].Y != 3 {
		t.Fatalf("rel failed: %+v", next.Rows[0])
	}
}

func TestApplyWearByUID(t *testing.T) {
	s := stateWithRecruits(1001)
	s.NextUID = 2
	s.Rows = []Row{{UID: 1, ItemID: 5101, Count: 1, X: 0, Y: 0}}
	next, err := Apply(s, []protocol.Op{{K: protocol.OpWear, CID: 1001, Slot: 1, UID: 1}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if next.Rows[0].Wearer != 1001 {
		t.Fatalf("wear failed: %+v", next.Rows[0])
	}
}

func TestApplyWearRejectsSlotMismatch(t *testing.T) {
	s := stateWithRecruits(1001)
	s.NextUID = 2
	s.Rows = []Row{{UID: 1, ItemID: 5101, Count: 1}} // 武器
	if _, err := Apply(s, []protocol.Op{{K: protocol.OpWear, CID: 1001, Slot: 2, UID: 1}}, testItems(), testLimits()); err == nil {
		t.Fatal("want error for slot mismatch")
	}
}

func TestApplyWearRejectsUnknownCharacter(t *testing.T) {
	s := stateWithRecruits(1001)
	s.NextUID = 2
	s.Rows = []Row{{UID: 1, ItemID: 5101, Count: 1}}
	if _, err := Apply(s, []protocol.Op{{K: protocol.OpWear, CID: 8888, Slot: 1, UID: 1}}, testItems(), testLimits()); err == nil {
		t.Fatal("want error for unknown character")
	}
}

func TestApplyWearReplacesOldRow(t *testing.T) {
	s := stateWithRecruits(1001)
	s.NextUID = 3
	s.Rows = []Row{
		{UID: 1, ItemID: 5101, Count: 1, Wearer: 1001},
		{UID: 2, ItemID: 5102, Count: 1, X: 5, Y: 5},
	}
	next, err := Apply(s, []protocol.Op{{K: protocol.OpWear, CID: 1001, Slot: 1, UID: 2}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if next.Rows[0].Wearer != 0 {
		t.Fatalf("old worn row not released: %+v", next.Rows[0])
	}
	if next.Rows[1].Wearer != 1001 {
		t.Fatalf("new row not worn: %+v", next.Rows[1])
	}
}

func TestApplyWearByTempIDFromAddInSameBatch(t *testing.T) {
	s := stateWithRecruits(1001)
	next, err := Apply(s, []protocol.Op{
		{K: protocol.OpAdd, TID: 41, ItemID: 5101, Count: 1},
		{K: protocol.OpWear, CID: 1001, Slot: 1, TID: 41},
	}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Rows) != 1 || next.Rows[0].Wearer != 1001 || next.Rows[0].ItemID != 5101 {
		t.Fatalf("wear-by-tid failed: %+v", next.Rows)
	}
}

func TestApplyWearByItemIDMintsWornRow(t *testing.T) {
	s := stateWithRecruits(1001)
	next, err := Apply(s, []protocol.Op{{K: protocol.OpWear, CID: 1001, Slot: 1, ItemID: 5101}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Rows) != 1 || next.Rows[0].Wearer != 1001 || next.Rows[0].Count != 1 {
		t.Fatalf("wear-by-itemId failed: %+v", next.Rows)
	}
}

func TestApplyWearRejectsUnknownTempID(t *testing.T) {
	s := stateWithRecruits(1001)
	if _, err := Apply(s, []protocol.Op{{K: protocol.OpWear, CID: 1001, Slot: 1, TID: 404}}, testItems(), testLimits()); err == nil {
		t.Fatal("want error for unknown tid")
	}
}

func TestApplyRecruitAppendsAndRejectsDuplicate(t *testing.T) {
	s := stateWithRecruits(1001)
	next, err := Apply(s, []protocol.Op{{K: protocol.OpRecruit, CID: 1002}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Characters) != 2 || next.Characters[1].CID != 1002 {
		t.Fatalf("recruit failed: %+v", next.Characters)
	}
	if _, err := Apply(next, []protocol.Op{{K: protocol.OpRecruit, CID: 1002}}, testItems(), testLimits()); err == nil {
		t.Fatal("want error for duplicate recruit")
	}
}

func TestApplyIsAtomicOnAnyFailure(t *testing.T) {
	s := stateWithRecruits(1001)
	s.NextUID = 2
	s.Rows = []Row{{UID: 1, ItemID: 5001, Count: 3}}
	_, err := Apply(s, []protocol.Op{
		{K: protocol.OpAdd, TID: 1, ItemID: 5001, Count: 1},
		{K: protocol.OpDel, UID: 999}, // 失败：不存在
	}, testItems(), testLimits())
	if err == nil {
		t.Fatal("want error")
	}
	if len(s.Rows) != 1 || s.Rows[0].Count != 3 {
		t.Fatalf("input state mutated: %+v", s.Rows)
	}
}

func TestApplyFullSettlementBatch(t *testing.T) {
	// 战局结算的完整批次：招募新角色、战利品入库、旧武器销毁、捡到的新武器穿上。
	s := stateWithRecruits(1001)
	s.NextUID = 3
	s.Rows = []Row{
		{UID: 1, ItemID: 5101, Count: 1, Wearer: 1001}, // 旧武器（将被销毁）
		{UID: 2, ItemID: 5001, Count: 10, X: 0, Y: 0},
	}
	next, err := Apply(s, []protocol.Op{
		{K: protocol.OpRecruit, CID: 1002},
		{K: protocol.OpAdd, TID: 7, ItemID: 5301, Count: 4, X: 1, Y: 0}, // 战利品
		{K: protocol.OpAdd, TID: 8, ItemID: 5102, Count: 1},             // 新武器（直接穿上）
		{K: protocol.OpDel, UID: 1},                                     // 销毁旧武器
		{K: protocol.OpWear, CID: 1001, Slot: 1, TID: 8},
	}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Rows) != 3 {
		t.Fatalf("want 3 rows, got %d: %+v", len(next.Rows), next.Rows)
	}
	for _, r := range next.Rows {
		if r.UID == 1 {
			t.Fatal("destroyed row still present")
		}
	}
	worn := next.wornRowInSlot(testItems(), 1001, 1)
	if worn < 0 || next.Rows[worn].ItemID != 5102 {
		t.Fatalf("new weapon not worn: %+v", next.Rows)
	}
}
