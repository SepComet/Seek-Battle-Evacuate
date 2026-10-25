package itemtable

import (
	"os"
	"path/filepath"
	"testing"

	"sbe-server/internal/core"
)

const fixtureJSON = `[
  {"id": 5001, "rarity": 1, "stackLimit": 99, "equipSlot": 0},
  {"id": 5002, "rarity": 1, "stackLimit": 60, "equipSlot": 0},
  {"id": 5101, "rarity": 3, "stackLimit": 1, "equipSlot": 1},
  {"id": 5201, "rarity": 5, "stackLimit": 1, "equipSlot": 2}
]`

// 真实导出产物：由 gen_cli 导出并经 tools/sync_config 同步到 config。
const realItemPath = "../../config/tbitemconfig.json"

func writeFixture(t *testing.T) string {
	t.Helper()
	dir := t.TempDir()
	path := filepath.Join(dir, "tbitemconfig.json")
	if err := os.WriteFile(path, []byte(fixtureJSON), 0o644); err != nil {
		t.Fatalf("write fixture: %v", err)
	}
	return path
}

func writeJSON(t *testing.T, content string) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "tbitemconfig.json")
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatalf("write fixture: %v", err)
	}
	return path
}

func TestLoadReadsLubanExport(t *testing.T) {
	table, err := Load(writeFixture(t))
	if err != nil {
		t.Fatalf("Load returned error: %v", err)
	}
	def, ok := table.Get(5101)
	if !ok || def.StackLimit != 1 || def.EquipSlot != 1 || def.Rarity != core.RarityBlue {
		t.Fatalf("item 5101 wrong: %+v ok=%v", def, ok)
	}
	if _, ok := table.Get(404); ok {
		t.Fatal("unknown item should miss")
	}
}

func TestLoadCatalogBuildsRarityPools(t *testing.T) {
	catalog, err := LoadCatalog(writeFixture(t))
	if err != nil {
		t.Fatalf("LoadCatalog returned error: %v", err)
	}
	// 桶内顺序 = JSON 数组序（配置表序），生成器取模依赖它
	if got := catalog.RarityPool(core.RarityWhite); !equalInts(got, []int{5001, 5002}) {
		t.Fatalf("white pool = %v, want [5001 5002]", got)
	}
	if got := catalog.RarityPool(core.RarityBlue); !equalInts(got, []int{5101}) {
		t.Fatalf("blue pool = %v, want [5101]", got)
	}
	if got := catalog.RarityPool(core.RarityRed); !equalInts(got, []int{5201}) {
		t.Fatalf("red pool = %v, want [5201]", got)
	}
	if got := catalog.RarityPool(core.RarityGreen); len(got) != 0 {
		t.Fatalf("green pool should be empty, got %v", got)
	}
}

func TestLoadCatalogRepositoryExport(t *testing.T) {
	catalog, err := LoadCatalog(realItemPath)
	if err != nil {
		t.Fatalf("LoadCatalog(%s): %v", realItemPath, err)
	}
	// 真实物品表 83 条，五档稀有度都必须有可用物品（否则掉落加权选择会取空桶报错）
	total := 0
	for _, rarity := range core.RarityOrder {
		pool := catalog.RarityPool(rarity)
		if len(pool) == 0 {
			t.Fatalf("rarity %d pool is empty", rarity)
		}
		total += len(pool)
	}
	if total != len(catalog.Table) {
		t.Fatalf("rarity pools cover %d items, table has %d", total, len(catalog.Table))
	}
	def, ok := catalog.Get(5003)
	if !ok || def.Rarity != core.RarityBlue {
		t.Fatalf("item 5003 wrong: %+v ok=%v", def, ok)
	}
}

func TestLoadRejectsDuplicateID(t *testing.T) {
	path := writeJSON(t, `[{"id":5001,"rarity":1,"stackLimit":1},{"id":5001,"rarity":1,"stackLimit":2}]`)
	if _, err := Load(path); err == nil {
		t.Fatal("want error for duplicate id")
	}
}

func TestLoadRejectsInvalidRarity(t *testing.T) {
	cases := []struct{ name, json string }{
		{"missing rarity", `[{"id":5001,"stackLimit":1}]`},
		{"rarity zero", `[{"id":5001,"rarity":0,"stackLimit":1}]`},
		{"rarity out of range", `[{"id":5001,"rarity":6,"stackLimit":1}]`},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			if _, err := Load(writeJSON(t, c.json)); err == nil {
				t.Fatal("want error for invalid rarity")
			}
		})
	}
}

func TestLoadMissingFileReturnsError(t *testing.T) {
	if _, err := Load(filepath.Join(t.TempDir(), "nope.json")); err == nil {
		t.Fatal("want error for missing file")
	}
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
