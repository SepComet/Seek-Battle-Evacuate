package run

import (
	"errors"
	"fmt"
	"reflect"
	"testing"
	"time"

	"sbe-server/internal/core"
	"sbe-server/internal/gametable"
	"sbe-server/internal/itemtable"
	"sbe-server/internal/mapdef"
	"sbe-server/internal/mapgen"
)

// 本文件覆盖 run 会话管理器的外部行为：开局幂等、交互校验、结算额度与 TTL。
// 断言全部走 Manager 的公开方法；期望内容由 mapgen.Generate 在测试内独立算出，
// 不复用被测实现的中间结果。时间通过 Config.Now 注入，TTL 用例不依赖真实时钟。

// testContent 内联配置 + 固定种子下的权威内容。
type testContent struct {
	layout  *mapdef.Layout
	tables  *gametable.Tables
	seed    int64
	content *mapgen.Content
}

// buildTestContent 装配内联探索配置，并挑一个「既有存在敌点、又有 None 敌点」的种子。
func buildTestContent(t *testing.T) *testContent {
	t.Helper()

	catalog, err := itemtable.NewCatalog([]itemtable.CatalogEntry{
		{ID: 5001, Rarity: 1, StackLimit: 99},
		{ID: 5002, Rarity: 1, StackLimit: 99},
		{ID: 5101, Rarity: 2, StackLimit: 1},
		{ID: 5102, Rarity: 2, StackLimit: 1},
		{ID: 5301, Rarity: 3, StackLimit: 5},
		{ID: 5302, Rarity: 3, StackLimit: 5},
	})
	if err != nil {
		t.Fatalf("NewCatalog: %v", err)
	}

	layout := &mapdef.Layout{
		Rooms: []mapdef.Room{{Type: 0, MinX: -100, MinY: -100, MaxX: 100, MaxY: 100}},
		ResourcePoints: []mapdef.ResourcePoint{
			{X: -4, Y: 5, ResourcePointID: 1},
			{X: 2, Y: 7, ResourcePointID: 2},
		},
		EnemySpawnPoints:  []mapdef.Vec2{{X: -19, Y: 1}, {X: -14, Y: 4}, {X: 3, Y: 9}, {X: 12, Y: -2}},
		PlayerSpawnPoints: []mapdef.Vec2{{X: 1, Y: -16}, {X: 5, Y: -16}},
		ExtractionPoints:  []mapdef.Vec2{{X: 30, Y: -3}},
	}

	resources := []gametable.ResourcePoint{{ID: 1}, {ID: 2}}
	for i := range resources {
		resources[i].LootConfigs = []gametable.LootConfig{{
			Difficulty: 1, MinCount: 1, MaxCount: 2,
			WhiteWeight: 60, GreenWeight: 25, BlueWeight: 15,
		}}
	}
	tables := &gametable.Tables{
		Resources: gametable.NewResourceTable(resources),
		Parties: gametable.NewEnemyPartyTable([]gametable.EnemyParty{
			{ID: 4001, Threat: 1, EnemyIDs: []int{3001}},
			{ID: 4201, Threat: 3, EnemyIDs: []int{3001}},
		}),
		Difficulties: gametable.NewDifficultyTable([]gametable.Difficulty{{Tier: 1, Weights: []gametable.ThreatWeight{
			{Threat: 0, Weight: 3}, {Threat: 1, Weight: 3}, {Threat: 3, Weight: 2},
		}}}),
		Items: catalog,
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
		if present > 0 && absent > 0 {
			return &testContent{layout: layout, tables: tables, seed: seed, content: content}
		}
	}
	t.Fatal("no seed yields both present and absent enemy points")
	return nil
}

// manager 以固定种子、可预测 runId（run-1、run-2 …）与可控时钟构造管理器。
func (c *testContent) manager(clock *fakeClock) *Manager {
	next := 0
	return NewManager(Config{
		Layout: c.layout,
		Tables: c.tables,
		Seed:   func() (int64, error) { return c.seed, nil },
		RunID: func() (string, error) {
			next++
			return fmt.Sprintf("run-%d", next), nil
		},
		Now: clock.Now,
	})
}

// fakeClock 可推进的测试时钟。
type fakeClock struct{ now time.Time }

func newFakeClock() *fakeClock {
	return &fakeClock{now: time.Date(2026, 10, 25, 12, 0, 0, 0, time.UTC)}
}

func (c *fakeClock) Now() time.Time          { return c.now }
func (c *fakeClock) Advance(d time.Duration) { c.now = c.now.Add(d) }

func (c *testContent) firstPresent(t *testing.T) mapgen.EnemyPointContent {
	t.Helper()
	for _, point := range c.content.EnemyPoints {
		if point.Present {
			return point
		}
	}
	t.Fatal("no present enemy point")
	return mapgen.EnemyPointContent{}
}

func (c *testContent) firstAbsent(t *testing.T) mapgen.EnemyPointContent {
	t.Helper()
	for _, point := range c.content.EnemyPoints {
		if !point.Present {
			return point
		}
	}
	t.Fatal("no absent enemy point")
	return mapgen.EnemyPointContent{}
}

// TestStartRunCreatesSessionAndIsIdempotent 新建局后再次 Start（不论 resume）返回现状。
func TestStartRunCreatesSessionAndIsIdempotent(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())

	view, err := m.Start(7, 1, false)
	if err != nil {
		t.Fatalf("Start: %v", err)
	}
	if view.RunID != "run-1" || view.Seed != tc.seed || view.Difficulty != 1 {
		t.Fatalf("view identity = %+v", view)
	}
	if view.Spawn != tc.content.PlayerSpawnPoint || view.Extraction != tc.content.ExtractionPoint {
		t.Fatalf("spawn/extraction drifted from generated content: %+v", view)
	}
	if len(view.Consumed) != 0 || len(view.Looted) != 0 {
		t.Fatalf("fresh run reports consumed/looted: %+v", view)
	}

	again, err := m.Start(7, 1, true)
	if err != nil {
		t.Fatalf("Start(resume): %v", err)
	}
	if !reflect.DeepEqual(view, again) {
		t.Fatalf("Start is not idempotent:\n%+v\n%+v", view, again)
	}
	// resume=false 且已有活跃 run 也返回现状（不冲突）。
	third, err := m.Start(7, 2, false)
	if err != nil {
		t.Fatalf("Start(resume=false) with active run: %v", err)
	}
	if third.RunID != view.RunID {
		t.Fatalf("start_run replaced the active run: %s vs %s", third.RunID, view.RunID)
	}
}

// TestStartRunResumeWithoutSessionRejected resume=true 且无活跃 run → ErrRejected，msg 含 no active run。
func TestStartRunResumeWithoutSessionRejected(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())

	_, err := m.Start(7, 1, true)
	if !errors.Is(err, core.ErrRejected) {
		t.Fatalf("resume without session: err = %v, want ErrRejected", err)
	}
	if err.Error() != "op rejected: no active run" {
		t.Fatalf("resume without session message = %q", err.Error())
	}
	// 其他用户的 run 不算我的活跃 run。
	if _, err := m.Start(8, 1, false); err != nil {
		t.Fatalf("Start other user: %v", err)
	}
	if _, err := m.Start(7, 1, true); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("resume must not see another user's run: err = %v", err)
	}
}

// TestStartRunRejectsUnknownDifficulty 难度不在难度表 → ErrRejected，且不留下活跃 run。
func TestStartRunRejectsUnknownDifficulty(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())

	if _, err := m.Start(7, 2, false); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("unknown difficulty: err = %v, want ErrRejected", err)
	}
	if _, err := m.Start(7, 1, true); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("rejected start left a session behind")
	}
}

// TestEncounterReturnsGeneratedParty 正常点返回生成结果；不存在的点（None/越界/非敌点）拒绝。
func TestEncounterReturnsGeneratedParty(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())
	if _, err := m.Start(7, 1, false); err != nil {
		t.Fatalf("Start: %v", err)
	}
	present := tc.firstPresent(t)

	partyID, threat, err := m.Encounter(7, "run-1", present.PointID)
	if err != nil {
		t.Fatalf("Encounter(%s): %v", present.PointID, err)
	}
	if partyID != present.EnemyPartyID || threat != present.Threat {
		t.Fatalf("Encounter(%s) = %d/%d, want %d/%d", present.PointID, partyID, threat, present.EnemyPartyID, present.Threat)
	}
	// 幂等：重复请求同结果（encounter 不消耗点）。
	for i := 0; i < 3; i++ {
		againParty, againThreat, err := m.Encounter(7, "run-1", present.PointID)
		if err != nil || againParty != partyID || againThreat != threat {
			t.Fatalf("Encounter retry #%d = %d/%d (%v)", i, againParty, againThreat, err)
		}
	}

	absent := tc.firstAbsent(t)
	cases := []string{absent.PointID, "e99", "e-1", "e", "e01", tc.content.ResourcePoints[0].PointID, "garbage"}
	for _, pointID := range cases {
		if _, _, err := m.Encounter(7, "run-1", pointID); !errors.Is(err, core.ErrRejected) {
			t.Fatalf("Encounter(%q): err = %v, want ErrRejected", pointID, err)
		}
	}
}

// TestEncounterRequiresActiveMatchingRun 无活跃 run 与 runId 不匹配都拒绝。
func TestEncounterRequiresActiveMatchingRun(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())
	pointID := tc.firstPresent(t).PointID

	if _, _, err := m.Encounter(7, "run-1", pointID); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("Encounter without run: err = %v", err)
	}
	if _, err := m.Start(7, 1, false); err != nil {
		t.Fatalf("Start: %v", err)
	}
	if _, _, err := m.Encounter(7, "run-2", pointID); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("Encounter with foreign runId: err = %v", err)
	}
	if _, _, err := m.Encounter(8, "run-1", pointID); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("another user must not see this run: err = %v", err)
	}
}

// TestLootIsIdempotentAndTracksConsumed 首次开箱记账，重试回放同一清单，consumed 按开箱序。
func TestLootIsIdempotentAndTracksConsumed(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())
	if _, err := m.Start(7, 1, false); err != nil {
		t.Fatalf("Start: %v", err)
	}

	first, second := tc.content.ResourcePoints[0], tc.content.ResourcePoints[1]
	items, err := m.Loot(7, "run-1", first.PointID)
	if err != nil {
		t.Fatalf("Loot(%s): %v", first.PointID, err)
	}
	if !reflect.DeepEqual(items, first.ItemIDs) {
		t.Fatalf("Loot(%s) = %v, want %v", first.PointID, items, first.ItemIDs)
	}
	replay, err := m.Loot(7, "run-1", first.PointID)
	if err != nil || !reflect.DeepEqual(replay, items) {
		t.Fatalf("Loot replay = %v (%v), want %v", replay, err, items)
	}
	if _, err := m.Loot(7, "run-1", second.PointID); err != nil {
		t.Fatalf("Loot(%s): %v", second.PointID, err)
	}

	resumed, err := m.Start(7, 1, true)
	if err != nil {
		t.Fatalf("Start(resume): %v", err)
	}
	if !reflect.DeepEqual(resumed.Consumed, []string{first.PointID, second.PointID}) {
		t.Fatalf("consumed = %v", resumed.Consumed)
	}
	want := []LootRecord{
		{PointID: first.PointID, ItemIDs: first.ItemIDs},
		{PointID: second.PointID, ItemIDs: second.ItemIDs},
	}
	if !reflect.DeepEqual(resumed.Looted, want) {
		t.Fatalf("looted = %+v, want %+v", resumed.Looted, want)
	}

	// 未知点 / 非物资点 / runId 不匹配 / 无 run 都拒绝。
	for _, pointID := range []string{"r99", "r-1", "r", "r01", tc.firstPresent(t).PointID} {
		if _, err := m.Loot(7, "run-1", pointID); !errors.Is(err, core.ErrRejected) {
			t.Fatalf("Loot(%q): err = %v, want ErrRejected", pointID, err)
		}
	}
	if _, err := m.Loot(7, "run-2", first.PointID); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("Loot with foreign runId: err = %v", err)
	}
	if _, err := m.Loot(8, "run-1", first.PointID); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("Loot for another user: err = %v", err)
	}
}

// TestValidateSettleAllowance 额度 = 开箱所得 + 携带入场 + 战斗所得；超限拒绝，未超限放行。
func TestValidateSettleAllowance(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())
	if _, err := m.Start(7, 1, false); err != nil {
		t.Fatalf("Start: %v", err)
	}
	point := tc.content.ResourcePoints[0]
	lootedItems, err := m.Loot(7, "run-1", point.PointID)
	if err != nil {
		t.Fatalf("Loot: %v", err)
	}
	lootedID := lootedItems[0]

	carried := []core.Stack{{ItemID: 5301, Count: 2}}
	battle := []core.Stack{{ItemID: 5101, Count: 1}, {ItemID: 5301, Count: 1}}

	// 恰好用完额度：开箱 1 件 + 携带 2 件 + 战斗 1 件（同一种物品合并计数）。
	within := map[int]int{lootedID: 1, 5301: 3, 5101: 1}
	if err := m.ValidateSettle(7, "run-1", battle, within, carried); err != nil {
		t.Fatalf("ValidateSettle within allowance: %v", err)
	}
	// 5101 只允许 1 件（战斗所得），带出 2 件即超限。
	over := map[int]int{lootedID: 1, 5301: 3, 5101: 2}
	if err := m.ValidateSettle(7, "run-1", battle, over, carried); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("ValidateSettle over allowance: err = %v, want ErrRejected", err)
	}
	// 开箱所得之外凭空 add 同类物品也算超限。
	if err := m.ValidateSettle(7, "run-1", nil, map[int]int{lootedID: 2}, nil); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("ValidateSettle phantom loot: err = %v, want ErrRejected", err)
	}
	// 负数战斗所得是非法上报。
	if err := m.ValidateSettle(7, "run-1", []core.Stack{{ItemID: 5101, Count: -1}}, nil, nil); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("ValidateSettle negative battle loot: err = %v, want ErrRejected", err)
	}
	// 超限后 session 未被污染：合法上报仍可通过。
	if err := m.ValidateSettle(7, "run-1", battle, within, carried); err != nil {
		t.Fatalf("ValidateSettle after rejection: %v", err)
	}
}

// TestValidateSettleSkipsWhenRunUnknown 活跃 run 不存在或 runId 不匹配时跳过对账
// （v3 兼容与幂等回放路径，边界记录在 ticket Comments）。
func TestValidateSettleSkipsWhenRunUnknown(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())

	added := map[int]int{5001: 99}
	if err := m.ValidateSettle(7, "run-1", nil, added, nil); err != nil {
		t.Fatalf("ValidateSettle without session must skip: %v", err)
	}
	if _, err := m.Start(7, 1, false); err != nil {
		t.Fatalf("Start: %v", err)
	}
	if err := m.ValidateSettle(7, "run-2", nil, added, nil); err != nil {
		t.Fatalf("ValidateSettle with foreign runId must skip: %v", err)
	}
}

// TestLootedSums 开箱所得的 itemId 汇总（跨点累加）。
func TestLootedSums(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())
	if _, err := m.Start(7, 1, false); err != nil {
		t.Fatalf("Start: %v", err)
	}
	want := map[int]int{}
	for _, point := range tc.content.ResourcePoints[:2] {
		items, err := m.Loot(7, "run-1", point.PointID)
		if err != nil {
			t.Fatalf("Loot(%s): %v", point.PointID, err)
		}
		for _, itemID := range items {
			want[itemID]++
		}
	}
	if got := m.LootedSums(7, "run-1"); !reflect.DeepEqual(got, want) {
		t.Fatalf("LootedSums = %v, want %v", got, want)
	}
	if got := m.LootedSums(7, "run-x"); got != nil {
		t.Fatalf("LootedSums for unknown run = %v, want nil", got)
	}
}

// TestActiveReportsMatchingRun Active 是分派层「是否需要对账」的判据：
// 只有 runId 匹配的活跃 run 才返回 true。
func TestActiveReportsMatchingRun(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())

	if m.Active(7, "run-1") {
		t.Fatal("Active without session")
	}
	if _, err := m.Start(7, 1, false); err != nil {
		t.Fatalf("Start: %v", err)
	}
	if !m.Active(7, "run-1") {
		t.Fatal("Active for the running session")
	}
	if m.Active(7, "run-2") || m.Active(8, "run-1") {
		t.Fatal("Active matched a foreign run")
	}
	m.Finish(7, "run-1")
	if m.Active(7, "run-1") {
		t.Fatal("Active after Finish")
	}
}

// TestFinishTerminatesRun settle 是唯一终结路径：Finish 后交互与 resume 都拒绝。
func TestFinishTerminatesRun(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())
	if _, err := m.Start(7, 1, false); err != nil {
		t.Fatalf("Start: %v", err)
	}
	pointID := tc.firstPresent(t).PointID

	m.Finish(7, "run-1")
	if _, _, err := m.Encounter(7, "run-1", pointID); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("Encounter after Finish: err = %v", err)
	}
	if _, err := m.Start(7, 1, true); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("resume after Finish: err = %v", err)
	}
	// 新开局是另一局（新 runId）。
	view, err := m.Start(7, 1, false)
	if err != nil {
		t.Fatalf("Start after Finish: %v", err)
	}
	if view.RunID == "run-1" {
		t.Fatalf("new run reused the finished runId")
	}
}

// TestSweepExpiresIdleRun TTL 清理超时未结算的 run（后台 goroutine 周期调用 Sweep，测试直接驱动）。
func TestSweepExpiresIdleRun(t *testing.T) {
	tc := buildTestContent(t)
	clock := newFakeClock()
	m := tc.manager(clock)
	if _, err := m.Start(7, 1, false); err != nil {
		t.Fatalf("Start: %v", err)
	}

	// TTL 内不动。
	clock.Advance(DefaultTTL - time.Minute)
	if removed := m.Sweep(clock.Now()); removed != 0 {
		t.Fatalf("Sweep before TTL removed %d runs", removed)
	}
	if _, _, err := m.Encounter(7, "run-1", tc.firstPresent(t).PointID); err != nil {
		t.Fatalf("Encounter before TTL: %v", err)
	}

	// 超过 TTL（从最后一次交互起算）即清理。
	clock.Advance(DefaultTTL + time.Minute)
	if removed := m.Sweep(clock.Now()); removed != 1 {
		t.Fatalf("Sweep after TTL removed %d runs, want 1", removed)
	}
	if removed := m.Sweep(clock.Now()); removed != 0 {
		t.Fatalf("second Sweep removed %d runs, want 0", removed)
	}
	if _, err := m.Loot(7, "run-1", tc.content.ResourcePoints[0].PointID); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("swept run still usable: err = %v", err)
	}
	if _, err := m.Start(7, 1, true); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("resume after Sweep: err = %v", err)
	}
}

// TestSweepKeepsRunTouchedRecently 交互会续期 LastTouched：超时判断以最后一次交互为准。
func TestSweepKeepsRunTouchedRecently(t *testing.T) {
	tc := buildTestContent(t)
	clock := newFakeClock()
	m := tc.manager(clock)
	if _, err := m.Start(7, 1, false); err != nil {
		t.Fatalf("Start: %v", err)
	}

	clock.Advance(DefaultTTL - time.Minute)
	if _, err := m.Loot(7, "run-1", tc.content.ResourcePoints[0].PointID); err != nil {
		t.Fatalf("Loot: %v", err)
	}
	// 距开局已超过 TTL，但距最后一次交互只有 2 分钟 → 保留。
	clock.Advance(2 * time.Minute)
	if removed := m.Sweep(clock.Now()); removed != 0 {
		t.Fatalf("Sweep removed a recently touched run: %d", removed)
	}
}

// TestRejectedRequestsDoNotExtendTTL 被拒的请求不续期（防止无效流量把 run 永生）。
func TestRejectedRequestsDoNotExtendTTL(t *testing.T) {
	tc := buildTestContent(t)
	clock := newFakeClock()
	m := tc.manager(clock)
	if _, err := m.Start(7, 1, false); err != nil {
		t.Fatalf("Start: %v", err)
	}

	clock.Advance(DefaultTTL + time.Minute)
	if _, _, err := m.Encounter(7, "run-x", tc.firstPresent(t).PointID); !errors.Is(err, core.ErrRejected) {
		t.Fatalf("Encounter with foreign runId: err = %v", err)
	}
	if removed := m.Sweep(clock.Now()); removed != 1 {
		t.Fatalf("rejected request kept the run alive: removed %d", removed)
	}
}

// TestStartSweeperStops 后台清理 goroutine 可启动并可停止（生产装配路径不泄漏 goroutine）。
func TestStartSweeperStops(t *testing.T) {
	tc := buildTestContent(t)
	m := tc.manager(newFakeClock())
	stop := m.StartSweeper(5 * time.Millisecond)
	stop()
	stop() // 幂等：重复调用不 panic
}

// TestSeedAllocatorStaysInRange 服务器分配的种子必须落在 [1, 2147483647]（契约 §5）。
func TestSeedAllocatorStaysInRange(t *testing.T) {
	seen := make(map[int64]bool)
	for i := 0; i < 500; i++ {
		seed, err := randomSeed()
		if err != nil {
			t.Fatalf("randomSeed: %v", err)
		}
		if seed < SeedMin || seed > SeedMax {
			t.Fatalf("seed %d out of range [%d,%d]", seed, SeedMin, SeedMax)
		}
		seen[seed] = true
	}
	if len(seen) < 400 {
		t.Fatalf("randomSeed produced only %d distinct seeds out of 500", len(seen))
	}
}

// TestStartRejectsOutOfRangeSeed 注入的种子分配器越界即报错（服务器配置缺陷，不静默夹取）。
func TestStartRejectsOutOfRangeSeed(t *testing.T) {
	tc := buildTestContent(t)
	for _, seed := range []int64{0, -1, SeedMax + 1} {
		m := NewManager(Config{
			Layout: tc.layout,
			Tables: tc.tables,
			Seed:   func() (int64, error) { return seed, nil },
			RunID:  func() (string, error) { return "run-1", nil },
		})
		_, err := m.Start(7, 1, false)
		if err == nil {
			t.Fatalf("Start with seed %d: want error", seed)
		}
		if errors.Is(err, core.ErrRejected) {
			t.Fatalf("Start with seed %d: out-of-range seed is a server bug, not a rejection", seed)
		}
	}
}
