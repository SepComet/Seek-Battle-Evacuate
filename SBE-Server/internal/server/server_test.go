package server

import (
	"encoding/json"
	"fmt"
	"net/http/httptest"
	"reflect"
	"testing"
	"time"

	"sbe-server/internal/core"
	"sbe-server/internal/protocol"
	"sbe-server/internal/ws"
)

type testItems map[int]core.ItemDef

func (t testItems) Get(id int) (core.ItemDef, bool) {
	d, ok := t[id]
	return d, ok
}

func items() testItems {
	return testItems{
		5001: {StackLimit: 99, EquipSlot: 0},
		5101: {StackLimit: 1, EquipSlot: 1},
		5102: {StackLimit: 1, EquipSlot: 1},
		5201: {StackLimit: 1, EquipSlot: 2},
		5301: {StackLimit: 5, EquipSlot: 0},
	}
}

func limits() core.Limits {
	return core.Limits{
		MaxPlayerPartySize: 4,
		BackpackSlotCount:  3,
		Difficulties:       map[int]bool{1: true, 2: true, 3: true},
		InitialCredits:     1000,
	}
}

type client struct {
	t    *testing.T
	conn *ws.Conn
	rid  int64
}

func dial(t *testing.T, addr string) *client {
	conn, err := ws.DialClient(addr, "/ws")
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	t.Cleanup(func() { _ = conn.Close() })
	return &client{t: t, conn: conn}
}

func (c *client) call(env protocol.Envelope) protocol.Envelope {
	c.t.Helper()
	c.rid++
	env.RID = c.rid
	env.V = protocol.Version
	raw, err := json.Marshal(env)
	if err != nil {
		c.t.Fatalf("marshal: %v", err)
	}
	if err := c.conn.WriteMessage(raw); err != nil {
		c.t.Fatalf("write: %v", err)
	}
	_ = c.conn.SetReadDeadline(time.Now().Add(3 * time.Second))
	_, respRaw, err := c.conn.ReadMessage()
	if err != nil {
		c.t.Fatalf("read: %v", err)
	}
	var resp protocol.Envelope
	if err := json.Unmarshal(respRaw, &resp); err != nil {
		c.t.Fatalf("unmarshal %s: %v", respRaw, err)
	}
	if resp.RID != env.RID {
		c.t.Fatalf("rid mismatch: sent %d got %d", env.RID, resp.RID)
	}
	return resp
}

func mustOK(c *client, env protocol.Envelope) *protocol.Snapshot {
	c.t.Helper()
	resp := c.call(env)
	if resp.T != protocol.MsgOK {
		c.t.Fatalf("want ok, got %s (%s: %s)", resp.T, resp.Code, resp.Msg)
	}
	if resp.Snapshot == nil {
		c.t.Fatalf("ok response missing snapshot")
	}
	return resp.Snapshot
}

func uidOfWorn(snap *protocol.Snapshot, cid int) int64 {
	for _, r := range snap.Items {
		if r.Wearer == cid {
			return r.UID
		}
	}
	return 0
}

func uidOfGridItem(snap *protocol.Snapshot, itemID int) int64 {
	for _, r := range snap.Items {
		if r.ItemID == itemID && r.Wearer == 0 {
			return r.UID
		}
	}
	return 0
}

func wornItemID(snap *protocol.Snapshot, cid int) int {
	for _, r := range snap.Items {
		if r.Wearer == cid {
			return r.ItemID
		}
	}
	return 0
}

func TestFullFlowRegisterMigrateSettleIdempotentEquip(t *testing.T) {
	srv := New(items(), limits())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()
	addr := ts.Listener.Addr().String()

	// 1. 注册：新账号得到 token 与空快照
	c := dial(t, addr)
	regResp := c.call(protocol.Envelope{T: protocol.MsgRegister, Username: "player-1", Password: "secret-1"})
	if regResp.T != protocol.MsgOK || regResp.Token == "" {
		t.Fatalf("register failed: %+v", regResp)
	}
	token := regResp.Token
	reg := regResp.Snapshot
	if reg.Initialized || len(reg.Items) != 0 {
		t.Fatalf("fresh account not empty: %+v", reg)
	}
	// 协议 v5：注册快照携带按 initialCredits 发放的余额
	if reg.Credits != 1000 {
		t.Fatalf("fresh account credits = %d, want 1000", reg.Credits)
	}

	// 2. 首登迁移：上传本地存档（1001 穿着武器 5101）
	mig := mustOK(c, protocol.Envelope{T: protocol.MsgMigrate, Migrate: &protocol.MigratePayload{
		Items: []protocol.MigrateItem{{ItemID: 5001, Count: 20, X: 0, Y: 0}},
		Characters: []protocol.MigrateChar{
			{CID: 1001, WeaponItemID: 5101},
			{CID: 1002},
		},
	}})
	if !mig.Initialized || len(mig.Items) != 2 || len(mig.Characters) != 2 {
		t.Fatalf("migrate snapshot wrong: %+v", mig)
	}
	if mig.Credits != 1000 {
		t.Fatalf("migrate must not touch credits: %d, want 1000", mig.Credits)
	}

	// 3. 结算：招募 1003、战利品入库、旧武器销毁、捡到的新武器穿上
	settleOps := []protocol.Op{
		{K: protocol.OpRecruit, CID: 1003},
		{K: protocol.OpAdd, TID: 7, ItemID: 5301, Count: 4, X: 1, Y: 0},
		{K: protocol.OpAdd, TID: 8, ItemID: 5102, Count: 1},
		{K: protocol.OpDel, UID: uidOfWorn(mig, 1001)},
		{K: protocol.OpWear, CID: 1001, Slot: 1, TID: 8},
	}
	first := mustOK(c, protocol.Envelope{T: protocol.MsgSettle, RunID: "run-1", Outcome: 1, Ops: settleOps})
	if len(first.Items) != 3 || len(first.Characters) != 3 {
		t.Fatalf("settle snapshot wrong: %+v", first)
	}
	if wornItemID(first, 1001) != 5102 {
		t.Fatalf("new weapon not worn after settle: %+v", first.Items)
	}

	// 4. 同 runId 重发：返回与首次完全一致的快照（幂等）
	again := mustOK(c, protocol.Envelope{T: protocol.MsgSettle, RunID: "run-1", Outcome: 1, Ops: settleOps})
	if !reflect.DeepEqual(first, again) {
		t.Fatalf("idempotent settle returned different snapshot:\nfirst: %+v\nagain: %+v", first, again)
	}

	// 5. 大厅操作：卸下武器回网格，给 1002 穿上新购防具
	lobby := mustOK(c, protocol.Envelope{T: protocol.MsgOps, Ops: []protocol.Op{
		{K: protocol.OpRel, UID: uidOfWorn(first, 1001), X: 3, Y: 3},
		{K: protocol.OpAdd, TID: 9, ItemID: 5201, Count: 1},
		{K: protocol.OpWear, CID: 1002, Slot: 2, TID: 9},
	}})
	if wornItemID(lobby, 1001) != 0 {
		t.Fatalf("weapon not released: %+v", lobby.Items)
	}
	if wornItemID(lobby, 1002) != 5201 {
		t.Fatalf("armor not worn: %+v", lobby.Items)
	}

	// 6. 新连接凭 token 认证拉取，与最后状态一致
	c2 := dial(t, addr)
	pulled := mustOK(c2, protocol.Envelope{T: protocol.MsgAuth, Token: token})
	if !reflect.DeepEqual(pulled, lobby) {
		t.Fatalf("auth pull snapshot differs from last ops snapshot:\n%+v\n%+v", pulled, lobby)
	}
}

func TestUnauthorizedPullRejected(t *testing.T) {
	srv := New(items(), limits())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()

	c := dial(t, ts.Listener.Addr().String())
	resp := c.call(protocol.Envelope{T: protocol.MsgPull})
	if resp.T != protocol.MsgErr || resp.Code != protocol.ErrCodeUnauthorized {
		t.Fatalf("want unauthorized err, got %+v", resp)
	}
}

func TestSettleRejectsUnknownItemAtomically(t *testing.T) {
	srv := New(items(), limits())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()

	c := dial(t, ts.Listener.Addr().String())
	mustOK(c, protocol.Envelope{T: protocol.MsgRegister, Username: "player-reject", Password: "secret-2"})
	mustOK(c, protocol.Envelope{T: protocol.MsgMigrate, Migrate: &protocol.MigratePayload{
		Characters: []protocol.MigrateChar{{CID: 1001}},
	}})
	bad := c.call(protocol.Envelope{T: protocol.MsgSettle, RunID: "run-x", Ops: []protocol.Op{
		{K: protocol.OpAdd, TID: 1, ItemID: 5001, Count: 1},
		{K: protocol.OpAdd, TID: 2, ItemID: 9999, Count: 1}, // 未知物品
	}})
	if bad.T != protocol.MsgErr || bad.Code != protocol.ErrCodeRejected {
		t.Fatalf("want rejected, got %+v", bad)
	}
	after := mustOK(c, protocol.Envelope{T: protocol.MsgPull})
	if len(after.Items) != 0 {
		t.Fatalf("rejected batch must not apply partially: %+v", after.Items)
	}
	// 被拒的 runId 未入账，修正后可重发成功
	good := mustOK(c, protocol.Envelope{T: protocol.MsgSettle, RunID: "run-x", Ops: []protocol.Op{
		{K: protocol.OpAdd, TID: 1, ItemID: 5001, Count: 1},
	}})
	if len(good.Items) != 1 {
		t.Fatalf("retry after rejection failed: %+v", good)
	}
}

func TestRidRetryReturnsCachedResponse(t *testing.T) {
	srv := New(items(), limits())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()

	c := dial(t, ts.Listener.Addr().String())
	mustOK(c, protocol.Envelope{T: protocol.MsgRegister, Username: "player-rid", Password: "secret-3"})

	ops := []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: 5001, Count: 3, X: 0, Y: 0}}
	first := mustOK(c, protocol.Envelope{T: protocol.MsgOps, Ops: ops})
	// 相同 rid 重发（不递增）：返回缓存应答，不重复入账
	c.rid--
	second := mustOK(c, protocol.Envelope{T: protocol.MsgOps, Ops: ops})
	if !reflect.DeepEqual(first, second) {
		t.Fatal("rid retry must return cached response")
	}
	after := mustOK(c, protocol.Envelope{T: protocol.MsgPull})
	if len(after.Items) != 1 || after.Items[0].Count != 3 {
		t.Fatalf("rid retry duplicated add: %+v", after.Items)
	}
}

func TestLoadoutSyncSettleHistory(t *testing.T) {
	srv := New(items(), limits())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()
	addr := ts.Listener.Addr().String()

	// 1. 注册 + 迁移：携带完整战备配置
	c := dial(t, addr)
	reg := c.call(protocol.Envelope{T: protocol.MsgRegister, Username: "loadout-player", Password: "secret-4"})
	if reg.T != protocol.MsgOK || reg.Token == "" {
		t.Fatalf("register failed: %+v", reg)
	}
	mig := mustOK(c, protocol.Envelope{T: protocol.MsgMigrate, Migrate: &protocol.MigratePayload{
		Characters: []protocol.MigrateChar{{CID: 1001}, {CID: 1002}},
		Party:      []int{1001, 1002},
		Difficulty: 2,
		Carried:    []protocol.Stack{{ItemID: 5001, Count: 5}},
	}})
	if !reflect.DeepEqual(mig.Party, []int{1001, 1002}) || mig.Difficulty != 2 || len(mig.Carried) != 1 {
		t.Fatalf("migrate loadout wrong: party=%+v diff=%d carried=%+v", mig.Party, mig.Difficulty, mig.Carried)
	}

	// 2. 大厅操作：调换出战顺序 + 改难度；携带计划不受影响
	lobby := mustOK(c, protocol.Envelope{T: protocol.MsgOps, Ops: []protocol.Op{
		{K: protocol.OpParty, Party: []int{1002, 1001}},
		{K: protocol.OpDiff, Difficulty: 3},
	}})
	if !reflect.DeepEqual(lobby.Party, []int{1002, 1001}) || lobby.Difficulty != 3 {
		t.Fatalf("ops loadout wrong: party=%+v diff=%d", lobby.Party, lobby.Difficulty)
	}
	if len(lobby.Carried) != 1 {
		t.Fatalf("carry lost: %+v", lobby.Carried)
	}

	// 3. 非法战备操作整批拒绝，不落半截状态
	bad := c.call(protocol.Envelope{T: protocol.MsgOps, Ops: []protocol.Op{
		{K: protocol.OpParty, Party: []int{1002, 1001, 1001}}, // 重复 cid
	}})
	if bad.T != protocol.MsgErr || bad.Code != protocol.ErrCodeRejected {
		t.Fatalf("want rejected, got %+v", bad)
	}

	// 4. 两局结算带战绩元数据
	mustOK(c, protocol.Envelope{T: protocol.MsgSettle, RunID: "run-a", Outcome: 1, Difficulty: 3, Seed: 111, StartedAt: 1000, EndedAt: 2000, Ops: []protocol.Op{
		{K: protocol.OpAdd, TID: 1, ItemID: 5001, Count: 1, X: 0, Y: 0},
	}})
	mustOK(c, protocol.Envelope{T: protocol.MsgSettle, RunID: "run-b", Outcome: 2, Difficulty: 3, Seed: 222, StartedAt: 3000, EndedAt: 4000})

	// 5. 战绩按 startedAt 倒序返回，元数据完整
	hist := c.call(protocol.Envelope{T: protocol.MsgHistory})
	if hist.T != protocol.MsgOK || len(hist.History) != 2 {
		t.Fatalf("history wrong: %+v", hist)
	}
	if hist.History[0].RunID != "run-b" || hist.History[1].RunID != "run-a" {
		t.Fatalf("history order wrong: %+v", hist.History)
	}
	firstRun := hist.History[1]
	if firstRun.Outcome != 1 || firstRun.Difficulty != 3 || firstRun.Seed != 111 || firstRun.StartedAt != 1000 || firstRun.EndedAt != 2000 {
		t.Fatalf("history meta wrong: %+v", firstRun)
	}

	// 6. 幂等重放不产生重复战绩
	mustOK(c, protocol.Envelope{T: protocol.MsgSettle, RunID: "run-a", Outcome: 4, Difficulty: 1, Seed: 999, StartedAt: 9000, EndedAt: 10000})
	hist = c.call(protocol.Envelope{T: protocol.MsgHistory})
	if len(hist.History) != 2 || hist.History[1] != firstRun {
		t.Fatalf("idempotent settle changed history: %+v", hist.History)
	}

	// 7. 断线重连：auth 拉取的快照保持战备配置
	_ = c.conn.Close()
	c2 := dial(t, addr)
	pulled := mustOK(c2, protocol.Envelope{T: protocol.MsgAuth, Token: reg.Token})
	if !reflect.DeepEqual(pulled.Party, []int{1002, 1001}) || pulled.Difficulty != 3 || len(pulled.Carried) != 1 {
		t.Fatalf("loadout not persisted across reconnect: party=%+v diff=%d carried=%+v", pulled.Party, pulled.Difficulty, pulled.Carried)
	}
	if after := mustOK(c2, protocol.Envelope{T: protocol.MsgPull}); !reflect.DeepEqual(after, pulled) {
		t.Fatalf("pull differs from reconnect snapshot: %+v", after)
	}
}

func TestHistoryReturnsLatest100ForCurrentUser(t *testing.T) {
	srv := New(items(), limits())
	sess := &session{}
	reg := srv.dispatch(sess, protocol.Envelope{T: protocol.MsgRegister, Username: "history-player", Password: "secret-5"})
	if reg.T != protocol.MsgOK {
		t.Fatalf("register: %+v", reg)
	}
	for i := 0; i < 101; i++ {
		resp := srv.dispatch(sess, protocol.Envelope{T: protocol.MsgSettle, RunID: fmt.Sprintf("history-%03d", i), StartedAt: int64(i), Outcome: 1})
		if resp.T != protocol.MsgOK {
			t.Fatalf("settle %d: %+v", i, resp)
		}
	}
	hist := srv.dispatch(sess, protocol.Envelope{T: protocol.MsgHistory})
	if hist.T != protocol.MsgOK || len(hist.History) != 100 || hist.History[0].RunID != "history-100" || hist.History[99].RunID != "history-001" {
		t.Fatalf("history cap/order: %+v", hist)
	}
	other := &session{}
	srv.dispatch(other, protocol.Envelope{T: protocol.MsgRegister, Username: "history-other", Password: "secret-6"})
	if hist := srv.dispatch(other, protocol.Envelope{T: protocol.MsgHistory}); hist.T != protocol.MsgOK || len(hist.History) != 0 {
		t.Fatalf("other user received history: %+v", hist)
	}
}

func TestLegacyVersionRejected(t *testing.T) {
	srv := New(items(), limits())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()
	c := dial(t, ts.Listener.Addr().String())
	// v2 客户端对 v3 服务器：直接 bad_message 断代
	if err := c.conn.WriteMessage([]byte(`{"t":"register","v":2,"rid":1,"device":"legacy"}`)); err != nil {
		t.Fatal(err)
	}
	_ = c.conn.SetReadDeadline(time.Now().Add(3 * time.Second))
	_, raw, err := c.conn.ReadMessage()
	if err != nil {
		t.Fatal(err)
	}
	var resp protocol.Envelope
	if err := json.Unmarshal(raw, &resp); err != nil {
		t.Fatal(err)
	}
	if resp.T != protocol.MsgErr || resp.Code != protocol.ErrCodeBadMessage || resp.V != protocol.Version {
		t.Fatalf("legacy protocol accepted: %+v", resp)
	}
}

func TestUnauthorizedHistoryRejected(t *testing.T) {
	srv := New(items(), limits())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()

	c := dial(t, ts.Listener.Addr().String())
	resp := c.call(protocol.Envelope{T: protocol.MsgHistory})
	if resp.T != protocol.MsgErr || resp.Code != protocol.ErrCodeUnauthorized {
		t.Fatalf("want unauthorized err, got %+v", resp)
	}
}
