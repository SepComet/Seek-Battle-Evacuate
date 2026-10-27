package memorystore

import (
	"context"
	"errors"
	"reflect"
	"sync/atomic"
	"testing"

	"sbe-server/internal/core"
	"sbe-server/internal/protocol"
	"sbe-server/internal/store"
)

type testItems map[int]core.ItemDef

func (items testItems) Get(id int) (core.ItemDef, bool) { d, ok := items[id]; return d, ok }

func testLimits() core.Limits {
	return core.Limits{MaxPlayerPartySize: 4, BackpackSlotCount: 6, Difficulties: map[int]bool{1: true}}
}

func newTestStore() *Store {
	var seq atomic.Int64
	return New(func() (int64, error) { return seq.Add(1), nil }, 1000)
}

// 注册/登录：重名冲突、错密拒绝、uid 稳定。
func TestRegisterAndLogin(t *testing.T) {
	s := newTestStore()
	ctx := context.Background()

	uidA, token, err := s.Register(ctx, "player-a", "secret-123")
	if err != nil || uidA <= 0 || token == "" {
		t.Fatalf("register: uid=%d token=%q err=%v", uidA, token, err)
	}
	if _, _, err := s.Register(ctx, "player-a", "other-pass"); !errors.Is(err, store.ErrUsernameTaken) {
		t.Fatalf("duplicate username must be ErrUsernameTaken, got %v", err)
	}
	if _, _, err := s.Register(ctx, "x", "secret-123"); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("short username must be rejected, got %v", err)
	}
	if _, _, err := s.Register(ctx, "player-b", "12345"); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("short password must be rejected, got %v", err)
	}

	uidLogin, token2, err := s.Login(ctx, "  player-a  ", "secret-123")
	if err != nil || uidLogin != uidA || token2 == "" || token2 == token {
		t.Fatalf("login: uid=%d err=%v", uidLogin, err)
	}
	if _, _, err := s.Login(ctx, "player-a", "wrong-pass"); !errors.Is(err, store.ErrInvalidCredentials) {
		t.Fatalf("wrong password must be ErrInvalidCredentials, got %v", err)
	}
	if _, _, err := s.Login(ctx, "ghost-user", "secret-123"); !errors.Is(err, store.ErrInvalidCredentials) {
		t.Fatalf("unknown username must be ErrInvalidCredentials, got %v", err)
	}
	// 两个 token 均可认证回同一 uid
	for _, tk := range []string{token, token2} {
		uidAuth, err := s.Auth(ctx, tk)
		if err != nil || uidAuth != uidA {
			t.Fatalf("auth %q: uid=%d err=%v", tk, uidAuth, err)
		}
	}
}

// runId 归属语义：同用户重试回放首次快照；被其他用户占用的 runId 拒绝且不影响本人回放。
func TestSettleOwnershipAndReplay(t *testing.T) {
	s := newTestStore()
	ctx := context.Background()
	userA, _, err := s.Register(ctx, "device-a", "secret-123")
	if err != nil {
		t.Fatal(err)
	}
	userB, _, err := s.Register(ctx, "device-b", "secret-456")
	if err != nil {
		t.Fatal(err)
	}
	items := testItems{5001: {StackLimit: 99}}
	limits := testLimits()
	ops := []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: 5001, Count: 1, X: 0, Y: 0}}

	first, replay, err := s.ApplySettle(ctx, userA, "run-shared", core.SettleMeta{}, ops, items, limits)
	if err != nil || replay {
		t.Fatalf("first settle: replay=%v err=%v", replay, err)
	}
	again, replay, err := s.ApplySettle(ctx, userA, "run-shared", core.SettleMeta{}, ops, items, limits)
	if err != nil || !replay || !reflect.DeepEqual(first, again) {
		t.Fatalf("same-user replay: replay=%v err=%v", replay, err)
	}
	if _, _, err := s.ApplySettle(ctx, userB, "run-shared", core.SettleMeta{}, ops, items, limits); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("cross-user runId must be rejected, got %v", err)
	}
	if _, replay, err := s.ApplySettle(ctx, userA, "run-shared", core.SettleMeta{}, nil, items, limits); err != nil || !replay {
		t.Fatalf("replay after intrusion attempt: replay=%v err=%v", replay, err)
	}
	// 被占用不波及他人正常结算
	if _, replay, err := s.ApplySettle(ctx, userB, "run-own", core.SettleMeta{}, ops, items, limits); err != nil || replay {
		t.Fatalf("other user own settle: replay=%v err=%v", replay, err)
	}
}

// 协议 v5：注册即发放初始信用点，且迁移/结算/操作批次均不改变余额。
func TestRegisterGrantsInitialCreditsAndPersists(t *testing.T) {
	s := newTestStore()
	ctx := context.Background()
	uid, _, err := s.Register(ctx, "credit-player", "secret-123")
	if err != nil {
		t.Fatal(err)
	}

	state, err := s.Load(ctx, uid)
	if err != nil {
		t.Fatal(err)
	}
	if state.Credits != 1000 {
		t.Fatalf("fresh account credits = %d, want 1000", state.Credits)
	}

	// 迁移（客户端上传初始内容）不得覆盖余额
	if _, err := s.Migrate(ctx, uid, protocol.MigratePayload{
		Items:      []protocol.MigrateItem{{ItemID: 5001, Count: 1, X: 0, Y: 0}},
		Characters: []protocol.MigrateChar{{CID: 1001}},
	}, testItems{5001: {StackLimit: 99}}, testLimits()); err != nil {
		t.Fatal(err)
	}
	if state, err = s.Load(ctx, uid); err != nil {
		t.Fatal(err)
	}
	if state.Credits != 1000 {
		t.Fatalf("credits changed after migrate: %d, want 1000", state.Credits)
	}

	// 结算只动物品行，余额保持
	if _, _, err := s.ApplySettle(ctx, uid, "run-credits", core.SettleMeta{}, nil, testItems{5001: {StackLimit: 99}}, testLimits()); err != nil {
		t.Fatal(err)
	}
	ops := []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: 5001, Count: 1, X: 1, Y: 0}}
	if _, err := s.ApplyOps(ctx, uid, ops, testItems{5001: {StackLimit: 99}}, testLimits()); err != nil {
		t.Fatal(err)
	}
	if state, err = s.Load(ctx, uid); err != nil {
		t.Fatal(err)
	}
	if state.Credits != 1000 {
		t.Fatalf("credits changed after settle/ops: %d, want 1000", state.Credits)
	}
}
