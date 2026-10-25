package server

import (
	"encoding/json"
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
	srv := New(items())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()
	addr := ts.Listener.Addr().String()

	// 1. 注册：新设备得到 token 与空快照
	c := dial(t, addr)
	regResp := c.call(protocol.Envelope{T: protocol.MsgRegister, Device: "device-uuid-1"})
	if regResp.T != protocol.MsgOK || regResp.Token == "" {
		t.Fatalf("register failed: %+v", regResp)
	}
	token := regResp.Token
	reg := regResp.Snapshot
	if reg.Initialized || len(reg.Items) != 0 {
		t.Fatalf("fresh account not empty: %+v", reg)
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
	srv := New(items())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()

	c := dial(t, ts.Listener.Addr().String())
	resp := c.call(protocol.Envelope{T: protocol.MsgPull})
	if resp.T != protocol.MsgErr || resp.Code != protocol.ErrCodeUnauthorized {
		t.Fatalf("want unauthorized err, got %+v", resp)
	}
}

func TestSettleRejectsUnknownItemAtomically(t *testing.T) {
	srv := New(items())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()

	c := dial(t, ts.Listener.Addr().String())
	mustOK(c, protocol.Envelope{T: protocol.MsgRegister, Device: "d2"})
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
	srv := New(items())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()

	c := dial(t, ts.Listener.Addr().String())
	mustOK(c, protocol.Envelope{T: protocol.MsgRegister, Device: "d3"})

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
