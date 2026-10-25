package mysqlstore

import (
	"context"
	"fmt"
	"os"
	"reflect"
	"testing"
	"time"

	"sbe-server/internal/core"
	"sbe-server/internal/protocol"
)

type testItems map[int]core.ItemDef

func (items testItems) Get(id int) (core.ItemDef, bool) { d, ok := items[id]; return d, ok }

// Requires an explicitly supplied test database with migrations 0001 and 0002 applied.
// Only rows belonging to this test's new account are removed by cleanup.
func TestMySQLLoadoutSettlementHistory(t *testing.T) {
	dsn := os.Getenv("SBE_TEST_MYSQL_DSN")
	if dsn == "" {
		t.Skip("SBE_TEST_MYSQL_DSN not set; real MySQL integration requires a migrated test database")
	}
	s, err := Open(dsn)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = s.Close() })
	ctx := context.Background()
	suffix := fmt.Sprintf("v2-test-%d", time.Now().UnixNano())
	userID, _, err := s.Register(ctx, suffix)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		for _, table := range []string{"settlements", "user_loadout", "warehouse_items", "user_characters", "auth_tokens"} {
			if _, err := s.db.ExecContext(ctx, "DELETE FROM "+table+" WHERE user_id = ?", userID); err != nil {
				t.Errorf("cleanup %s: %v", table, err)
			}
		}
		if _, err := s.db.ExecContext(ctx, "DELETE FROM users WHERE id = ?", userID); err != nil {
			t.Errorf("cleanup user: %v", err)
		}
	})
	items := testItems{5001: {StackLimit: 99}}
	limits := core.Limits{MaxPlayerPartySize: 4, BackpackSlotCount: 30, Difficulties: map[int]bool{1: true, 2: true}}
	migrated, err := s.Migrate(ctx, userID, protocol.MigratePayload{
		Characters: []protocol.MigrateChar{{CID: 1001}, {CID: 1002}}, Party: []int{1002, 1001}, Difficulty: 2,
		Carried: []protocol.Stack{{ItemID: 5001, Count: 7}},
	}, items, limits)
	if err != nil {
		t.Fatal(err)
	}
	loaded, err := s.Load(ctx, userID)
	if err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(migrated.ToSnapshot(), loaded.ToSnapshot()) {
		t.Fatalf("loadout did not round-trip: %+v", loaded)
	}
	meta := core.SettleMeta{Outcome: 1, Difficulty: 2, Seed: 9223372036854775800, StartedAt: 1234567890123, EndedAt: 1234567894567}
	_, replay, err := s.ApplySettle(ctx, userID, suffix, meta, nil, items, limits)
	if err != nil || replay {
		t.Fatalf("settle: replay=%v err=%v", replay, err)
	}
	_, replay, err = s.ApplySettle(ctx, userID, suffix, core.SettleMeta{Outcome: 4}, nil, items, limits)
	if err != nil || !replay {
		t.Fatalf("settle replay: replay=%v err=%v", replay, err)
	}
	history, err := s.History(ctx, userID, 100)
	if err != nil {
		t.Fatal(err)
	}
	want := []protocol.SnapRun{{RunID: suffix, Outcome: meta.Outcome, Difficulty: meta.Difficulty, Seed: meta.Seed, StartedAt: meta.StartedAt, EndedAt: meta.EndedAt}}
	if !reflect.DeepEqual(history, want) {
		t.Fatalf("history metadata/replay mismatch: %+v", history)
	}
	_, err = s.ApplyOps(ctx, userID, []protocol.Op{{K: protocol.OpParty}, {K: protocol.OpDiff}, {K: protocol.OpCarry}}, items, limits)
	if err != nil {
		t.Fatal(err)
	}
	loaded, err = s.Load(ctx, userID)
	if err != nil || len(loaded.Party) != 0 || loaded.Difficulty != 0 || len(loaded.Carried) != 0 {
		t.Fatalf("cleared loadout not persisted: %+v, %v", loaded, err)
	}
}
