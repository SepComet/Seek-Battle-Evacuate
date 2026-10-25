package server

import (
	"net/http/httptest"
	"reflect"
	"strings"
	"testing"
	"time"

	"sbe-server/internal/gametable"
	"sbe-server/internal/itemtable"
	"sbe-server/internal/mapdef"
	"sbe-server/internal/mapgen"
	"sbe-server/internal/protocol"
	"sbe-server/internal/run"
)

// 本文件覆盖探索内容上云（协议 v4）的外部行为：start_run / encounter / loot / settle 对账，
// 以及活跃 run 的 TTL 清理。全部走 fake WS 客户端（与 server_test.go 同一套 dial/call），
// 断言只看线上往返，不看 run 包内部结构。
//
// 生成内容不硬编码：测试先挑一个「既有存在敌点、又有 None 敌点」的种子，再用
// mapgen.Generate 自己算出期望值，服务器侧用同一个种子（run.Config.Seed 注入）必须给出同一份内容。

// exploration 一套内联探索配置 + 固定种子下生成的内容。
type exploration struct {
	layout  *mapdef.Layout
	tables  *gametable.Tables
	seed    int64
	content *mapgen.Content
}

// newExploration 装配内联布局与三张生成表，并找到一个满足测试需要的固定种子。
// 内联数据与 config/ 下的真实资产无关（真实配置的一致性见 mapgen 的真实配置冒烟）。
func newExploration(t *testing.T) *exploration {
	t.Helper()

	catalog, err := itemtable.NewCatalog([]itemtable.CatalogEntry{
		{ID: 5001, Rarity: 1, StackLimit: 99},
		{ID: 5101, Rarity: 1, StackLimit: 1, EquipSlot: 1},
		{ID: 5102, Rarity: 2, StackLimit: 1, EquipSlot: 1},
		{ID: 5201, Rarity: 3, StackLimit: 1, EquipSlot: 2},
		{ID: 5301, Rarity: 4, StackLimit: 5},
		{ID: 5401, Rarity: 5, StackLimit: 1},
	})
	if err != nil {
		t.Fatalf("NewCatalog: %v", err)
	}

	layout := &mapdef.Layout{
		Rooms: []mapdef.Room{{Type: 0, MinX: -100, MinY: -100, MaxX: 100, MaxY: 100}},
		ResourcePoints: []mapdef.ResourcePoint{
			{X: -4, Y: 5, ResourcePointID: 1},
			{X: 2, Y: 7, ResourcePointID: 2},
			{X: 6, Y: -3, ResourcePointID: 1},
		},
		EnemySpawnPoints:  []mapdef.Vec2{{X: -19, Y: 1}, {X: -14, Y: 4}, {X: 3, Y: 9}, {X: 12, Y: -2}},
		PlayerSpawnPoints: []mapdef.Vec2{{X: 1, Y: -16}, {X: 5, Y: -16}},
		ExtractionPoints:  []mapdef.Vec2{{X: 30, Y: -3}, {X: 28, Y: -8}},
	}

	resources := []gametable.ResourcePoint{{ID: 1}, {ID: 2}}
	for i := range resources {
		for difficulty := 1; difficulty <= 3; difficulty++ {
			resources[i].LootConfigs = append(resources[i].LootConfigs, gametable.LootConfig{
				Difficulty: difficulty, MinCount: 1, MaxCount: 1,
				WhiteWeight: 50, GreenWeight: 20, BlueWeight: 15, GoldWeight: 10, RedWeight: 5,
			})
		}
	}
	parties := []gametable.EnemyParty{
		{ID: 4001, Threat: 1, EnemyIDs: []int{3001}},
		{ID: 4101, Threat: 2, EnemyIDs: []int{3001}},
		{ID: 4201, Threat: 3, EnemyIDs: []int{3001}},
	}
	weights := []gametable.ThreatWeight{{Threat: 0, Weight: 3}, {Threat: 1, Weight: 3}, {Threat: 2, Weight: 2}, {Threat: 3, Weight: 1}}
	difficulties := []gametable.Difficulty{
		{Tier: 1, Weights: weights},
		{Tier: 2, Weights: weights},
		{Tier: 3, Weights: weights},
	}

	tables := &gametable.Tables{
		Resources:    gametable.NewResourceTable(resources),
		Parties:      gametable.NewEnemyPartyTable(parties),
		Difficulties: gametable.NewDifficultyTable(difficulties),
		Items:        catalog,
	}

	for seed := int64(1); seed <= 1000; seed++ {
		content, err := mapgen.Generate(seed, 1, layout, tables)
		if err != nil {
			t.Fatalf("Generate(seed=%d): %v", seed, err)
		}
		present, absent := 0, 0
		for _, point := range content.EnemyPoints {
			if point.Present {
				present++
			} else {
				absent++
			}
		}
		// encounter 的两条分支（存在 / None）都要可测。
		if present > 0 && absent > 0 {
			return &exploration{layout: layout, tables: tables, seed: seed, content: content}
		}
	}
	t.Fatal("no seed yields both present and absent enemy points")
	return nil
}

// firstPresentEnemy 本局第一个存在的敌点。
func (e *exploration) firstPresentEnemy(t *testing.T) mapgen.EnemyPointContent {
	t.Helper()
	for _, point := range e.content.EnemyPoints {
		if point.Present {
			return point
		}
	}
	t.Fatal("generated content has no present enemy point")
	return mapgen.EnemyPointContent{}
}

// firstAbsentEnemy 本局第一个被掷为 None 的敌点。
func (e *exploration) firstAbsentEnemy(t *testing.T) mapgen.EnemyPointContent {
	t.Helper()
	for _, point := range e.content.EnemyPoints {
		if !point.Present {
			return point
		}
	}
	t.Fatal("generated content has no absent enemy point")
	return mapgen.EnemyPointContent{}
}

// runManager 以固定种子构造 run 管理器（服务器分配种子的随机路径由 run 包自测覆盖）。
func (e *exploration) runManager() *run.Manager {
	return run.NewManager(run.Config{
		Layout: e.layout,
		Tables: e.tables,
		Seed:   func() (int64, error) { return e.seed, nil },
	})
}

// newRunServer 起一个带探索 run 管理器的内存存储服务器（Run 管理器与 cmd/sbe-server 同一落点）。
func newRunServer(t *testing.T, exp *exploration) (*Server, string) {
	t.Helper()
	srv := New(items(), limits())
	srv.Run = exp.runManager()
	ts := httptest.NewServer(srv.Handler("/ws"))
	t.Cleanup(ts.Close)
	return srv, ts.Listener.Addr().String()
}

// registerPlayer 注册一个新账号（成功即已登录）。
func registerPlayer(t *testing.T, c *client, name string) protocol.Envelope {
	t.Helper()
	resp := c.call(protocol.Envelope{T: protocol.MsgRegister, Username: name, Password: "secret-run"})
	if resp.T != protocol.MsgOK {
		t.Fatalf("register %s: %+v", name, resp)
	}
	return resp
}

// mustStartRun 发 start_run 并断言成功。
func mustStartRun(t *testing.T, c *client, difficulty int, resume bool) protocol.Envelope {
	t.Helper()
	resp := c.call(protocol.Envelope{T: protocol.MsgStartRun, Difficulty: difficulty, Resume: resume})
	if resp.T != protocol.MsgOK {
		t.Fatalf("start_run(difficulty=%d, resume=%t): %+v", difficulty, resume, resp)
	}
	if resp.RunID == "" {
		t.Fatalf("start_run returned empty runId: %+v", resp)
	}
	return resp
}

// mustReject 断言请求被 rejected（不新增错误码，契约 §7 错误码）。
func mustReject(t *testing.T, c *client, env protocol.Envelope, msgPart string) protocol.Envelope {
	t.Helper()
	resp := c.call(env)
	if resp.T != protocol.MsgErr || resp.Code != protocol.ErrCodeRejected {
		t.Fatalf("%s must be rejected, got %+v", env.T, resp)
	}
	if msgPart != "" && !strings.Contains(resp.Msg, msgPart) {
		t.Fatalf("%s rejected with msg %q, want it to contain %q", env.T, resp.Msg, msgPart)
	}
	return resp
}

func vec2(x, y float64) protocol.Vec2 { return protocol.Vec2{X: x, Y: y} }

// TestStartRunIsIdempotentAndReflectsLoot 已有活跃 run 时再次 start_run 返回现状；
// 开箱后 consumed/looted 随现状返回（这是断线重连的恢复路径）。
func TestStartRunIsIdempotentAndReflectsLoot(t *testing.T) {
	exp := newExploration(t)
	_, addr := newRunServer(t, exp)
	c := dial(t, addr)
	registerPlayer(t, c, "run-idempotent")

	first := mustStartRun(t, c, 1, false)
	if first.Seed != exp.seed {
		t.Fatalf("seed = %d, want the allocated %d", first.Seed, exp.seed)
	}
	if first.SpawnPoint == nil || *first.SpawnPoint != vec2(exp.content.PlayerSpawnPoint.X, exp.content.PlayerSpawnPoint.Y) {
		t.Fatalf("spawnPoint = %+v, want %+v", first.SpawnPoint, exp.content.PlayerSpawnPoint)
	}
	if first.ExtractionPoint == nil || *first.ExtractionPoint != vec2(exp.content.ExtractionPoint.X, exp.content.ExtractionPoint.Y) {
		t.Fatalf("extractionPoint = %+v, want %+v", first.ExtractionPoint, exp.content.ExtractionPoint)
	}
	if len(first.Consumed) != 0 || len(first.Looted) != 0 {
		t.Fatalf("fresh run reports consumed/looted: %v / %v", first.Consumed, first.Looted)
	}

	// 第二次（resume=false、难度也不同）仍返回同一局，不新建。
	second := mustStartRun(t, c, 2, false)
	if second.RunID != first.RunID || second.Seed != first.Seed ||
		*second.SpawnPoint != *first.SpawnPoint || *second.ExtractionPoint != *first.ExtractionPoint {
		t.Fatalf("start_run is not idempotent:\nfirst=%+v\nsecond=%+v", first, second)
	}

	// 开箱后现状包含消耗记录与清单。
	point := exp.content.ResourcePoints[0]
	loot := c.call(protocol.Envelope{T: protocol.MsgLoot, RunID: first.RunID, PointID: point.PointID})
	if loot.T != protocol.MsgOK || !reflect.DeepEqual(loot.ItemIDs, point.ItemIDs) {
		t.Fatalf("loot %s: got %+v, want itemIds=%v", point.PointID, loot, point.ItemIDs)
	}

	resumed := mustStartRun(t, c, 1, true)
	if !reflect.DeepEqual(resumed.Consumed, []string{point.PointID}) {
		t.Fatalf("resume consumed = %v, want [%s]", resumed.Consumed, point.PointID)
	}
	if len(resumed.Looted) != 1 || resumed.Looted[0].PointID != point.PointID ||
		!reflect.DeepEqual(resumed.Looted[0].ItemIDs, point.ItemIDs) {
		t.Fatalf("resume looted = %+v, want one record for %s with %v", resumed.Looted, point.PointID, point.ItemIDs)
	}
	if resumed.RunID != first.RunID || resumed.Seed != first.Seed {
		t.Fatalf("resume changed run identity: %+v", resumed)
	}
}

// TestStartRunResumeWithoutActiveRunRejected resume=true 且无活跃 run 必须拒绝，
// 客户端据此不误建新局；resume=false 才是新建。
func TestStartRunResumeWithoutActiveRunRejected(t *testing.T) {
	exp := newExploration(t)
	_, addr := newRunServer(t, exp)
	c := dial(t, addr)
	registerPlayer(t, c, "run-resume")

	mustReject(t, c, protocol.Envelope{T: protocol.MsgStartRun, Difficulty: 1, Resume: true}, "no active run")
	created := mustStartRun(t, c, 1, false)
	again := mustStartRun(t, c, 1, true)
	if again.RunID != created.RunID {
		t.Fatalf("resume created a second run: %s vs %s", again.RunID, created.RunID)
	}
}

// TestStartRunRejectsUnknownDifficulty 难度必须是难度表的 tier，否则 rejected 且不开局。
func TestStartRunRejectsUnknownDifficulty(t *testing.T) {
	exp := newExploration(t)
	_, addr := newRunServer(t, exp)
	c := dial(t, addr)
	registerPlayer(t, c, "run-difficulty")

	mustReject(t, c, protocol.Envelope{T: protocol.MsgStartRun, Difficulty: 9}, "difficulty")
	// 被拒的请求没有留下活跃 run。
	mustReject(t, c, protocol.Envelope{T: protocol.MsgStartRun, Difficulty: 1, Resume: true}, "no active run")
	mustStartRun(t, c, 1, false)
}

// TestEncounterFlow 正常 / None 点 / 越界 / 非敌点 / 重复请求。
func TestEncounterFlow(t *testing.T) {
	exp := newExploration(t)
	_, addr := newRunServer(t, exp)
	c := dial(t, addr)
	registerPlayer(t, c, "run-encounter")
	start := mustStartRun(t, c, 1, false)

	present := exp.firstPresentEnemy(t)
	resp := c.call(protocol.Envelope{T: protocol.MsgEncounter, RunID: start.RunID, PointID: present.PointID})
	if resp.T != protocol.MsgOK {
		t.Fatalf("encounter %s: %+v", present.PointID, resp)
	}
	if resp.EnemyPartyID != present.EnemyPartyID || resp.Threat != present.Threat {
		t.Fatalf("encounter %s = party %d threat %d, want party %d threat %d",
			present.PointID, resp.EnemyPartyID, resp.Threat, present.EnemyPartyID, present.Threat)
	}

	// 幂等：重复请求（含战斗重试）返回同一结果。
	again := c.call(protocol.Envelope{T: protocol.MsgEncounter, RunID: start.RunID, PointID: present.PointID})
	if again.T != protocol.MsgOK || again.EnemyPartyID != resp.EnemyPartyID || again.Threat != resp.Threat {
		t.Fatalf("encounter not idempotent:\n%+v\n%+v", resp, again)
	}

	// 本局被掷为 None 的敌点视为不存在。
	absent := exp.firstAbsentEnemy(t)
	mustReject(t, c, protocol.Envelope{T: protocol.MsgEncounter, RunID: start.RunID, PointID: absent.PointID}, "does not exist")
	// 越界的敌点下标。
	mustReject(t, c, protocol.Envelope{T: protocol.MsgEncounter, RunID: start.RunID, PointID: "e99"}, "does not exist")
	// 物资点不是敌点。
	mustReject(t, c, protocol.Envelope{T: protocol.MsgEncounter, RunID: start.RunID, PointID: exp.content.ResourcePoints[0].PointID}, "does not exist")
	// runId 不匹配。
	mustReject(t, c, protocol.Envelope{T: protocol.MsgEncounter, RunID: "not-the-run", PointID: present.PointID}, "runId")
}

// TestLootFlow 正常 / 重复同清单 / 不存在点 / 无活跃 run / runId 不匹配。
func TestLootFlow(t *testing.T) {
	exp := newExploration(t)
	_, addr := newRunServer(t, exp)
	c := dial(t, addr)
	registerPlayer(t, c, "run-loot")
	start := mustStartRun(t, c, 1, false)

	first, second := exp.content.ResourcePoints[0], exp.content.ResourcePoints[1]
	loot := c.call(protocol.Envelope{T: protocol.MsgLoot, RunID: start.RunID, PointID: first.PointID})
	if loot.T != protocol.MsgOK || !reflect.DeepEqual(loot.ItemIDs, first.ItemIDs) {
		t.Fatalf("loot %s = %+v, want %v", first.PointID, loot, first.ItemIDs)
	}
	// 重复请求回放同一清单（不重复消耗、不改清单）。
	repeat := c.call(protocol.Envelope{T: protocol.MsgLoot, RunID: start.RunID, PointID: first.PointID})
	if repeat.T != protocol.MsgOK || !reflect.DeepEqual(repeat.ItemIDs, loot.ItemIDs) {
		t.Fatalf("loot not idempotent:\n%+v\n%+v", loot, repeat)
	}
	other := c.call(protocol.Envelope{T: protocol.MsgLoot, RunID: start.RunID, PointID: second.PointID})
	if other.T != protocol.MsgOK || !reflect.DeepEqual(other.ItemIDs, second.ItemIDs) {
		t.Fatalf("loot %s = %+v, want %v", second.PointID, other, second.ItemIDs)
	}

	mustReject(t, c, protocol.Envelope{T: protocol.MsgLoot, RunID: start.RunID, PointID: "r99"}, "does not exist")
	mustReject(t, c, protocol.Envelope{T: protocol.MsgLoot, RunID: start.RunID, PointID: exp.firstPresentEnemy(t).PointID}, "does not exist")
	mustReject(t, c, protocol.Envelope{T: protocol.MsgLoot, RunID: "not-the-run", PointID: first.PointID}, "runId")

	// consumed/looted 按开箱顺序回放。
	resumed := mustStartRun(t, c, 1, true)
	if !reflect.DeepEqual(resumed.Consumed, []string{first.PointID, second.PointID}) {
		t.Fatalf("consumed order = %v", resumed.Consumed)
	}
	if len(resumed.Looted) != 2 || resumed.Looted[1].PointID != second.PointID {
		t.Fatalf("looted order = %+v", resumed.Looted)
	}
}

// TestRunMessagesRequireLoginAndActiveRun 三条新消息都要登录；无活跃 run 时 encounter/loot 拒绝。
func TestRunMessagesRequireLoginAndActiveRun(t *testing.T) {
	exp := newExploration(t)
	_, addr := newRunServer(t, exp)

	anonymous := dial(t, addr)
	for _, env := range []protocol.Envelope{
		{T: protocol.MsgStartRun, Difficulty: 1},
		{T: protocol.MsgEncounter, RunID: "x", PointID: "e0"},
		{T: protocol.MsgLoot, RunID: "x", PointID: "r0"},
	} {
		resp := anonymous.call(env)
		if resp.T != protocol.MsgErr || resp.Code != protocol.ErrCodeUnauthorized {
			t.Fatalf("%s without login: %+v", env.T, resp)
		}
	}

	c := dial(t, addr)
	registerPlayer(t, c, "run-no-session")
	mustReject(t, c, protocol.Envelope{T: protocol.MsgEncounter, RunID: "x", PointID: exp.firstPresentEnemy(t).PointID}, "no active run")
	mustReject(t, c, protocol.Envelope{T: protocol.MsgLoot, RunID: "x", PointID: exp.content.ResourcePoints[0].PointID}, "no active run")
}

// TestSettleReconciliationRejectsOverLimit 带出物资超过（开箱所得 ∪ 携带入场 ∪ 战斗所得）时整批不落地，
// 且被拒的 settle 不终结活跃 run（run 还在，可继续交互）。
func TestSettleReconciliationRejectsOverLimit(t *testing.T) {
	exp := newExploration(t)
	_, addr := newRunServer(t, exp)
	c := dial(t, addr)
	registerPlayer(t, c, "run-overlimit")
	mustOK(c, protocol.Envelope{T: protocol.MsgMigrate, Migrate: &protocol.MigratePayload{
		Characters: []protocol.MigrateChar{{CID: 1001}},
	}})

	start := mustStartRun(t, c, 1, false)
	point := exp.content.ResourcePoints[0]
	loot := c.call(protocol.Envelope{T: protocol.MsgLoot, RunID: start.RunID, PointID: point.PointID})
	if loot.T != protocol.MsgOK || len(loot.ItemIDs) == 0 {
		t.Fatalf("loot: %+v", loot)
	}
	itemID := loot.ItemIDs[0]

	// 开箱只拿到 1 件，却上报 2 件 → 拒绝，且整批不落地。
	mustReject(t, c, protocol.Envelope{T: protocol.MsgSettle, RunID: start.RunID, Outcome: 1,
		Ops: []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: itemID, Count: 2}}}, "only")
	snap := mustOK(c, protocol.Envelope{T: protocol.MsgPull})
	if len(snap.Items) != 0 {
		t.Fatalf("rejected settle leaked items: %+v", snap.Items)
	}

	// 被拒的 settle 不是终结路径：run 仍然活跃。
	if resp := c.call(protocol.Envelope{T: protocol.MsgEncounter, RunID: start.RunID, PointID: exp.firstPresentEnemy(t).PointID}); resp.T != protocol.MsgOK {
		t.Fatalf("rejected settle terminated the run: %+v", resp)
	}

	// 合法上报（1 件）落地，并清除活跃 run（settle 是唯一终结路径）。
	applied := mustOK(c, protocol.Envelope{T: protocol.MsgSettle, RunID: start.RunID, Outcome: 1,
		Ops: []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: itemID, Count: 1}}})
	if len(applied.Items) != 1 || applied.Items[0].ItemID != itemID {
		t.Fatalf("settle did not apply the loot: %+v", applied.Items)
	}
	mustReject(t, c, protocol.Envelope{T: protocol.MsgEncounter, RunID: start.RunID, PointID: exp.firstPresentEnemy(t).PointID}, "no active run")
	mustReject(t, c, protocol.Envelope{T: protocol.MsgStartRun, Difficulty: 1, Resume: true}, "no active run")
}

// TestSettleAllowanceIncludesCarriedInAndBattleLoot 携带入场额度与战斗所得（客户端上报、本阶段信任）都计入额度。
func TestSettleAllowanceIncludesCarriedInAndBattleLoot(t *testing.T) {
	exp := newExploration(t)
	_, addr := newRunServer(t, exp)

	t.Run("carried in", func(t *testing.T) {
		c := dial(t, addr)
		registerPlayer(t, c, "run-carried")
		mustOK(c, protocol.Envelope{T: protocol.MsgMigrate, Migrate: &protocol.MigratePayload{
			Characters: []protocol.MigrateChar{{CID: 1001}},
			Carried:    []protocol.Stack{{ItemID: 5001, Count: 5}},
		}})
		start := mustStartRun(t, c, 1, false)
		// 未开箱，但带了 5 个 5001 进场：带出 5 个在额度内。
		mustOK(c, protocol.Envelope{T: protocol.MsgSettle, RunID: start.RunID, Outcome: 1,
			Ops: []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: 5001, Count: 5}}})
	})

	t.Run("battle loot", func(t *testing.T) {
		c := dial(t, addr)
		registerPlayer(t, c, "run-battleloot")
		mustOK(c, protocol.Envelope{T: protocol.MsgMigrate, Migrate: &protocol.MigratePayload{
			Characters: []protocol.MigrateChar{{CID: 1001}},
		}})
		start := mustStartRun(t, c, 1, false)
		// 战斗所得本阶段信任客户端上报：靠 battleLoot 计入额度。
		mustOK(c, protocol.Envelope{T: protocol.MsgSettle, RunID: start.RunID, Outcome: 2,
			BattleLoot: []protocol.Stack{{ItemID: 5301, Count: 3}},
			Ops:        []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: 5301, Count: 3}}})
	})

	t.Run("battle loot excess rejected", func(t *testing.T) {
		c := dial(t, addr)
		registerPlayer(t, c, "run-battleloot-over")
		mustOK(c, protocol.Envelope{T: protocol.MsgMigrate, Migrate: &protocol.MigratePayload{
			Characters: []protocol.MigrateChar{{CID: 1001}},
		}})
		start := mustStartRun(t, c, 1, false)
		mustReject(t, c, protocol.Envelope{T: protocol.MsgSettle, RunID: start.RunID, Outcome: 2,
			BattleLoot: []protocol.Stack{{ItemID: 5301, Count: 1}},
			Ops:        []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: 5301, Count: 3}}}, "only")
	})
}

// TestSweepExpiresIdleRun TTL 清理超时未结算的 run（Sweep 由后台 goroutine 周期调用，测试直接驱动）。
func TestSweepExpiresIdleRun(t *testing.T) {
	exp := newExploration(t)
	srv, addr := newRunServer(t, exp)
	c := dial(t, addr)
	registerPlayer(t, c, "run-ttl")
	start := mustStartRun(t, c, 1, false)

	if removed := srv.Run.Sweep(time.Now().Add(24 * time.Hour)); removed != 1 {
		t.Fatalf("Sweep removed %d runs, want 1", removed)
	}
	if removed := srv.Run.Sweep(time.Now().Add(48 * time.Hour)); removed != 0 {
		t.Fatalf("second Sweep removed %d runs, want 0", removed)
	}
	mustReject(t, c, protocol.Envelope{T: protocol.MsgEncounter, RunID: start.RunID, PointID: exp.firstPresentEnemy(t).PointID}, "no active run")
	mustReject(t, c, protocol.Envelope{T: protocol.MsgStartRun, Difficulty: 1, Resume: true}, "no active run")
}

// TestFullFlowStartEncounterLootSettle 联网探索全流程：开局 → 撞敌 → 开箱 → 结算。
func TestFullFlowStartEncounterLootSettle(t *testing.T) {
	exp := newExploration(t)
	_, addr := newRunServer(t, exp)
	c := dial(t, addr)
	registerPlayer(t, c, "run-full")
	mustOK(c, protocol.Envelope{T: protocol.MsgMigrate, Migrate: &protocol.MigratePayload{
		Characters: []protocol.MigrateChar{{CID: 1001}, {CID: 1002}},
		Party:      []int{1001, 1002},
		Difficulty: 1,
		Carried:    []protocol.Stack{{ItemID: 5001, Count: 2}},
	}})

	start := mustStartRun(t, c, 1, false)
	enemy := exp.firstPresentEnemy(t)
	encounter := c.call(protocol.Envelope{T: protocol.MsgEncounter, RunID: start.RunID, PointID: enemy.PointID})
	if encounter.T != protocol.MsgOK || encounter.EnemyPartyID != enemy.EnemyPartyID || encounter.Threat != enemy.Threat {
		t.Fatalf("encounter: %+v", encounter)
	}

	point := exp.content.ResourcePoints[0]
	loot := c.call(protocol.Envelope{T: protocol.MsgLoot, RunID: start.RunID, PointID: point.PointID})
	if loot.T != protocol.MsgOK || !reflect.DeepEqual(loot.ItemIDs, point.ItemIDs) {
		t.Fatalf("loot: %+v", loot)
	}

	// 带出：开箱所得 + 携带入场（2 个 5001）。
	ops := []protocol.Op{{K: protocol.OpAdd, TID: 1, ItemID: point.ItemIDs[0], Count: 1}}
	if point.ItemIDs[0] != 5001 {
		ops = append(ops, protocol.Op{K: protocol.OpAdd, TID: 2, ItemID: 5001, Count: 2})
	}
	settled := mustOK(c, protocol.Envelope{T: protocol.MsgSettle, RunID: start.RunID, Outcome: 1,
		Difficulty: 1, Seed: start.Seed, StartedAt: 1000, EndedAt: 2000, Ops: ops})
	if len(settled.Items) != len(ops) {
		t.Fatalf("settle snapshot rows = %d, want %d", len(settled.Items), len(ops))
	}

	// 战绩里的 seed 与开局下发的一致（审计链）。
	hist := c.call(protocol.Envelope{T: protocol.MsgHistory})
	if hist.T != protocol.MsgOK || len(hist.History) != 1 {
		t.Fatalf("history: %+v", hist)
	}
	if hist.History[0].RunID != start.RunID || hist.History[0].Seed != start.Seed {
		t.Fatalf("history run does not match start_run: %+v", hist.History[0])
	}

	// 结算后本局终结：resume 不得重开。
	mustReject(t, c, protocol.Envelope{T: protocol.MsgStartRun, Difficulty: 1, Resume: true}, "no active run")
}
