package gametable

import (
	"os"
	"path/filepath"
	"testing"
)

// 真实导出产物：由 gen_cli 导出并经 tools/sync_config 同步到 config。
const realDifficultyPath = "../../config/tbdifficultyconfig.json"

func TestLoadDifficultiesRepositoryExport(t *testing.T) {
	table, err := LoadDifficulties(realDifficultyPath)
	if err != nil {
		t.Fatalf("LoadDifficulties(%s): %v", realDifficultyPath, err)
	}
	if table.Len() != 3 {
		t.Fatalf("difficulty count = %d, want 3", table.Len())
	}
	// 每档难度都必须有非空威胁权重（生成器按 tier 取，缺失即报错）
	for tier := 1; tier <= 3; tier++ {
		weights, ok := table.Weights(tier)
		if !ok {
			t.Fatalf("tier %d missing", tier)
		}
		if len(weights) == 0 {
			t.Fatalf("tier %d has no threat weights", tier)
		}
	}
	// 抽查真实数值：难度 1 为 None=30 Low=50 Middle=15 High=5（配置序）
	weights, _ := table.Weights(1)
	want := []ThreatWeight{{Threat: 0, Weight: 30}, {Threat: 1, Weight: 50},
		{Threat: 2, Weight: 15}, {Threat: 3, Weight: 5}}
	if len(weights) != len(want) {
		t.Fatalf("tier 1 weights = %v, want %v", weights, want)
	}
	for i := range want {
		if weights[i] != want[i] {
			t.Fatalf("tier 1 weights[%d] = %+v, want %+v", i, weights[i], want[i])
		}
	}
	if _, ok := table.Weights(0); ok {
		t.Fatal("tier 0 should not be configured")
	}
}

func TestLoadDifficultiesRejectsInvalidTables(t *testing.T) {
	cases := []struct{ name, json string }{
		{"empty", `[]`},
		{"malformed", `{`},
		{"zero tier", `[{"tier":0,"enemyParties":[{"threat":1,"weight":1}]}]`},
		{"duplicate tier", `[{"tier":1,"enemyParties":[{"threat":1,"weight":1}]},
			{"tier":1,"enemyParties":[{"threat":1,"weight":1}]}]`},
		{"no weights", `[{"tier":1,"enemyParties":[]}]`},
		{"all zero weights", `[{"tier":1,"enemyParties":[{"threat":0,"weight":0},{"threat":1,"weight":0}]}]`},
		{"negative weight", `[{"tier":1,"enemyParties":[{"threat":1,"weight":-1}]}]`},
		{"invalid threat", `[{"tier":1,"enemyParties":[{"threat":4,"weight":1}]}]`},
		{"negative threat", `[{"tier":1,"enemyParties":[{"threat":-1,"weight":1}]}]`},
		{"duplicate threat", `[{"tier":1,"enemyParties":[{"threat":1,"weight":1},{"threat":1,"weight":2}]}]`},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			path := filepath.Join(t.TempDir(), "tbdifficultyconfig.json")
			if err := os.WriteFile(path, []byte(c.json), 0o644); err != nil {
				t.Fatalf("write fixture: %v", err)
			}
			if _, err := LoadDifficulties(path); err == nil {
				t.Fatal("want validation error")
			}
		})
	}
}

func TestLoadDifficultiesMissingFileReturnsError(t *testing.T) {
	if _, err := LoadDifficulties(filepath.Join(t.TempDir(), "nope.json")); err == nil {
		t.Fatal("want error for missing file")
	}
}

// TestNewTablesPreserveOrder 内存构造器必须保序：队伍池顺序 = 入参顺序（生成器按池下标取模）。
func TestNewTablesPreserveOrder(t *testing.T) {
	parties := NewEnemyPartyTable([]EnemyParty{
		{ID: 4002, Threat: 1, EnemyIDs: []int{3001}},
		{ID: 4001, Threat: 1, EnemyIDs: []int{3002}},
		{ID: 4201, Threat: 3, EnemyIDs: []int{3001}},
	})
	pool := parties.Pool(1)
	if len(pool) != 2 || pool[0].ID != 4002 || pool[1].ID != 4001 {
		t.Fatalf("threat 1 pool = %+v, want [4002 4001]", pool)
	}
	if pool := parties.Pool(0); len(pool) != 0 {
		t.Fatalf("threat 0 pool should be empty, got %+v", pool)
	}
	if party, ok := parties.Get(4201); !ok || party.Threat != 3 {
		t.Fatalf("party 4201 lookup failed: %+v ok=%v", party, ok)
	}

	resources := NewResourceTable([]ResourcePoint{
		{ID: 1, LootConfigs: []LootConfig{{Difficulty: 2, MinCount: 1, MaxCount: 1, WhiteWeight: 1}}},
	})
	point, ok := resources.Get(1)
	if !ok {
		t.Fatal("resource point 1 missing")
	}
	if _, ok := point.LootFor(1); ok {
		t.Fatal("difficulty 1 should miss")
	}
	if _, ok := point.LootFor(2); !ok {
		t.Fatal("difficulty 2 should hit")
	}
}
