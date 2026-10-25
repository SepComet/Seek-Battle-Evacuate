package mapgen

import (
	"encoding/json"
	"reflect"
	"testing"

	"sbe-server/internal/gametable"
	"sbe-server/internal/itemtable"
	"sbe-server/internal/mapdef"
)

// 真实运行时配置：tools/sync_config 从客户端 DataTables 同步到 config 的全套产物。
const (
	realItems        = "../../config/tbitemconfig.json"
	realMap          = "../../config/tbmapdefinition.json"
	realResources    = "../../config/tbresourcepointconfig.json"
	realParties      = "../../config/tbenemypartyconfig.json"
	realDifficulties = "../../config/tbdifficultyconfig.json"
)

// TestGenerateWithRealConfigs 真实配置冒烟：六份真实配置跑完整生成，
// 只断言一致性不变量（不断言具体内容——真实资产会变）。
func TestGenerateWithRealConfigs(t *testing.T) {
	layout, tables := loadRealConfigs(t)
	assertTablesConsistent(t, layout, tables)

	// 本原型三档难度（tbdifficultyconfig 当前为 1~3），每档都要能生成。
	masters := []int64{1, 123456789, 2147483647}
	for difficulty := 1; difficulty <= 3; difficulty++ {
		for _, master := range masters {
			content, err := Generate(master, difficulty, layout, tables)
			if err != nil {
				t.Fatalf("Generate(master=%d, difficulty=%d): %v", master, difficulty, err)
			}
			assertContentInvariants(t, content, layout, tables, difficulty)
		}
	}
}

func loadRealConfigs(t *testing.T) (*mapdef.Layout, *gametable.Tables) {
	t.Helper()
	layout, err := mapdef.Load(realMap)
	if err != nil {
		t.Fatalf("mapdef.Load: %v", err)
	}
	resources, err := gametable.LoadResourcePoints(realResources)
	if err != nil {
		t.Fatalf("gametable.LoadResourcePoints: %v", err)
	}
	parties, err := gametable.LoadEnemyParties(realParties)
	if err != nil {
		t.Fatalf("gametable.LoadEnemyParties: %v", err)
	}
	difficulties, err := gametable.LoadDifficulties(realDifficulties)
	if err != nil {
		t.Fatalf("gametable.LoadDifficulties: %v", err)
	}
	catalog, err := itemtable.LoadCatalog(realItems)
	if err != nil {
		t.Fatalf("itemtable.LoadCatalog: %v", err)
	}
	return layout, &gametable.Tables{
		Resources:    resources,
		Parties:      parties,
		Difficulties: difficulties,
		Items:        catalog,
	}
}

// assertTablesConsistent 真实资产之间的跨表前置条件：地图引用的物资点类型可查、
// 每个难度都有掉落配置与非空威胁权重。
func assertTablesConsistent(t *testing.T, layout *mapdef.Layout, tables *gametable.Tables) {
	t.Helper()
	for difficulty := 1; difficulty <= 3; difficulty++ {
		if _, ok := tables.Difficulties.Weights(difficulty); !ok {
			t.Fatalf("difficulty %d has no threat weights", difficulty)
		}
	}
	for i, point := range layout.ResourcePoints {
		resource, ok := tables.Resources.Get(point.ResourcePointID)
		if !ok {
			t.Fatalf("resource point %d: unknown resourcePointId %d", i, point.ResourcePointID)
		}
		for difficulty := 1; difficulty <= 3; difficulty++ {
			if _, ok := resource.LootFor(difficulty); !ok {
				t.Fatalf("resource point %d: no loot config for difficulty %d", i, difficulty)
			}
		}
	}
}

func assertContentInvariants(t *testing.T, content *Content, layout *mapdef.Layout, tables *gametable.Tables,
	difficulty int) {
	t.Helper()
	if content.Difficulty != difficulty {
		t.Fatalf("content difficulty = %d, want %d", content.Difficulty, difficulty)
	}
	if !containsVec(layout.PlayerSpawnPoints, content.PlayerSpawnPoint) {
		t.Fatalf("spawn point %+v is not one of the layout candidates", content.PlayerSpawnPoint)
	}
	if !containsVec(layout.ExtractionPoints, content.ExtractionPoint) {
		t.Fatalf("extraction point %+v is not one of the layout candidates", content.ExtractionPoint)
	}

	// 物资点：与地图列表同序同长，全部存在，物品都在物品表中且同点不重复。
	if len(content.ResourcePoints) != len(layout.ResourcePoints) {
		t.Fatalf("resource point count = %d, want %d", len(content.ResourcePoints), len(layout.ResourcePoints))
	}
	for i, point := range content.ResourcePoints {
		definition := layout.ResourcePoints[i]
		if point.Index != i || point.PointID != ResourcePointID(i) {
			t.Fatalf("resource point %d: index=%d id=%q", i, point.Index, point.PointID)
		}
		if !point.Present {
			t.Fatalf("resource point %d should always be present", i)
		}
		if point.Position != definition.Position() || point.ResourcePointID != definition.ResourcePointID {
			t.Fatalf("resource point %d: position/resourcePointId drifted from layout", i)
		}
		if len(point.ItemIDs) == 0 {
			t.Fatalf("resource point %d: empty loot", i)
		}
		seen := make(map[int]bool, len(point.ItemIDs))
		for _, itemID := range point.ItemIDs {
			if seen[itemID] {
				t.Fatalf("resource point %d: duplicate item %d within one point", i, itemID)
			}
			seen[itemID] = true
			if _, ok := tables.Items.Get(itemID); !ok {
				t.Fatalf("resource point %d: item %d is not in the item table", i, itemID)
			}
		}
	}

	// 敌点：与地图列表同序同长（不存在的点也占位）；存在的点队伍必须属于对应威胁的池。
	if len(content.EnemyPoints) != len(layout.EnemySpawnPoints) {
		t.Fatalf("enemy point count = %d, want %d", len(content.EnemyPoints), len(layout.EnemySpawnPoints))
	}
	for i, point := range content.EnemyPoints {
		if point.Index != i || point.PointID != EnemyPointID(i) {
			t.Fatalf("enemy point %d: index=%d id=%q", i, point.Index, point.PointID)
		}
		if point.Position != layout.EnemySpawnPoints[i] {
			t.Fatalf("enemy point %d: position drifted from layout", i)
		}
		if point.RoomIndex < 0 || point.RoomIndex >= len(layout.Rooms) {
			t.Fatalf("enemy point %d: room index %d out of range", i, point.RoomIndex)
		}
		if !layout.Rooms[point.RoomIndex].Contains(point.Position) {
			t.Fatalf("enemy point %d: room %d does not contain the point", i, point.RoomIndex)
		}
		if !point.Present {
			if point.Threat != 0 || point.EnemyPartyID != 0 {
				t.Fatalf("enemy point %d: absent point carries threat/party (%d/%d)",
					i, point.Threat, point.EnemyPartyID)
			}
			continue
		}
		if point.Threat <= 0 || point.Threat > 3 {
			t.Fatalf("enemy point %d: threat %d out of range", i, point.Threat)
		}
		party, ok := tables.Parties.Get(point.EnemyPartyID)
		if !ok {
			t.Fatalf("enemy point %d: party %d is not in the party table", i, point.EnemyPartyID)
		}
		if party.Threat != point.Threat {
			t.Fatalf("enemy point %d: party %d has threat %d, generated threat %d",
				i, point.EnemyPartyID, party.Threat, point.Threat)
		}
	}
}

// TestGenerateIsPure 同一 (masterSeed, 配置) 两次生成结果完全相同，且生成不修改入参。
func TestGenerateIsPure(t *testing.T) {
	layout, tables := vectorA().build(t)
	before, err := json.Marshal(layout)
	if err != nil {
		t.Fatalf("marshal layout: %v", err)
	}

	first, err := Generate(123456789, 1, layout, tables)
	if err != nil {
		t.Fatalf("first Generate: %v", err)
	}
	second, err := Generate(123456789, 1, layout, tables)
	if err != nil {
		t.Fatalf("second Generate: %v", err)
	}
	if !reflect.DeepEqual(first, second) {
		t.Fatal("two generations with the same master seed differ")
	}

	after, err := json.Marshal(layout)
	if err != nil {
		t.Fatalf("marshal layout: %v", err)
	}
	if string(before) != string(after) {
		t.Fatal("Generate mutated the layout")
	}
}

// TestGenerateRejectsInvalidInputs 生成器入口自己校验硬性不变量，不做静默兜底
// （契约 §8：导出与生成两处校验）。
func TestGenerateRejectsInvalidInputs(t *testing.T) {
	t.Run("nil layout", func(t *testing.T) {
		_, tables := vectorA().build(t)
		if _, err := Generate(1, 1, nil, tables); err == nil {
			t.Fatal("want error for nil layout")
		}
	})

	t.Run("nil tables", func(t *testing.T) {
		layout, _ := vectorA().build(t)
		if _, err := Generate(1, 1, layout, nil); err == nil {
			t.Fatal("want error for nil tables")
		}
	})

	t.Run("enemy point outside every room", func(t *testing.T) {
		layout, tables := vectorA().build(t)
		layout.EnemySpawnPoints[0] = mapdef.Vec2{X: 500, Y: 500}
		if _, err := Generate(123456789, 1, layout, tables); err == nil {
			t.Fatal("want error for enemy point outside every room")
		}
	})

	t.Run("difficulty without threat config", func(t *testing.T) {
		layout, tables := vectorA().build(t)
		if _, err := Generate(123456789, 2, layout, tables); err == nil {
			t.Fatal("want error for difficulty without config")
		}
	})

	t.Run("empty spawn candidates", func(t *testing.T) {
		layout, tables := vectorA().build(t)
		layout.PlayerSpawnPoints = nil
		if _, err := Generate(123456789, 1, layout, tables); err == nil {
			t.Fatal("want error for empty spawn candidates")
		}
	})

	t.Run("empty extraction candidates", func(t *testing.T) {
		layout, tables := vectorA().build(t)
		layout.ExtractionPoints = nil
		if _, err := Generate(123456789, 1, layout, tables); err == nil {
			t.Fatal("want error for empty extraction candidates")
		}
	})

	t.Run("missing resource point config", func(t *testing.T) {
		layout, tables := vectorA().build(t)
		layout.ResourcePoints[0].ResourcePointID = 99
		if _, err := Generate(123456789, 1, layout, tables); err == nil {
			t.Fatal("want error for unknown resource point config")
		}
	})

	t.Run("missing loot config for difficulty", func(t *testing.T) {
		input := vectorA()
		for i := range input.loot {
			if input.loot[i].resourcePointID == 1 {
				input.loot[i].difficulty = 2
			}
		}
		layout, tables := input.build(t)
		if _, err := Generate(123456789, 1, layout, tables); err == nil {
			t.Fatal("want error for missing loot config")
		}
	})

	t.Run("zero rarity weights", func(t *testing.T) {
		input := vectorA()
		for i := range input.loot {
			if input.loot[i].resourcePointID == 1 {
				input.loot[i].white, input.loot[i].green, input.loot[i].blue = 0, 0, 0
				input.loot[i].gold, input.loot[i].red = 0, 0
			}
		}
		layout, tables := input.build(t)
		if _, err := Generate(123456789, 1, layout, tables); err == nil {
			t.Fatal("want error for all-zero rarity weights")
		}
	})

	t.Run("empty rarity bucket", func(t *testing.T) {
		// 物品表里没有任何 Red 物品，而配置把 100% 权重压到 Red。
		input := vectorA()
		input.items = []itemSpec{{5001, 1}, {5002, 1}, {5003, 2}, {5004, 3}, {5005, 4}}
		input.loot = []lootSpec{
			{resourcePointID: 1, minCount: 1, maxCount: 1, red: 100},
			{resourcePointID: 2, minCount: 1, maxCount: 1, white: 100},
		}
		layout, tables := input.build(t)
		if _, err := Generate(123456789, 1, layout, tables); err == nil {
			t.Fatal("want error for empty rarity bucket")
		}
	})

	t.Run("zero threat weights", func(t *testing.T) {
		input := vectorA()
		for i := range input.threatWeights {
			input.threatWeights[i].Weight = 0
		}
		layout, tables := input.build(t)
		if _, err := Generate(123456789, 1, layout, tables); err == nil {
			t.Fatal("want error for all-zero threat weights")
		}
	})

	t.Run("empty party pool for rolled threat", func(t *testing.T) {
		// 威胁权重只保留 High，队伍表里没有任何 High 队伍。
		input := vectorA()
		input.threatWeights = []gametable.ThreatWeight{{Threat: 3, Weight: 1}}
		var lowOnly []partySpec
		for _, party := range input.parties {
			if party.threat != 3 {
				lowOnly = append(lowOnly, party)
			}
		}
		input.parties = lowOnly
		layout, tables := input.build(t)
		if _, err := Generate(123456789, 1, layout, tables); err == nil {
			t.Fatal("want error for empty party pool")
		}
	})
}

// TestPointIDs 交互主键构造（契约 §6）。
func TestPointIDs(t *testing.T) {
	if got := ResourcePointID(0); got != "r0" {
		t.Fatalf("ResourcePointID(0) = %q, want r0", got)
	}
	if got := ResourcePointID(12); got != "r12" {
		t.Fatalf("ResourcePointID(12) = %q, want r12", got)
	}
	if got := EnemyPointID(3); got != "e3" {
		t.Fatalf("EnemyPointID(3) = %q, want e3", got)
	}
}

func containsVec(candidates []mapdef.Vec2, point mapdef.Vec2) bool {
	for _, candidate := range candidates {
		if candidate == point {
			return true
		}
	}
	return false
}
