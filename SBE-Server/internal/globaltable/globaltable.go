// Package globaltable 从 Luban 导出的 tbglobalconfig.json / tbdifficultyconfig.json
// 加载服务器校验所需的全局约束与难度枚举。
// 数据源与客户端同源：由 SBE-Design 的 gen_cli 脚本导出（见 tools/sync_config）。
package globaltable

import (
	"encoding/json"
	"fmt"
	"os"

	"sbe-server/internal/core"
)

type rawGlobal struct {
	MinPlayerPartySize int `json:"minPlayerPartySize"`
	MaxPlayerPartySize int `json:"maxPlayerPartySize"`
	BackpackSlotCount  int `json:"backpackSlotCount"`
}

type rawDifficulty struct {
	Tier int `json:"tier"`
}

// Load 从 Luban 导出的 JSON 文件组装 core.Limits。
func Load(globalPath, difficultyPath string) (core.Limits, error) {
	limits := core.Limits{
		Difficulties: make(map[int]bool),
	}

	data, err := os.ReadFile(globalPath)
	if err != nil {
		return limits, fmt.Errorf("read global table: %w", err)
	}
	// mode=one 的单行表导出为单元素数组
	var globals []rawGlobal
	if err := json.Unmarshal(data, &globals); err != nil {
		return limits, fmt.Errorf("parse global table %s: %w", globalPath, err)
	}
	if len(globals) != 1 {
		return limits, fmt.Errorf("global table %s: want exactly 1 row, got %d", globalPath, len(globals))
	}
	g := globals[0]
	if g.MaxPlayerPartySize <= 0 {
		return limits, fmt.Errorf("global table %s: invalid maxPlayerPartySize %d", globalPath, g.MaxPlayerPartySize)
	}
	if g.BackpackSlotCount <= 0 {
		return limits, fmt.Errorf("global table %s: invalid backpackSlotCount %d", globalPath, g.BackpackSlotCount)
	}
	limits.MaxPlayerPartySize = g.MaxPlayerPartySize
	limits.BackpackSlotCount = g.BackpackSlotCount

	data, err = os.ReadFile(difficultyPath)
	if err != nil {
		return limits, fmt.Errorf("read difficulty table: %w", err)
	}
	var diffs []rawDifficulty
	if err := json.Unmarshal(data, &diffs); err != nil {
		return limits, fmt.Errorf("parse difficulty table %s: %w", difficultyPath, err)
	}
	if len(diffs) == 0 {
		return limits, fmt.Errorf("difficulty table %s: empty", difficultyPath)
	}
	for _, d := range diffs {
		if d.Tier <= 0 {
			return limits, fmt.Errorf("difficulty table %s: invalid tier %d", difficultyPath, d.Tier)
		}
		if limits.Difficulties[d.Tier] {
			return limits, fmt.Errorf("difficulty table %s: duplicate tier %d", difficultyPath, d.Tier)
		}
		limits.Difficulties[d.Tier] = true
	}
	return limits, nil
}
