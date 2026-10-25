package gametable

import (
	"os"
	"path/filepath"
	"testing"
)

// 真实导出产物：由 gen_cli 导出并经 tools/sync_config 同步到 config。
const (
	realResourcePath = "../../config/tbresourcepointconfig.json"
	realPartyPath    = "../../config/tbenemypartyconfig.json"
)

func TestLoadResourcePointsRepositoryExport(t *testing.T) {
	table, err := LoadResourcePoints(realResourcePath)
	if err != nil {
		t.Fatalf("LoadResourcePoints(%s): %v", realResourcePath, err)
	}

	for id := 1; id <= 5; id++ {
		point, ok := table.Get(id)
		if !ok {
			t.Fatalf("resource point %d missing", id)
		}
		// 每个物资点都必须有 1~3 三档难度的掉落配置（生成器按难度查表，缺失即报错）
		for difficulty := 1; difficulty <= 3; difficulty++ {
			loot, ok := point.LootFor(difficulty)
			if !ok {
				t.Fatalf("resource point %d: no loot config for difficulty %d", id, difficulty)
			}
			total := 0
			for _, weight := range loot.Weights() {
				total += weight
			}
			if total <= 0 {
				t.Fatalf("resource point %d difficulty %d: all rarity weights are zero", id, difficulty)
			}
			if loot.MinCount <= 0 || loot.MaxCount < loot.MinCount {
				t.Fatalf("resource point %d difficulty %d: bad count range [%d,%d]",
					id, difficulty, loot.MinCount, loot.MaxCount)
			}
		}
	}

	// 抽查真实数值：资源点 1 难度 1 为 70/25/5/0/0，数量 1~2
	loot, ok := mustGet(t, table, 1).LootFor(1)
	if !ok {
		t.Fatal("resource point 1 difficulty 1 missing")
	}
	if want := [RarityCount]int{70, 25, 5, 0, 0}; loot.Weights() != want {
		t.Fatalf("resource point 1 difficulty 1 weights = %v, want %v", loot.Weights(), want)
	}
	if loot.MinCount != 1 || loot.MaxCount != 2 {
		t.Fatalf("resource point 1 difficulty 1 counts = [%d,%d], want [1,2]", loot.MinCount, loot.MaxCount)
	}

	// 难度 3 的红色权重必须已配置（高难度的红装产出）
	loot3, ok := mustGet(t, table, 1).LootFor(3)
	if !ok {
		t.Fatal("resource point 1 difficulty 3 missing")
	}
	if loot3.RedWeight != 10 {
		t.Fatalf("resource point 1 difficulty 3 redWeight = %d, want 10", loot3.RedWeight)
	}

	if _, ok := table.Get(404); ok {
		t.Fatal("unknown resource point should miss")
	}
}

func TestLoadEnemyPartiesRepositoryExport(t *testing.T) {
	table, err := LoadEnemyParties(realPartyPath)
	if err != nil {
		t.Fatalf("LoadEnemyParties(%s): %v", realPartyPath, err)
	}

	// 按威胁等级建池，顺序即配置表顺序（生成器的队伍下标依赖此顺序）
	wantPools := map[int][]int{
		1: {4001, 4002},
		2: {4101, 4102},
		3: {4201, 4202, 4203},
	}
	for threat, wantIDs := range wantPools {
		pool := table.Pool(threat)
		if len(pool) != len(wantIDs) {
			t.Fatalf("threat %d pool size = %d, want %d", threat, len(pool), len(wantIDs))
		}
		for i, wantID := range wantIDs {
			if pool[i].ID != wantID {
				t.Fatalf("threat %d pool[%d] = %d, want %d", threat, i, pool[i].ID, wantID)
			}
			if pool[i].Threat != threat {
				t.Fatalf("threat %d pool[%d] carries threat %d", threat, i, pool[i].Threat)
			}
		}
	}

	if pool := table.Pool(0); len(pool) != 0 {
		t.Fatalf("threat 0 (None) pool should be empty, got %d entries", len(pool))
	}

	party, ok := table.Get(4202)
	if !ok {
		t.Fatal("enemy party 4202 missing")
	}
	if want := []int{3001, 3001, 3001, 3001}; !equalInts(party.EnemyIDs, want) {
		t.Fatalf("enemy party 4202 enemyIds = %v, want %v", party.EnemyIDs, want)
	}
	if party.Threat != 3 {
		t.Fatalf("enemy party 4202 threat = %d, want 3", party.Threat)
	}
}

func TestLoadResourcePointsRejectsInvalidTables(t *testing.T) {
	cases := []struct{ name, json string }{
		{"empty", `[]`},
		{"invalid id", `[{"id":0,"lootConfigs":[{"difficulty":1,"minCount":1,"maxCount":2,"whiteWeight":1}]}]`},
		{"duplicate id", `[{"id":1,"lootConfigs":[{"difficulty":1,"minCount":1,"maxCount":2,"whiteWeight":1}]},
			{"id":1,"lootConfigs":[{"difficulty":1,"minCount":1,"maxCount":2,"whiteWeight":1}]}]`},
		{"no loot config", `[{"id":1,"lootConfigs":[]}]`},
		{"zero weights", `[{"id":1,"lootConfigs":[{"difficulty":1,"minCount":1,"maxCount":1}]}]`},
		{"bad count range", `[{"id":1,"lootConfigs":[{"difficulty":1,"minCount":3,"maxCount":1,"whiteWeight":1}]}]`},
		{"zero min count", `[{"id":1,"lootConfigs":[{"difficulty":1,"minCount":0,"maxCount":1,"whiteWeight":1}]}]`},
		{"duplicate difficulty", `[{"id":1,"lootConfigs":[
			{"difficulty":1,"minCount":1,"maxCount":1,"whiteWeight":1},
			{"difficulty":1,"minCount":1,"maxCount":1,"whiteWeight":1}]}]`},
		{"malformed", "{"},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			path := filepath.Join(t.TempDir(), "tbresourcepointconfig.json")
			if err := os.WriteFile(path, []byte(c.json), 0o644); err != nil {
				t.Fatalf("write fixture: %v", err)
			}
			if _, err := LoadResourcePoints(path); err == nil {
				t.Fatal("want validation error")
			}
		})
	}
}

func TestLoadEnemyPartiesRejectsInvalidTables(t *testing.T) {
	cases := []struct{ name, json string }{
		{"empty", `[]`},
		{"invalid id", `[{"id":0,"enemyIds":[3001],"threatConfig":1}]`},
		{"duplicate id", `[{"id":1,"enemyIds":[3001],"threatConfig":1},{"id":1,"enemyIds":[3001],"threatConfig":1}]`},
		{"no enemies", `[{"id":1,"enemyIds":[],"threatConfig":1}]`},
		{"invalid enemy id", `[{"id":1,"enemyIds":[0],"threatConfig":1}]`},
		{"invalid threat", `[{"id":1,"enemyIds":[3001],"threatConfig":0}]`},
		{"malformed", "{"},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			path := filepath.Join(t.TempDir(), "tbenemypartyconfig.json")
			if err := os.WriteFile(path, []byte(c.json), 0o644); err != nil {
				t.Fatalf("write fixture: %v", err)
			}
			if _, err := LoadEnemyParties(path); err == nil {
				t.Fatal("want validation error")
			}
		})
	}
}

func TestLoadMissingFilesReturnError(t *testing.T) {
	dir := t.TempDir()
	if _, err := LoadResourcePoints(filepath.Join(dir, "nope.json")); err == nil {
		t.Fatal("want error for missing resource point table")
	}
	if _, err := LoadEnemyParties(filepath.Join(dir, "nope.json")); err == nil {
		t.Fatal("want error for missing enemy party table")
	}
}

func mustGet(t *testing.T, table *ResourceTable, id int) ResourcePoint {
	t.Helper()
	point, ok := table.Get(id)
	if !ok {
		t.Fatalf("resource point %d missing", id)
	}
	return point
}

func equalInts(a, b []int) bool {
	if len(a) != len(b) {
		return false
	}
	for i := range a {
		if a[i] != b[i] {
			return false
		}
	}
	return true
}
