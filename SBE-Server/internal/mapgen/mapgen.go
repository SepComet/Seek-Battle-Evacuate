// Package mapgen 生成一局探索的完整内容：物资点掉落清单、敌点（存在性/威胁/队伍）、
// 出生点与撤离点。算法与 C# 侧逐位同构（同一套子种子结构与消费序），
// 由 internal/mapgen/vectors_test.go 的种子向量锁死。
//
// 生成是纯函数：配置进、结果出，不碰 IO、不持有状态、不修改入参，
// 因此会话层（ticket 04）可以持有一份结果并在重连时按点回放。
//
// 契约来源：exploration-online-v1 的 notes/contracts.md
// §1（SplitMix64）、§2（SubSeed）、§3（生成顺序与消费序）、§6（点位 ID）。
package mapgen

import (
	"fmt"

	"sbe-server/internal/core"
	"sbe-server/internal/gametable"
	"sbe-server/internal/mapdef"
)

// ThreatNone 敌点威胁枚举的零值：掷到它表示该点本局不生成（契约 §3 第 2 段）。
const ThreatNone = 0

// Content 一局探索的完整内容。
// 两类点位列表都与地图定义列表同序同长（越界即视为不存在），
// 不存在的敌点也在列表内（Present=false），与客户端 MapPlacement 的形状一致。
type Content struct {
	Difficulty       int
	PlayerSpawnPoint mapdef.Vec2
	ExtractionPoint  mapdef.Vec2
	ResourcePoints   []ResourcePointContent
	EnemyPoints      []EnemyPointContent
}

// ResourcePointContent 一个物资点：位置、类型 id 与掉落清单。
// 本阶段物资点每局必刷，Present 保留给未来的箱子存在性权重。
type ResourcePointContent struct {
	// PointID 交互主键 "r<i>"，i 为地图定义中的列表下标（契约 §6）。
	PointID         string
	Index           int
	Position        mapdef.Vec2
	ResourcePointID int
	Present         bool
	// ItemIDs 掉落顺序即清单顺序；同一物资点内不重复，跨点允许重复。
	ItemIDs []int
}

// EnemyPointContent 一个敌点：位置、存在性、威胁等级与队伍。
type EnemyPointContent struct {
	// PointID 交互主键 "e<i>"，i 为地图定义中的列表下标（契约 §6）。
	PointID  string
	Index    int
	Position mapdef.Vec2
	// RoomIndex 所属房间的地图定义列表下标（>=0）；-1 表示不属于任何房间，
	// 生成器入口会拒绝这种布局（房间归属是硬性不变量，契约 §8）。
	RoomIndex int
	Present   bool
	// Threat 威胁等级枚举：1=Low 2=Middle 3=High；不存在的点为 ThreatNone(0)。
	Threat int
	// EnemyPartyID 敌队伍 id；不存在的点为 0。
	EnemyPartyID int
}

// ResourcePointID 构造物资点交互主键（契约 §6）。
func ResourcePointID(index int) string {
	return fmt.Sprintf("r%d", index)
}

// EnemyPointID 构造敌点交互主键（契约 §6）。
func EnemyPointID(index int) string {
	return fmt.Sprintf("e%d", index)
}

// rarityCount 稀有度档数（White..Red）。
const rarityCount = gametable.RarityCount

// Generate 生成一局完整内容。
//
// 纯函数：只读入参，不持有跨调用状态；同一 (master, difficulty, layout, tables)
// 必然产出同一结果。任何配置缺失或不可用都返回 error，不做静默兜底。
//
// 消费序（契约 §3）：物资点 i 用 SubSeed(master, KindResource, i) 起独立 PRNG，
// 先掷数量再循环（稀有度 → 桶内下标）；敌点 i 用 SubSeed(master, KindEnemy, i)，
// 先掷威胁、None 则不再消费，否则掷队伍下标；最后撤离（KindExtraction, 0）
// 与出生（KindSpawn, 0）各掷一次下标。
func Generate(master int64, difficulty int, layout *mapdef.Layout, tables *gametable.Tables) (*Content, error) {
	if layout == nil {
		return nil, fmt.Errorf("mapgen: nil layout")
	}
	if tables == nil {
		return nil, fmt.Errorf("mapgen: nil tables")
	}
	if tables.Resources == nil || tables.Parties == nil || tables.Difficulties == nil || tables.Items == nil {
		return nil, fmt.Errorf("mapgen: incomplete tables")
	}
	if err := validateEnemyRooms(layout); err != nil {
		return nil, err
	}
	threatWeights, ok := tables.Difficulties.Weights(difficulty)
	if !ok {
		return nil, fmt.Errorf("mapgen: no enemy party weights for difficulty %d", difficulty)
	}

	resourcePoints, err := generateResourcePoints(master, difficulty, layout, tables)
	if err != nil {
		return nil, err
	}
	enemyPoints, err := generateEnemyPoints(master, layout, tables, threatWeights)
	if err != nil {
		return nil, err
	}
	extractionPoint, err := placePoint(layout.ExtractionPoints, master, KindExtraction, 0,
		"map definition does not contain an extraction point")
	if err != nil {
		return nil, err
	}
	spawnPoint, err := placePoint(layout.PlayerSpawnPoints, master, KindSpawn, 0,
		"map definition does not contain a player spawn point")
	if err != nil {
		return nil, err
	}

	return &Content{
		Difficulty:       difficulty,
		PlayerSpawnPoint: spawnPoint,
		ExtractionPoint:  extractionPoint,
		ResourcePoints:   resourcePoints,
		EnemyPoints:      enemyPoints,
	}, nil
}

// validateEnemyRooms 生成器入口的第二次房间归属校验（契约 §8「两处校验」：
// 编辑器导出处一次、服务器生成处一次）。mapdef.Load 已在加载期校验过，
// 但生成器是可以被直接调用的纯函数，不能依赖调用方是否走过加载器。
func validateEnemyRooms(layout *mapdef.Layout) error {
	if len(layout.Rooms) == 0 {
		return fmt.Errorf("mapgen: no rooms in layout")
	}
	for i, point := range layout.EnemySpawnPoints {
		if layout.RoomIndexAt(point) < 0 {
			return fmt.Errorf("mapgen: enemy spawn point %d (%g,%g) does not belong to any room",
				i, point.X, point.Y)
		}
	}
	return nil
}

// generateResourcePoints 物资点 stage（契约 §3 第 1 段）。
// 每个点从独立子种子起步；点内消费序：数量 →（稀有度 → 桶内下标）× 数量。
func generateResourcePoints(master int64, difficulty int, layout *mapdef.Layout,
	tables *gametable.Tables) ([]ResourcePointContent, error) {
	results := make([]ResourcePointContent, len(layout.ResourcePoints))
	for i, definition := range layout.ResourcePoints {
		resourceConfig, ok := tables.Resources.Get(definition.ResourcePointID)
		if !ok {
			return nil, fmt.Errorf("mapgen: resource point %d: unknown resourcePointId %d",
				i, definition.ResourcePointID)
		}
		loot, ok := resourceConfig.LootFor(difficulty)
		if !ok {
			return nil, fmt.Errorf("mapgen: resource point %d (id %d): no loot config for difficulty %d",
				i, definition.ResourcePointID, difficulty)
		}
		itemIDs, err := rollLoot(master, i, loot, tables.Items)
		if err != nil {
			return nil, fmt.Errorf("mapgen: resource point %d (id %d): %w", i, definition.ResourcePointID, err)
		}
		results[i] = ResourcePointContent{
			PointID:         ResourcePointID(i),
			Index:           i,
			Position:        definition.Position(),
			ResourcePointID: definition.ResourcePointID,
			// 本阶段物资点每局必刷（契约 §4：存在性恒 true）。
			Present: true,
			ItemIDs: itemIDs,
		}
	}
	return results, nil
}

// rollLoot 掷一个物资点内的掉落清单。桶是表数据的副本（同点不重复抽取），
// 桶内顺序 = 物品配置表顺序。
func rollLoot(master int64, index int, loot gametable.LootConfig, items gametable.ItemCatalog) ([]int, error) {
	// 数量区间非法会让 NextInt 的跨度非正（除零），必须先报错。
	if loot.MinCount <= 0 || loot.MaxCount < loot.MinCount {
		return nil, fmt.Errorf("invalid loot count range [%d,%d]", loot.MinCount, loot.MaxCount)
	}

	rng := NewSplitMix64(SubSeed(master, KindResource, index))
	itemCount := rng.NextInt(loot.MinCount, loot.MaxCount+1)

	pools := buildItemPools(items)
	weights := loot.Weights()

	itemIDs := make([]int, 0, itemCount)
	for i := 0; i < itemCount; i++ {
		rarity, err := selectWeighted(rng, weights[:])
		if err != nil {
			return nil, fmt.Errorf("select rarity: %w", err)
		}
		// weights 下标 = 稀有度值 - 1（RarityOrder = White..Red 的连续区间）。
		poolIndex := rarity - core.RarityWhite
		pool := pools[poolIndex]
		if len(pool) == 0 {
			return nil, fmt.Errorf("no item of rarity %d to draw", rarity)
		}
		itemIndex := rng.NextInt(0, len(pool))
		itemIDs = append(itemIDs, pool[itemIndex])
		pools[poolIndex] = append(pool[:itemIndex], pool[itemIndex+1:]...)
	}
	return itemIDs, nil
}

// buildItemPools 按稀有度分桶（桶内顺序 = 物品配置表顺序）。
// 桶每次生成独立重建，避免跨物资点共享可变的抽取状态。
func buildItemPools(items gametable.ItemCatalog) [][]int {
	pools := make([][]int, rarityCount)
	for i := range core.RarityOrder {
		pools[i] = append([]int(nil), items.RarityPool(core.RarityOrder[i])...)
	}
	return pools
}

// generateEnemyPoints 敌点 stage（契约 §3 第 2 段）。
// 消费序：威胁 →（非 None 时）队伍池下标。威胁为 None 时不再消费随机。
func generateEnemyPoints(master int64, layout *mapdef.Layout, tables *gametable.Tables,
	threatWeights []gametable.ThreatWeight) ([]EnemyPointContent, error) {
	results := make([]EnemyPointContent, len(layout.EnemySpawnPoints))
	for i, position := range layout.EnemySpawnPoints {
		rng := NewSplitMix64(SubSeed(master, KindEnemy, i))
		threat, err := selectThreat(rng, threatWeights)
		if err != nil {
			return nil, fmt.Errorf("mapgen: enemy point %d: %w", i, err)
		}

		point := EnemyPointContent{
			PointID:   EnemyPointID(i),
			Index:     i,
			Position:  position,
			RoomIndex: layout.RoomIndexAt(position),
			Present:   threat != ThreatNone,
			Threat:    threat,
		}
		if threat != ThreatNone {
			pool := tables.Parties.Pool(threat)
			if len(pool) == 0 {
				return nil, fmt.Errorf("mapgen: enemy point %d: no enemy party for threat %d", i, threat)
			}
			point.EnemyPartyID = pool[rng.NextInt(0, len(pool))].ID
		}
		results[i] = point
	}
	return results, nil
}

// selectThreat 按配置顺序加权选择威胁等级（契约 §3 的 SelectWeighted）：
// 总权重 <= 0 视为配置错误，走完未命中同样报错。
func selectThreat(rng *SplitMix64, weights []gametable.ThreatWeight) (int, error) {
	total := 0
	for _, weight := range weights {
		if weight.Weight < 0 {
			return 0, fmt.Errorf("negative threat weight for threat %d", weight.Threat)
		}
		total += weight.Weight
	}
	if total <= 0 {
		return 0, fmt.Errorf("enemy threat weights are all zero")
	}

	value := rng.NextInt(0, total)
	for _, weight := range weights {
		if value < weight.Weight {
			return weight.Threat, nil
		}
		value -= weight.Weight
	}
	return 0, fmt.Errorf("enemy threat selection failed")
}

// selectWeighted 按 [White..Red] 顺序加权选择稀有度，返回稀有度枚举值。
// 与 selectThreat 同一算法，只是权重顺序由 RarityOrder 固定、失败信息不同。
func selectWeighted(rng *SplitMix64, weights []int) (int, error) {
	total := 0
	for _, weight := range weights {
		if weight < 0 {
			return 0, fmt.Errorf("negative rarity weight")
		}
		total += weight
	}
	if total <= 0 {
		return 0, fmt.Errorf("rarity weights are all zero")
	}

	value := rng.NextInt(0, total)
	for i, weight := range weights {
		if value < weight {
			return core.RarityOrder[i], nil
		}
		value -= weight
	}
	return 0, fmt.Errorf("rarity selection failed")
}

// placePoint 出生/撤离点的唯一摆位路径：固定 index=0 的子种子，只消费一次下标随机
// （契约 §3 第 3、4 段）。
func placePoint(candidates []mapdef.Vec2, master int64, kind, index int, emptyMessage string) (mapdef.Vec2, error) {
	if len(candidates) == 0 {
		return mapdef.Vec2{}, fmt.Errorf("mapgen: %s", emptyMessage)
	}
	rng := NewSplitMix64(SubSeed(master, kind, index))
	return candidates[rng.NextInt(0, len(candidates))], nil
}
