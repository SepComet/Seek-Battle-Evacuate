package core

import (
	"reflect"
	"testing"

	"sbe-server/internal/protocol"
)

func TestApplyPartySetsOrderedQueue(t *testing.T) {
	s := stateWithRecruits(1001, 1002, 1003)
	next, err := Apply(s, []protocol.Op{{K: protocol.OpParty, Party: []int{1003, 1001}}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Party) != 2 || next.Party[0] != 1003 || next.Party[1] != 1001 {
		t.Fatalf("party order wrong: %+v", next.Party)
	}
}

func TestApplyPartyEmptyClearsQueue(t *testing.T) {
	s := stateWithRecruits(1001)
	s.Party = []int{1001}
	next, err := Apply(s, []protocol.Op{{K: protocol.OpParty, Party: nil}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Party) != 0 {
		t.Fatalf("party not cleared: %+v", next.Party)
	}
}

func TestApplyPartyRejectsForeignCharacter(t *testing.T) {
	s := stateWithRecruits(1001)
	if _, err := Apply(s, []protocol.Op{{K: protocol.OpParty, Party: []int{1001, 9999}}}, testItems(), testLimits()); err == nil {
		t.Fatal("want error for cid not in roster")
	}
}

func TestApplyPartyRejectsDuplicate(t *testing.T) {
	s := stateWithRecruits(1001, 1002)
	if _, err := Apply(s, []protocol.Op{{K: protocol.OpParty, Party: []int{1001, 1001}}}, testItems(), testLimits()); err == nil {
		t.Fatal("want error for duplicate cid")
	}
}

func TestApplyPartyRejectsOverMax(t *testing.T) {
	s := stateWithRecruits(1001, 1002, 1003, 1004, 1005)
	_, err := Apply(s, []protocol.Op{{K: protocol.OpParty, Party: []int{1001, 1002, 1003, 1004, 1005}}}, testItems(), testLimits())
	if err == nil {
		t.Fatal("want error for party over max size")
	}
	if len(s.Party) != 0 {
		t.Fatal("input state mutated on error")
	}
}

func TestApplyRecruitThenPartyInSameBatch(t *testing.T) {
	s := stateWithRecruits(1001)
	next, err := Apply(s, []protocol.Op{
		{K: protocol.OpRecruit, CID: 1002},
		{K: protocol.OpParty, Party: []int{1002}},
	}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Party) != 1 || next.Party[0] != 1002 {
		t.Fatalf("party after recruit wrong: %+v", next.Party)
	}
}

func TestApplyDifficultySetsAndValidates(t *testing.T) {
	s := State{}
	next, err := Apply(s, []protocol.Op{{K: protocol.OpDiff, Difficulty: 2}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if next.Difficulty != 2 {
		t.Fatalf("difficulty not set: %d", next.Difficulty)
	}
	if _, err := Apply(next, []protocol.Op{{K: protocol.OpDiff, Difficulty: 9}}, testItems(), testLimits()); err == nil {
		t.Fatal("want error for unknown difficulty")
	}
	// 0 = 取消配置，合法
	next, err = Apply(next, []protocol.Op{{K: protocol.OpDiff, Difficulty: 0}}, testItems(), testLimits())
	if err != nil || next.Difficulty != 0 {
		t.Fatalf("difficulty reset failed: %d %v", next.Difficulty, err)
	}
}

func TestApplyCarryReplacesList(t *testing.T) {
	s := State{}
	next, err := Apply(s, []protocol.Op{{K: protocol.OpCarry, Carry: []protocol.Stack{
		{ItemID: 5001, Count: 5},
		{ItemID: 5301, Count: 2},
	}}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Carried) != 2 || next.Carried[1] != (Stack{ItemID: 5301, Count: 2}) {
		t.Fatalf("carry wrong: %+v", next.Carried)
	}
	// 整表替换：下一次替换覆盖而非追加
	next, err = Apply(next, []protocol.Op{{K: protocol.OpCarry, Carry: []protocol.Stack{{ItemID: 5001, Count: 1}}}}, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Apply returned error: %v", err)
	}
	if len(next.Carried) != 1 || next.Carried[0].Count != 1 {
		t.Fatalf("carry replace failed: %+v", next.Carried)
	}
}

func TestApplyCarryRejectsInvalid(t *testing.T) {
	cases := []struct {
		name string
		ops  []protocol.Op
	}{
		{"unknown item", []protocol.Op{{K: protocol.OpCarry, Carry: []protocol.Stack{{ItemID: 9999, Count: 1}}}}},
		{"over stack limit", []protocol.Op{{K: protocol.OpCarry, Carry: []protocol.Stack{{ItemID: 5301, Count: 6}}}}},
		{"zero count", []protocol.Op{{K: protocol.OpCarry, Carry: []protocol.Stack{{ItemID: 5001, Count: 0}}}}},
		{"over backpack slots", []protocol.Op{{K: protocol.OpCarry, Carry: []protocol.Stack{
			{ItemID: 5001, Count: 1}, {ItemID: 5001, Count: 1}, {ItemID: 5001, Count: 1}, {ItemID: 5001, Count: 1},
		}}}},
	}
	for _, c := range cases {
		if _, err := Apply(State{}, c.ops, testItems(), testLimits()); err == nil {
			t.Fatalf("%s: want error", c.name)
		}
	}
}

func TestApplyLoadoutAtomicOnAnyFailure(t *testing.T) {
	s := stateWithRecruits(1001)
	s.Difficulty = 2
	_, err := Apply(s, []protocol.Op{
		{K: protocol.OpDiff, Difficulty: 3},
		{K: protocol.OpParty, Party: []int{9999}}, // 失败：不在花名册
	}, testItems(), testLimits())
	if err == nil {
		t.Fatal("want error")
	}
	if s.Difficulty != 2 {
		t.Fatalf("input state mutated: %d", s.Difficulty)
	}
}

func TestLoadoutBatchFailureReturnsOriginalState(t *testing.T) {
	s := stateWithRecruits(1001, 1002)
	s.Party = []int{1001}
	s.Difficulty = 1
	s.Carried = []Stack{{ItemID: 5001, Count: 2}}
	want := s.ToSnapshot()
	cases := [][]protocol.Op{
		{{K: protocol.OpParty, Party: []int{1002}}, {K: protocol.OpDiff, Difficulty: 99}},
		{{K: protocol.OpDiff, Difficulty: 2}, {K: protocol.OpCarry, Carry: []protocol.Stack{{ItemID: 5001, Count: 0}}}},
		{{K: protocol.OpCarry, Carry: []protocol.Stack{{ItemID: 5001, Count: 3}}}, {K: protocol.OpParty, Party: []int{9999}}},
	}
	for i, ops := range cases {
		next, err := Apply(s, ops, testItems(), testLimits())
		if err == nil || !reflect.DeepEqual(next.ToSnapshot(), want) || !reflect.DeepEqual(s.ToSnapshot(), want) {
			t.Fatalf("case %d: rejected batch changed state: input=%+v returned=%+v error=%v", i, s, next, err)
		}
	}
}

func TestLoadoutSnapshotsAndClonesDoNotAliasState(t *testing.T) {
	s := stateWithRecruits(1001)
	s.Party = []int{1001}
	s.Carried = []Stack{{ItemID: 5001, Count: 2}}
	clone, snap := s.Clone(), s.ToSnapshot()
	clone.Party[0], snap.Party[0] = 2001, 3001
	clone.Carried[0].Count, snap.Carried[0].Count = 3, 4
	if s.Party[0] != 1001 || s.Carried[0].Count != 2 {
		t.Fatalf("state aliased clone/snapshot: %+v", s)
	}
}

func TestMigrateWithLoadout(t *testing.T) {
	payload := protocol.MigratePayload{
		Characters: []protocol.MigrateChar{{CID: 1001}, {CID: 1002}},
		Party:      []int{1002, 1001},
		Difficulty: 3,
		Carried:    []protocol.Stack{{ItemID: 5001, Count: 10}},
	}
	next, err := Migrate(State{}, payload, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Migrate returned error: %v", err)
	}
	if len(next.Party) != 2 || next.Party[0] != 1002 || next.Difficulty != 3 || len(next.Carried) != 1 {
		t.Fatalf("loadout wrong: party=%+v diff=%d carried=%+v", next.Party, next.Difficulty, next.Carried)
	}
}

func TestMigrateRejectsForeignPartyCID(t *testing.T) {
	payload := protocol.MigratePayload{
		Characters: []protocol.MigrateChar{{CID: 1001}},
		Party:      []int{1002}, // 未随迁移上传的角色
	}
	if _, err := Migrate(State{}, payload, testItems(), testLimits()); err == nil {
		t.Fatal("want error for party cid not in migrated characters")
	}
}

func TestMigrateRejectsBadDifficultyAndCarry(t *testing.T) {
	if _, err := Migrate(State{}, protocol.MigratePayload{
		Characters: []protocol.MigrateChar{{CID: 1001}},
		Difficulty: 9,
	}, testItems(), testLimits()); err == nil {
		t.Fatal("want error for unknown difficulty")
	}
	if _, err := Migrate(State{}, protocol.MigratePayload{
		Characters: []protocol.MigrateChar{{CID: 1001}},
		Carried:    []protocol.Stack{{ItemID: 9999, Count: 1}},
	}, testItems(), testLimits()); err == nil {
		t.Fatal("want error for unknown carry item")
	}
}
