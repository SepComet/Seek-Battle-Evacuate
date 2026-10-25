// 生成器消费的配置集合与内存构造入口。
//
// Generate 需要四张表：物资点掉落、敌队伍、难度威胁权重、物品稀有度分桶。
// 生产路径由 cmd/sbe-server 的 loadConfigs 装配（全部读 Luban 导出 JSON）；
// 测试与将来的场景调试可以直接用 New* 构造器装配内联数据，不经过 IO。
package gametable

import (
	"sbe-server/internal/core"
)

// ItemCatalog 生成器所需的物品表查询面：按 id 取定义、按稀有度取有序桶。
// 由 itemtable.Catalog 实现（桶内顺序 = 配置表顺序）。物品稀有度的合法性
// 由目录加载器保证（itemtable 在加载期拒绝 [1,5] 之外的值）。
type ItemCatalog interface {
	Get(itemID int) (core.ItemDef, bool)
	RarityPool(rarity int) []int
}

// Tables 探索内容生成所需的四张配置表。
type Tables struct {
	Resources    *ResourceTable
	Parties      *EnemyPartyTable
	Difficulties *DifficultyTable
	Items        ItemCatalog
}

// NewResourceTable 用已解析的物资点配置构造表（Load 与测试共用）。
// 本构造器不做校验：重复 id 后者覆盖前者。生产路径必须走 LoadResourcePoints。
func NewResourceTable(points []ResourcePoint) *ResourceTable {
	byID := make(map[int]ResourcePoint, len(points))
	for _, point := range points {
		byID[point.ID] = point
	}
	return &ResourceTable{byID: byID}
}

// NewEnemyPartyTable 用已解析的敌队伍构造表并按威胁建池（Load 与测试共用）。
// 池内顺序 = 入参顺序，生成器按池下标取模，不得重排。
// 本构造器不做校验；生产路径必须走 LoadEnemyParties。
func NewEnemyPartyTable(parties []EnemyParty) *EnemyPartyTable {
	table := &EnemyPartyTable{
		byID:     make(map[int]EnemyParty, len(parties)),
		byThreat: make(map[int][]EnemyParty),
	}
	for _, party := range parties {
		table.byID[party.ID] = party
		table.byThreat[party.Threat] = append(table.byThreat[party.Threat], party)
	}
	return table
}
