package main

import (
	"fmt"

	"sbe-server/internal/core"
	"sbe-server/internal/gametable"
	"sbe-server/internal/globaltable"
	"sbe-server/internal/itemtable"
	"sbe-server/internal/mapdef"
)

// configs 服务器启动时加载的全套配置。
// 前四项供既有的局外校验使用，后四项供探索内容生成使用
// （生成器入参见 internal/gametable.Tables 与 internal/mapgen.Generate）。
type configs struct {
	Catalog        *itemtable.Catalog
	Limits         core.Limits
	Layout         *mapdef.Layout
	ResourcePoints *gametable.ResourceTable
	EnemyParties   *gametable.EnemyPartyTable
	Difficulties   *gametable.DifficultyTable
}

// Tables 把四份生成配置装配成生成器入参（不含 IO，纯搬运）。
func (c *configs) Tables() *gametable.Tables {
	return &gametable.Tables{
		Resources:    c.ResourcePoints,
		Parties:      c.EnemyParties,
		Difficulties: c.Difficulties,
		Items:        c.Catalog,
	}
}

// loadConfigs 按固定顺序加载全部配置，任一失败立即返回 error（main 中据此 log.Fatalf）。
// 除各文件自身的校验外，还做一处跨表校验：地图里的物资点类型必须能在资源点表中查到，
// 否则生成阶段必然报错——放在启动时暴露，避免带病启动。
func loadConfigs(itemsPath, globalPath, difficultyPath, mapPath, resourcePointPath, enemyPartyPath string) (*configs, error) {
	catalog, err := itemtable.LoadCatalog(itemsPath)
	if err != nil {
		return nil, fmt.Errorf("load item table: %w", err)
	}

	limits, err := globaltable.Load(globalPath, difficultyPath)
	if err != nil {
		return nil, fmt.Errorf("load global table: %w", err)
	}

	// 地图加载即校验「每个敌人点必须归属某个房间」等硬性不变量。
	layout, err := mapdef.Load(mapPath)
	if err != nil {
		return nil, fmt.Errorf("load map definition: %w", err)
	}

	resourceTable, err := gametable.LoadResourcePoints(resourcePointPath)
	if err != nil {
		return nil, fmt.Errorf("load resource point table: %w", err)
	}

	partyTable, err := gametable.LoadEnemyParties(enemyPartyPath)
	if err != nil {
		return nil, fmt.Errorf("load enemy party table: %w", err)
	}

	// 难度表的威胁权重（生成敌点用）；tier 集合已在 globaltable.Load 中加载用于校验，
	// 这里只取生成所需的权重，两份配置都指同一个文件。
	difficultyTable, err := gametable.LoadDifficulties(difficultyPath)
	if err != nil {
		return nil, fmt.Errorf("load difficulty weights: %w", err)
	}

	for i, point := range layout.ResourcePoints {
		if _, ok := resourceTable.Get(point.ResourcePointID); !ok {
			return nil, fmt.Errorf("load map definition: resource point %d (%g,%g) references unknown resourcePointId %d",
				i, point.X, point.Y, point.ResourcePointID)
		}
	}

	return &configs{
		Catalog:        catalog,
		Limits:         limits,
		Layout:         layout,
		ResourcePoints: resourceTable,
		EnemyParties:   partyTable,
		Difficulties:   difficultyTable,
	}, nil
}
