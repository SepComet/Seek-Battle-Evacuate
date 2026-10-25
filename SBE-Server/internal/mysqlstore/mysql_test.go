package mysqlstore

import (
	"context"
	"errors"
	"fmt"
	"os"
	"reflect"
	"testing"
	"time"

	"sbe-server/internal/core"
	"sbe-server/internal/protocol"
	"sbe-server/internal/snowflake"
	"sbe-server/internal/store"
)

type testItems map[int]core.ItemDef

func (items testItems) Get(id int) (core.ItemDef, bool) { d, ok := items[id]; return d, ok }

// Requires an explicitly supplied test database with migrations 0001~0004 applied.
// Only rows belonging to this test's new accounts are removed by cleanup.
func TestMySQLLoadoutSettlementHistory(t *testing.T) {
	dsn := os.Getenv("SBE_TEST_MYSQL_DSN")
	if dsn == "" {
		t.Skip("SBE_TEST_MYSQL_DSN not set; run tools/mysql_integration_test.(bat|sh) or see README")
	}
	uidGen, err := snowflake.New(0)
	if err != nil {
		t.Fatal(err)
	}
	s, err := Open(dsn, uidGen.Next)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = s.Close() })
	ctx := context.Background()
	suffix := fmt.Sprintf("v2-test-%d", time.Now().UnixNano())
	userID, _, err := s.Register(ctx, suffix, "secret-123")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		for _, table := range []string{"settlements", "user_loadout", "warehouse_items", "user_characters", "auth_tokens"} {
			if _, err := s.db.ExecContext(ctx, "DELETE FROM "+table+" WHERE user_id = ?", userID); err != nil {
				t.Errorf("cleanup %s: %v", table, err)
			}
		}
		if _, err := s.db.ExecContext(ctx, "DELETE FROM users WHERE uid = ?", userID); err != nil {
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

// 账号注册/登录与 runId 归属、uid 按用户铸号：
// 重名拒绝、错密拒绝、登录 uid 稳定；同用户重试回放；他人占用同 runId 拒绝；
// 两个用户各自铸出的相同 uid 必须能在 0003 复合主键下共存。
func TestMySQLSettleOwnershipAndPerUserUID(t *testing.T) {
	dsn := os.Getenv("SBE_TEST_MYSQL_DSN")
	if dsn == "" {
		t.Skip("SBE_TEST_MYSQL_DSN not set; run tools/mysql_integration_test.(bat|sh) or see README")
	}
	uidGen, err := snowflake.New(0)
	if err != nil {
		t.Fatal(err)
	}
	s, err := Open(dsn, uidGen.Next)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = s.Close() })
	ctx := context.Background()
	suffix := fmt.Sprintf("own-test-%d", time.Now().UnixNano())
	userA, _, err := s.Register(ctx, suffix+"-a", "secret-a1")
	if err != nil {
		t.Fatal(err)
	}
	if _, _, err := s.Register(ctx, suffix+"-a", "secret-a2"); !errors.Is(err, store.ErrUsernameTaken) {
		t.Fatalf("duplicate username must be ErrUsernameTaken, got %v", err)
	}
	userB, _, err := s.Register(ctx, suffix+"-b", "secret-b1")
	if err != nil {
		t.Fatal(err)
	}
	if uidA, _, err := s.Login(ctx, suffix+"-a", "secret-a1"); err != nil || uidA != userA {
		t.Fatalf("login uid mismatch: uid=%d want=%d err=%v", uidA, userA, err)
	}
	if _, _, err := s.Login(ctx, suffix+"-a", "wrong-pass"); !errors.Is(err, store.ErrInvalidCredentials) {
		t.Fatalf("wrong password must be ErrInvalidCredentials, got %v", err)
	}
	t.Cleanup(func() {
		for _, uid := range []int64{userA, userB} {
			for _, table := range []string{"settlements", "user_loadout", "warehouse_items", "user_characters", "auth_tokens"} {
				if _, err := s.db.ExecContext(ctx, "DELETE FROM "+table+" WHERE user_id = ?", uid); err != nil {
					t.Errorf("cleanup %s (user %d): %v", table, uid, err)
				}
			}
			if _, err := s.db.ExecContext(ctx, "DELETE FROM users WHERE uid = ?", uid); err != nil {
				t.Errorf("cleanup user %d: %v", uid, err)
			}
		}
	})

	items := testItems{5001: {StackLimit: 99}}
	limits := core.Limits{MaxPlayerPartySize: 4, BackpackSlotCount: 6, Difficulties: map[int]bool{1: true}}
	ops := []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: 5001, Count: 1, X: 0, Y: 0}}
	runID := suffix + "-run"

	if _, replay, err := s.ApplySettle(ctx, userA, runID, core.SettleMeta{}, ops, items, limits); err != nil || replay {
		t.Fatalf("userA settle: replay=%v err=%v", replay, err)
	}
	if _, replay, err := s.ApplySettle(ctx, userA, runID, core.SettleMeta{}, ops, items, limits); err != nil || !replay {
		t.Fatalf("userA replay: replay=%v err=%v", replay, err)
	}
	if _, _, err := s.ApplySettle(ctx, userB, runID, core.SettleMeta{}, ops, items, limits); err == nil {
		t.Fatal("cross-user runId must be rejected")
	} else if !errors.Is(err, core.ErrRejected) {
		t.Fatalf("cross-user runId err = %v, want ErrRejected", err)
	}
	// 跨用户同 uid 共存（旧全局主键下此处 1062 回滚）
	if _, replay, err := s.ApplySettle(ctx, userB, suffix+"-own", core.SettleMeta{}, ops, items, limits); err != nil || replay {
		t.Fatalf("userB own settle with per-user uid: replay=%v err=%v", replay, err)
	}
	stateA, err := s.Load(ctx, userA)
	if err != nil {
		t.Fatal(err)
	}
	stateB, err := s.Load(ctx, userB)
	if err != nil {
		t.Fatal(err)
	}
	if len(stateA.Rows) != 1 || len(stateB.Rows) != 1 || stateA.Rows[0].UID != stateB.Rows[0].UID {
		t.Fatalf("per-user uid coexistence broken: A=%+v B=%+v", stateA.Rows, stateB.Rows)
	}
}
