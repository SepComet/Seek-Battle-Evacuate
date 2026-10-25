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

// CatalogEntry 构造目录用的单条物品数据（免 IO 路径，供测试装配内联配置）。
type CatalogEntry struct {
	ID         int
	Rarity     int
	StackLimit int
	EquipSlot  int
}

// NewCatalog 用已解析的物品数据构造目录；与 LoadCatalog 共用同一套校验
// （id 为正且不重复、稀有度为 [White,Red] 内的值），桶内顺序 = 入参顺序。
func NewCatalog(entries []CatalogEntry) (*Catalog, error) {
	catalog := &Catalog{
		Table:       make(Table, len(entries)),
		rarityPools: make(map[int][]int),
	}
	for _, entry := range entries {
		if entry.ID <= 0 {
			return nil, fmt.Errorf("invalid id %d", entry.ID)
		}
		if _, dup := catalog.Table[entry.ID]; dup {
			return nil, fmt.Errorf("duplicate id %d", entry.ID)
		}
		if entry.Rarity < core.RarityWhite || entry.Rarity > core.RarityRed {
			return nil, fmt.Errorf("id %d: rarity %d out of range [%d,%d]",
				entry.ID, entry.Rarity, core.RarityWhite, core.RarityRed)
		}
		stackLimit := entry.StackLimit
		if stackLimit <= 0 {
			stackLimit = 1
		}
		catalog.Table[entry.ID] = core.ItemDef{StackLimit: stackLimit, EquipSlot: entry.EquipSlot, Rarity: entry.Rarity}
		catalog.rarityPools[entry.Rarity] = append(catalog.rarityPools[entry.Rarity], entry.ID)
	}
	return catalog, nil
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
	entries := make([]CatalogEntry, len(raw))
	for i, it := range raw {
		// 数组序即配置表序，分桶依赖它，不得重排
		entries[i] = CatalogEntry{ID: it.ID, Rarity: it.Rarity, StackLimit: it.StackLimit, EquipSlot: it.EquipSlot}
	}
	catalog, err := NewCatalog(entries)
	if err != nil {
		return nil, fmt.Errorf("item table %s: %w", path, err)
	}
	return catalog, nil
}
