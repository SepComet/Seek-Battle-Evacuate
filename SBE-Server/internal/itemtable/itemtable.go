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

// Load 从 Luban 导出的 JSON 文件加载。
func Load(path string) (Table, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("read item table: %w", err)
	}
	var raw []rawItem
	if err := json.Unmarshal(data, &raw); err != nil {
		return nil, fmt.Errorf("parse item table %s: %w", path, err)
	}
	table := make(Table, len(raw))
	for _, it := range raw {
		if it.ID <= 0 {
			return nil, fmt.Errorf("item table %s: invalid id %d", path, it.ID)
		}
		if _, dup := table[it.ID]; dup {
			return nil, fmt.Errorf("item table %s: duplicate id %d", path, it.ID)
		}
		if it.StackLimit <= 0 {
			it.StackLimit = 1
		}
		table[it.ID] = core.ItemDef{StackLimit: it.StackLimit, EquipSlot: it.EquipSlot}
	}
	return table, nil
}
