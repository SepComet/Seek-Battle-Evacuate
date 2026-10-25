package mapgen

import (
	"fmt"
	"strconv"
	"strings"
	"testing"

	"sbe-server/internal/gametable"
	"sbe-server/internal/itemtable"
	"sbe-server/internal/mapdef"
)

// 本文件的输入与期望输出文本逐字复制自
// D:\UnityProjects\.sbe-worktrees\exploration-online-v1\notes\vectors.md
// （ticket 02 产出；C# 侧 MapBuilderTests 内嵌同一批常量，契约 §11 要求两端逐字一致）。
// 向量里的地图/配置是测试专用数据，与 config/ 下的真实资产无关；
// 真实配置的一致性不变量见 mapgen_test.go 的 TestGenerateWithRealConfigs。

// ------------------------------------------------------------ PRNG 与子种子

// TestSplitMix64KnownValues 契约 §1 的 SplitMix64 已知值（vectors.md §0）。
func TestSplitMix64KnownValues(t *testing.T) {
	cases := []struct {
		seed   uint64
		values []string
	}{
		{0, []string{"E220A8397B1DCDAF", "6E789E6AA1B965F4", "06C45D188009454F", "F88BB8A8724C81EC", "1B39896A51A8749B"}},
		{1, []string{"910A2DEC89025CC1", "BEEB8DA1658EEC67", "F893A2EEFB32555E"}},
		{18446744073709551615, []string{"E4D971771B652C20", "E99FF867DBF682C9"}},
	}
	for _, c := range cases {
		rng := NewSplitMix64(c.seed)
		for i, want := range c.values {
			if got := fmt.Sprintf("%016X", rng.Next()); got != want {
				t.Fatalf("SplitMix64(%d).Next() #%d = %s, want %s", c.seed, i+1, got, want)
			}
		}
	}
}

// TestHash64KnownValues 契约 §1 的无状态 Hash64（vectors.md §0）。
func TestHash64KnownValues(t *testing.T) {
	cases := []struct {
		value uint64
		want  string
	}{
		{0, "E220A8397B1DCDAF"},
		{1, "910A2DEC89025CC1"},
		{42, "BDD732262FEB6E95"},
	}
	for _, c := range cases {
		if got := fmt.Sprintf("%016X", Hash64(c.value)); got != c.want {
			t.Fatalf("Hash64(%d) = %s, want %s", c.value, got, c.want)
		}
	}
}

// TestNextIntKnownValues 契约 §1 的 NextInt 取模行为（vectors.md §0）。
func TestNextIntKnownValues(t *testing.T) {
	cases := []struct {
		seed uint64
		min  int
		max  int
		want int
	}{
		{0, 0, 1, 0},
		{0, 0, 2, 1},
		{0, 0, 10, 5},
		{0, 5, 6, 5},
		{0, 100, 101, 100},
		{0, 7, 9, 8},
		{1, 7, 9, 8},
	}
	for _, c := range cases {
		if got := NewSplitMix64(c.seed).NextInt(c.min, c.max); got != c.want {
			t.Fatalf("SplitMix64(%d).NextInt(%d,%d) = %d, want %d", c.seed, c.min, c.max, got, c.want)
		}
	}

	// 跨度 4294967295 超出 int32 表示范围：跨度必须按 64 位算（C# 侧同样处理）。
	wideCases := []struct {
		seed uint64
		want int
	}{
		{0, -583109143},
		{42, 1841471675},
		{123456789, -534254766},
	}
	for _, c := range wideCases {
		if got := NewSplitMix64(c.seed).NextInt(-2147483648, 2147483647); got != c.want {
			t.Fatalf("SplitMix64(%d).NextInt(int.MinValue,int.MaxValue) = %d, want %d", c.seed, got, c.want)
		}
	}
}

// TestSubSeedKnownValues 契约 §2 的子种子派生（vectors.md §0）。
func TestSubSeedKnownValues(t *testing.T) {
	cases := []struct {
		master int64
		kind   int
		index  int
		want   string
	}{
		{123456789, KindResource, 0, "BFB3F7294C7F2779"},
		{123456789, KindResource, 1, "7B9819FCFAD60401"},
		{123456789, KindResource, 2, "202A6C971A705BEE"},
		{123456789, KindEnemy, 0, "41E75059FD28538E"},
		{123456789, KindEnemy, 3, "9AE1B3F82DAA67F8"},
		{123456789, KindExtraction, 0, "15884FDE4C369CDD"},
		{123456789, KindSpawn, 0, "FE1C3CE9428F5C39"},
		{42, KindEnemy, 0, "0684A9E565657C2E"},
		{2147483647, KindEnemy, 2, "075D2F9B21F9014D"},
	}
	for _, c := range cases {
		if got := fmt.Sprintf("%016X", SubSeed(c.master, c.kind, c.index)); got != c.want {
			t.Fatalf("SubSeed(%d,%d,%d) = %s, want %s", c.master, c.kind, c.index, got, c.want)
		}
	}
}

// ------------------------------------------------------------ 种子向量

// TestSeedVectorsFullBuild 四组向量的完整生成文本（vectors.md §1~§4）。
func TestSeedVectorsFullBuild(t *testing.T) {
	for _, input := range seedVectors() {
		t.Run(input.name, func(t *testing.T) {
			content := generateVector(t, input)
			assertLines(t, formatFull(content), input.expectedFull)
		})
	}
}

// TestSeedVectorsPlacementIsFullProjection 摆位文本 = 完整文本的投影
// （截掉 items= / party=），且与 fixture 的摆位期望逐字一致（vectors.md §5）。
func TestSeedVectorsPlacementIsFullProjection(t *testing.T) {
	for _, input := range seedVectors() {
		t.Run(input.name, func(t *testing.T) {
			full := formatFull(generateVector(t, input))
			assertLines(t, projectPlacement(full), input.expectedPlacement)
			// fixture 的两侧常量自身也必须满足投影关系。
			assertLines(t, projectPlacement(input.expectedFull), input.expectedPlacement)
		})
	}
}

// TestSubSeedIsolation_ResourceConfigChangeLeavesOtherStagesUntouched
// 向量 D 与 A 只差物资配置：R 行必须变，E 行与 spawn/extraction 必须逐字相同（vectors.md §4）。
func TestSubSeedIsolation_ResourceConfigChangeLeavesOtherStagesUntouched(t *testing.T) {
	baseline := formatFull(generateVector(t, vectorA()))
	changed := formatFull(generateVector(t, vectorD()))

	assertLinesDifferent(t, filterLines(baseline, "R"), filterLines(changed, "R"))
	assertLines(t, filterLines(changed, "E"), filterLines(baseline, "E"))
	assertLines(t, changed[:2], baseline[:2])
}

// TestDifferentMasterSeed_ProducesDifferentMap vectors.md §6 的排查向量：
// masterSeed+3（其余输入同向量 A）产出另一张图。
func TestDifferentMasterSeed_ProducesDifferentMap(t *testing.T) {
	input := vectorA()
	input.masterSeed += 3
	input.expectedFull = []string{
		"spawn=5,-16",
		"extraction=28,-8",
		"R0 present=true items=5003",
		"R1 present=true items=5004,5005",
		"R2 present=true items=5003",
		"E0 present=true threat=3 party=4201",
		"E1 present=true threat=2 party=4101",
		"E2 present=true threat=1 party=4002",
		"E3 present=false",
	}
	assertLines(t, formatFull(generateVector(t, input)), input.expectedFull)
}

// ------------------------------------------------------------ 向量数据

func seedVectors() []vectorInput {
	return []vectorInput{vectorA(), vectorB(), vectorC(), vectorD()}
}

func generateVector(t *testing.T, input vectorInput) *Content {
	t.Helper()
	layout, tables := input.build(t)
	content, err := Generate(input.masterSeed, 1, layout, tables)
	if err != nil {
		t.Fatalf("%s: Generate: %v", input.name, err)
	}
	return content
}

type lootSpec struct {
	resourcePointID               int
	difficulty                    int // 0 视为 1（向量只涉及难度 1）
	minCount                      int
	maxCount                      int
	white, green, blue, gold, red int
}

type itemSpec struct{ id, rarity int }

type partySpec struct{ id, threat int }

type vectorInput struct {
	name              string
	masterSeed        int64
	spawnPoints       []mapdef.Vec2
	extractionPoints  []mapdef.Vec2
	resourcePoints    []mapdef.ResourcePoint
	enemyPoints       []mapdef.Vec2
	threatWeights     []gametable.ThreatWeight
	loot              []lootSpec
	items             []itemSpec
	parties           []partySpec
	expectedFull      []string
	expectedPlacement []string
}

// build 把向量输入装配成 Generate 的入参。
// 向量只描述点位、不描述房间；房间归属不变量需要一个覆盖全部点位的矩形，
// 这里用一个足够大的普通房间。房间不参与任何随机消费，不影响向量输出。
func (v vectorInput) build(t *testing.T) (*mapdef.Layout, *gametable.Tables) {
	t.Helper()
	layout := &mapdef.Layout{
		Rooms:             []mapdef.Room{{Type: 0, MinX: -100, MinY: -100, MaxX: 100, MaxY: 100}},
		ResourcePoints:    v.resourcePoints,
		EnemySpawnPoints:  v.enemyPoints,
		PlayerSpawnPoints: v.spawnPoints,
		ExtractionPoints:  v.extractionPoints,
	}
	entries := make([]itemtable.CatalogEntry, len(v.items))
	for i, item := range v.items {
		entries[i] = itemtable.CatalogEntry{ID: item.id, Rarity: item.rarity, StackLimit: 1}
	}
	catalog, err := itemtable.NewCatalog(entries)
	if err != nil {
		t.Fatalf("NewCatalog: %v", err)
	}
	return layout, &gametable.Tables{
		Resources:    gametable.NewResourceTable(buildResourcePoints(v.loot)),
		Parties:      gametable.NewEnemyPartyTable(buildParties(v.parties)),
		Difficulties: gametable.NewDifficultyTable([]gametable.Difficulty{{Tier: 1, Weights: v.threatWeights}}),
		Items:        catalog,
	}
}

// buildResourcePoints 按首次出现顺序聚合各物资点类型的多档掉落配置。
func buildResourcePoints(specs []lootSpec) []gametable.ResourcePoint {
	var points []gametable.ResourcePoint
	indexByID := make(map[int]int)
	for _, spec := range specs {
		index, ok := indexByID[spec.resourcePointID]
		if !ok {
			index = len(points)
			indexByID[spec.resourcePointID] = index
			points = append(points, gametable.ResourcePoint{ID: spec.resourcePointID})
		}
		difficulty := spec.difficulty
		if difficulty == 0 {
			difficulty = 1
		}
		points[index].LootConfigs = append(points[index].LootConfigs, gametable.LootConfig{
			Difficulty:  difficulty,
			MinCount:    spec.minCount,
			MaxCount:    spec.maxCount,
			WhiteWeight: spec.white,
			GreenWeight: spec.green,
			BlueWeight:  spec.blue,
			GoldWeight:  spec.gold,
			RedWeight:   spec.red,
		})
	}
	return points
}

func buildParties(specs []partySpec) []gametable.EnemyParty {
	parties := make([]gametable.EnemyParty, len(specs))
	for i, spec := range specs {
		parties[i] = gametable.EnemyParty{ID: spec.id, Threat: spec.threat, EnemyIDs: []int{3001}}
	}
	return parties
}

// vectorA：masterSeed=123456789，三物资点 + 四敌点，威胁权重含 None（vectors.md §1）。
func vectorA() vectorInput {
	return vectorInput{
		name:       "A",
		masterSeed: 123456789,
		spawnPoints: []mapdef.Vec2{
			{X: 1, Y: -16}, {X: 5, Y: -16}, {X: 9, Y: -20},
		},
		extractionPoints: []mapdef.Vec2{{X: 30, Y: -3}, {X: 28, Y: -8}},
		resourcePoints: []mapdef.ResourcePoint{
			{X: -4, Y: 5, ResourcePointID: 1},
			{X: 2, Y: 7, ResourcePointID: 2},
			{X: 6, Y: -3, ResourcePointID: 1},
		},
		enemyPoints: []mapdef.Vec2{
			{X: -19, Y: 1}, {X: -14, Y: 4}, {X: 3, Y: 9}, {X: 12, Y: -2},
		},
		threatWeights: []gametable.ThreatWeight{
			{Threat: 0, Weight: 3},
			{Threat: 1, Weight: 4},
			{Threat: 2, Weight: 2},
			{Threat: 3, Weight: 1},
		},
		loot: []lootSpec{
			{resourcePointID: 1, minCount: 1, maxCount: 3, white: 60, green: 30, blue: 10, gold: 0, red: 0},
			{resourcePointID: 2, minCount: 2, maxCount: 2, white: 0, green: 0, blue: 50, gold: 50, red: 0},
		},
		items: []itemSpec{
			{5001, 1}, {5002, 1}, {5003, 2}, {5004, 3}, {5005, 4}, {5006, 5},
		},
		parties: []partySpec{
			{4001, 1}, {4002, 1}, {4101, 2}, {4201, 3},
		},
		expectedFull: []string{
			"spawn=1,-16",
			"extraction=30,-3",
			"R0 present=true items=5004,5002,5003",
			"R1 present=true items=5004,5005",
			"R2 present=true items=5001",
			"E0 present=true threat=1 party=4001",
			"E1 present=true threat=3 party=4201",
			"E2 present=true threat=1 party=4001",
			"E3 present=true threat=3 party=4201",
		},
		expectedPlacement: []string{
			"spawn=1,-16",
			"extraction=30,-3",
			"R0 present=true",
			"R1 present=true",
			"R2 present=true",
			"E0 present=true threat=1",
			"E1 present=true threat=3",
			"E2 present=true threat=1",
			"E3 present=true threat=3",
		},
	}
}

// vectorB：masterSeed=42，含多点 threat=None 与多稀有度掉落（vectors.md §2）。
func vectorB() vectorInput {
	return vectorInput{
		name:       "B",
		masterSeed: 42,
		spawnPoints: []mapdef.Vec2{
			{X: 1, Y: -16}, {X: 5, Y: -16},
		},
		extractionPoints: []mapdef.Vec2{
			{X: 30, Y: -3}, {X: 28, Y: -8}, {X: 26, Y: -12},
		},
		resourcePoints: []mapdef.ResourcePoint{
			{X: 10, Y: 2, ResourcePointID: 3},
			{X: -7, Y: -4, ResourcePointID: 4},
		},
		enemyPoints: []mapdef.Vec2{
			{X: -19, Y: 1}, {X: -14, Y: 4}, {X: 3, Y: 9}, {X: 12, Y: -2}, {X: 7, Y: 7},
		},
		threatWeights: []gametable.ThreatWeight{
			{Threat: 0, Weight: 3},
			{Threat: 1, Weight: 3},
			{Threat: 2, Weight: 4},
		},
		loot: []lootSpec{
			{resourcePointID: 3, minCount: 1, maxCount: 4, white: 20, green: 20, blue: 20, gold: 20, red: 20},
			{resourcePointID: 4, minCount: 3, maxCount: 3, white: 0, green: 0, blue: 0, gold: 0, red: 100},
		},
		items: []itemSpec{
			{5001, 1}, {5002, 2}, {5003, 3}, {5004, 4}, {5005, 5}, {5006, 5}, {5007, 5},
		},
		parties: []partySpec{
			{4001, 1}, {4101, 2}, {4201, 2}, {4301, 3},
		},
		expectedFull: []string{
			"spawn=5,-16",
			"extraction=30,-3",
			"R0 present=true items=5002",
			"R1 present=true items=5006,5007,5005",
			"E0 present=true threat=1 party=4001",
			"E1 present=false",
			"E2 present=true threat=2 party=4201",
			"E3 present=true threat=1 party=4001",
			"E4 present=false",
		},
		expectedPlacement: []string{
			"spawn=5,-16",
			"extraction=30,-3",
			"R0 present=true",
			"R1 present=true",
			"E0 present=true threat=1",
			"E1 present=false",
			"E2 present=true threat=2",
			"E3 present=true threat=1",
			"E4 present=false",
		},
	}
}

// vectorC：masterSeed=2147483647（契约 §5 取值上界），单候选出生/撤离、多 Threat=None（vectors.md §3）。
func vectorC() vectorInput {
	return vectorInput{
		name:             "C",
		masterSeed:       2147483647,
		spawnPoints:      []mapdef.Vec2{{X: 3, Y: -14}},
		extractionPoints: []mapdef.Vec2{{X: 30, Y: -3}},
		resourcePoints:   []mapdef.ResourcePoint{{X: 0, Y: 0, ResourcePointID: 5}},
		enemyPoints: []mapdef.Vec2{
			{X: -19, Y: 1}, {X: -14, Y: 4}, {X: 3, Y: 9},
		},
		threatWeights: []gametable.ThreatWeight{
			{Threat: 0, Weight: 1},
			{Threat: 1, Weight: 1},
			{Threat: 3, Weight: 2},
		},
		loot: []lootSpec{
			{resourcePointID: 5, minCount: 2, maxCount: 2, white: 100, green: 0, blue: 0, gold: 0, red: 0},
		},
		items: []itemSpec{
			{5101, 1}, {5102, 1}, {5103, 1},
		},
		parties: []partySpec{
			{4001, 1}, {4201, 3}, {4301, 3},
		},
		expectedFull: []string{
			"spawn=3,-14",
			"extraction=30,-3",
			"R0 present=true items=5101,5102",
			"E0 present=false",
			"E1 present=true threat=1 party=4001",
			"E2 present=true threat=3 party=4201",
		},
		expectedPlacement: []string{
			"spawn=3,-14",
			"extraction=30,-3",
			"R0 present=true",
			"E0 present=false",
			"E1 present=true threat=1",
			"E2 present=true threat=3",
		},
	}
}

// vectorD：与 A 只差物资配置（权重与数量），用于子种子隔离断言（vectors.md §4）。
func vectorD() vectorInput {
	input := vectorA()
	input.name = "D"
	input.loot = []lootSpec{
		{resourcePointID: 1, minCount: 1, maxCount: 2, white: 100, green: 0, blue: 0, gold: 0, red: 0},
		{resourcePointID: 2, minCount: 1, maxCount: 1, white: 0, green: 100, blue: 0, gold: 0, red: 0},
	}
	input.expectedFull = []string{
		"spawn=1,-16",
		"extraction=30,-3",
		"R0 present=true items=5001,5002",
		"R1 present=true items=5003",
		"R2 present=true items=5001,5002",
		"E0 present=true threat=1 party=4001",
		"E1 present=true threat=3 party=4201",
		"E2 present=true threat=1 party=4001",
		"E3 present=true threat=3 party=4201",
	}
	input.expectedPlacement = []string{
		"spawn=1,-16",
		"extraction=30,-3",
		"R0 present=true",
		"R1 present=true",
		"R2 present=true",
		"E0 present=true threat=1",
		"E1 present=true threat=3",
		"E2 present=true threat=1",
		"E3 present=true threat=3",
	}
	return input
}

// ------------------------------------------------------------ 文本格式化

// formatFull 完整生成文本（契约 §11）：spawn/extraction 两行 + R 行（含 items）+ E 行（全量）。
func formatFull(content *Content) []string {
	lines := []string{
		"spawn=" + formatPoint(content.PlayerSpawnPoint),
		"extraction=" + formatPoint(content.ExtractionPoint),
	}
	for i := range content.ResourcePoints {
		point := content.ResourcePoints[i]
		lines = append(lines, fmt.Sprintf("R%d present=%t items=%s", i, point.Present, formatInts(point.ItemIDs)))
	}
	for i := range content.EnemyPoints {
		point := content.EnemyPoints[i]
		if !point.Present {
			lines = append(lines, fmt.Sprintf("E%d present=false", i))
			continue
		}
		lines = append(lines, fmt.Sprintf("E%d present=true threat=%d party=%d", i, point.Threat, point.EnemyPartyID))
	}
	return lines
}

// projectPlacement 把完整文本投影成摆位文本：截掉 items= 与 party= 两段，其余逐字保留。
func projectPlacement(fullLines []string) []string {
	lines := make([]string, len(fullLines))
	for i, line := range fullLines {
		if index := strings.Index(line, " items="); index >= 0 {
			lines[i] = line[:index]
			continue
		}
		if index := strings.Index(line, " party="); index >= 0 {
			lines[i] = line[:index]
			continue
		}
		lines[i] = line
	}
	return lines
}

func formatPoint(point mapdef.Vec2) string {
	return formatCoord(point.X) + "," + formatCoord(point.Y)
}

// formatCoord 最短十进制表示；向量坐标一律整数，与 C# ToString("R") 对整数求值一致。
func formatCoord(value float64) string {
	return strconv.FormatFloat(value, 'f', -1, 64)
}

func formatInts(values []int) string {
	parts := make([]string, len(values))
	for i, value := range values {
		parts[i] = strconv.Itoa(value)
	}
	return strings.Join(parts, ",")
}

func filterLines(lines []string, prefix string) []string {
	var filtered []string
	for _, line := range lines {
		if strings.HasPrefix(line, prefix) {
			filtered = append(filtered, line)
		}
	}
	return filtered
}

func assertLines(t *testing.T, got, want []string) {
	t.Helper()
	if len(got) != len(want) {
		t.Fatalf("line count = %d, want %d\ngot:\n%s\nwant:\n%s",
			len(got), len(want), strings.Join(got, "\n"), strings.Join(want, "\n"))
	}
	for i := range want {
		if got[i] != want[i] {
			t.Fatalf("line %d = %q, want %q\ngot:\n%s\nwant:\n%s",
				i, got[i], want[i], strings.Join(got, "\n"), strings.Join(want, "\n"))
		}
	}
}

func assertLinesDifferent(t *testing.T, got, other []string) {
	t.Helper()
	if len(got) == len(other) {
		same := true
		for i := range got {
			if got[i] != other[i] {
				same = false
				break
			}
		}
		if same {
			t.Fatalf("lines should differ but are identical:\n%s", strings.Join(got, "\n"))
		}
	}
}
