package core

import (
	"testing"

	"sbe-server/internal/protocol"
)

func TestMigrateBuildsInitialState(t *testing.T) {
	s := State{}
	payload := protocol.MigratePayload{
		Items: []protocol.MigrateItem{
			{ItemID: 5001, Count: 20, X: 0, Y: 0},
			{ItemID: 5301, Count: 5, X: 1, Y: 0, R: true},
		},
		Characters: []protocol.MigrateChar{
			{CID: 1001, WeaponItemID: 5101, ArmorItemID: 0},
			{CID: 1002, WeaponItemID: 0, ArmorItemID: 5201},
		},
	}
	next, err := Migrate(s, payload, testItems(), testLimits())
	if err != nil {
		t.Fatalf("Migrate returned error: %v", err)
	}
	if !next.Initialized {
		t.Fatal("want initialized after migrate")
	}
	if len(next.Characters) != 2 || next.Characters[0].CID != 1001 || next.Characters[1].CID != 1002 {
		t.Fatalf("characters wrong: %+v", next.Characters)
	}
	if len(next.Rows) != 4 {
		t.Fatalf("want 4 rows (2 grid + 2 worn), got %d: %+v", len(next.Rows), next.Rows)
	}
	wornBy := map[int]int{}
	for _, r := range next.Rows {
		if r.Wearer > 0 {
			wornBy[r.Wearer]++
		}
	}
	if wornBy[1001] != 1 || wornBy[1002] != 1 {
		t.Fatalf("worn rows wrong: %+v", next.Rows)
	}
}

func TestMigrateRejectsUnknownItem(t *testing.T) {
	payload := protocol.MigratePayload{
		Items:      []protocol.MigrateItem{{ItemID: 9999, Count: 1}},
		Characters: []protocol.MigrateChar{{CID: 1001}},
	}
	if _, err := Migrate(State{}, payload, testItems(), testLimits()); err == nil {
		t.Fatal("want error for unknown item")
	}
}

func TestMigrateRejectsGearSlotMismatch(t *testing.T) {
	payload := protocol.MigratePayload{
		Characters: []protocol.MigrateChar{{CID: 1001, WeaponItemID: 5201}}, // 防具塞进武器栏
	}
	if _, err := Migrate(State{}, payload, testItems(), testLimits()); err == nil {
		t.Fatal("want error for slot mismatch")
	}
}

func TestMigrateRejectsNonEmptyState(t *testing.T) {
	s := State{Rows: []Row{{UID: 1, ItemID: 5001, Count: 1}}}
	payload := protocol.MigratePayload{Characters: []protocol.MigrateChar{{CID: 1001}}}
	if _, err := Migrate(s, payload, testItems(), testLimits()); err == nil {
		t.Fatal("want error for non-empty state")
	}
}
