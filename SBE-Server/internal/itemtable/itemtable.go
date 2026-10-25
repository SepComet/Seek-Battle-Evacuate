// Package itemtable 从 Luban 导出的 tbitemconfig.json 加载服务器校验所需的物品配置。
// 数据源与客户端同源：由 SBE-Design 的 gen_cli 脚本导出（见 tools/sync_config）。
package itemtable

import (
	"encoding/json"
	"fmt"
	"os"

	"sbe-server/internal/core"
)

type rawItem struct {
	ID         int `json:"id"`
	Rarity     int `json:"rarity"`
	StackLimit int `json:"stackLimit"`
	EquipSlot  int `json:"equipSlot"`
}

// Table 物品配置表。
type Table map[int]core.ItemDef

// Get 实现 core.ItemTable。
func (t Table) Get(itemID int) (core.ItemDef, bool) {
	def, ok := t[itemID]
	return def, ok
}

// Catalog 物品目录：物品定义表 + 按稀有度分桶的物品 id（桶内顺序 = 配置表顺序）。
// 物品表本身是 map（无序），生成器按稀有度取模选物品时必须用有序桶，
// 因此加载时另建一份保序视图；桶序变动会改变掉落结果，不得重排。
type Catalog struct {
	Table
	rarityPools map[int][]int
}

// RarityPool 返回指定稀有度（1=White … 5=Red）的物品 id 列表，顺序即配置表顺序。
func (c *Catalog) RarityPool(rarity int) []int {
	return c.rarityPools[rarity]
}

// Load 从 Luban 导出的 JSON 文件加载物品定义表（不含稀有度分桶）。
func Load(path string) (Table, error) {
	catalog, err := LoadCatalog(path)
	if err != nil {
		return nil, err
	}
	return catalog.Table, nil
}

// LoadCatalog 从 Luban 导出的 JSON 文件加载物品定义表与稀有度分桶。
func LoadCatalog(path string) (*Catalog, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("read item table: %w", err)
	}
	var raw []rawItem
	if err := json.Unmarshal(data, &raw); err != nil {
		return nil, fmt.Errorf("parse item table %s: %w", path, err)
	}
	catalog := &Catalog{
		Table:       make(Table, len(raw)),
		rarityPools: make(map[int][]int),
	}
	for _, it := range raw {
		if it.ID <= 0 {
			return nil, fmt.Errorf("item table %s: invalid id %d", path, it.ID)
		}
		if _, dup := catalog.Table[it.ID]; dup {
			return nil, fmt.Errorf("item table %s: duplicate id %d", path, it.ID)
		}
		if it.Rarity < core.RarityWhite || it.Rarity > core.RarityRed {
			return nil, fmt.Errorf("item table %s: id %d: rarity %d out of range [%d,%d]",
				path, it.ID, it.Rarity, core.RarityWhite, core.RarityRed)
		}
		if it.StackLimit <= 0 {
			it.StackLimit = 1
		}
		catalog.Table[it.ID] = core.ItemDef{StackLimit: it.StackLimit, EquipSlot: it.EquipSlot, Rarity: it.Rarity}
		// append 保持 JSON 数组顺序 = 配置表顺序
		catalog.rarityPools[it.Rarity] = append(catalog.rarityPools[it.Rarity], it.ID)
	}
	return catalog, nil
}
