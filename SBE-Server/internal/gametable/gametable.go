// Package gametable 从 Luban 导出的 tbresourcepointconfig.json / tbenemypartyconfig.json
// 加载探索内容生成所需的两张配置表：物资点掉落权重与敌队伍威胁池。
// 数据源与客户端同源：由 SBE-Design 的 gen_cli 脚本导出（见 tools/sync_config）。
// 本包只做解析与校验，不含生成逻辑——生成器（后续 ticket）直接消费这些纯数据。
package gametable

import (
	"encoding/json"
	"fmt"
	"os"
)

// RarityCount 稀有度档数（White..Red），与客户端 RarityOrder 长度一致。
const RarityCount = 5

type rawLootConfig struct {
	Difficulty  int `json:"difficulty"`
	MinCount    int `json:"minCount"`
	MaxCount    int `json:"maxCount"`
	WhiteWeight int `json:"whiteWeight"`
	GreenWeight int `json:"greenWeight"`
	BlueWeight  int `json:"blueWeight"`
	GoldWeight  int `json:"goldWeight"`
	RedWeight   int `json:"redWeight"`
}

type rawResourcePoint struct {
	ID          int             `json:"id"`
	Name        string          `json:"name"`
	LootConfigs []rawLootConfig `json:"lootConfigs"`
}

type rawEnemyParty struct {
	ID           int    `json:"id"`
	Name         string `json:"name"`
	EnemyIDs     []int  `json:"enemyIds"`
	ThreatConfig int    `json:"threatConfig"`
}

// LootConfig 某物资点在某个难度下的掉落生成配置。
type LootConfig struct {
	Difficulty  int
	MinCount    int
	MaxCount    int
	WhiteWeight int
	GreenWeight int
	BlueWeight  int
	GoldWeight  int
	RedWeight   int
}

// Weights 按稀有度顺序（White,Green,Blue,Gold,Red）返回五档权重，
// 供生成器的加权选择直接用（顺序与客户端 RarityOrder 一致）。
func (c LootConfig) Weights() [RarityCount]int {
	return [RarityCount]int{c.WhiteWeight, c.GreenWeight, c.BlueWeight, c.GoldWeight, c.RedWeight}
}

// ResourcePoint 物资点配置：id → 各难度掉落配置。
type ResourcePoint struct {
	ID          int
	LootConfigs []LootConfig
}

// LootFor 返回该物资点在指定难度下的掉落配置；缺失时 ok=false（调用方须报错，不静默兜底）。
func (p ResourcePoint) LootFor(difficulty int) (LootConfig, bool) {
	for _, loot := range p.LootConfigs {
		if loot.Difficulty == difficulty {
			return loot, true
		}
	}
	return LootConfig{}, false
}

// ResourceTable 物资点配置表。
type ResourceTable struct {
	byID map[int]ResourcePoint
}

// Get 按物资点类型 id 查询。
func (t *ResourceTable) Get(id int) (ResourcePoint, bool) {
	point, ok := t.byID[id]
	return point, ok
}

// Len 返回表内物资点类型数量（启动日志摘要用）。
func (t *ResourceTable) Len() int {
	return len(t.byID)
}

// LoadResourcePoints 读取并校验 Luban 导出的 tbresourcepointconfig.json。
func LoadResourcePoints(path string) (*ResourceTable, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("read resource point table: %w", err)
	}
	var raw []rawResourcePoint
	if err := json.Unmarshal(data, &raw); err != nil {
		return nil, fmt.Errorf("parse resource point table %s: %w", path, err)
	}
	if len(raw) == 0 {
		return nil, fmt.Errorf("resource point table %s: empty", path)
	}
	table := &ResourceTable{byID: make(map[int]ResourcePoint, len(raw))}
	for _, rp := range raw {
		if rp.ID <= 0 {
			return nil, fmt.Errorf("resource point table %s: invalid id %d", path, rp.ID)
		}
		if _, dup := table.byID[rp.ID]; dup {
			return nil, fmt.Errorf("resource point table %s: duplicate id %d", path, rp.ID)
		}
		if len(rp.LootConfigs) == 0 {
			return nil, fmt.Errorf("resource point table %s: id %d has no loot config", path, rp.ID)
		}
		point := ResourcePoint{ID: rp.ID, LootConfigs: make([]LootConfig, 0, len(rp.LootConfigs))}
		seen := make(map[int]bool, len(rp.LootConfigs))
		for _, lc := range rp.LootConfigs {
			loot := LootConfig{
				Difficulty:  lc.Difficulty,
				MinCount:    lc.MinCount,
				MaxCount:    lc.MaxCount,
				WhiteWeight: lc.WhiteWeight,
				GreenWeight: lc.GreenWeight,
				BlueWeight:  lc.BlueWeight,
				GoldWeight:  lc.GoldWeight,
				RedWeight:   lc.RedWeight,
			}
			if err := loot.validate(); err != nil {
				return nil, fmt.Errorf("resource point table %s: id %d: %w", path, rp.ID, err)
			}
			if seen[loot.Difficulty] {
				return nil, fmt.Errorf("resource point table %s: id %d: duplicate difficulty %d", path, rp.ID, loot.Difficulty)
			}
			seen[loot.Difficulty] = true
			point.LootConfigs = append(point.LootConfigs, loot)
		}
		table.byID[rp.ID] = point
	}
	return table, nil
}

// validate 校验单条掉落配置：数量区间与权重和必须可用（全零权重会让加权选择无解）。
func (c LootConfig) validate() error {
	if c.Difficulty <= 0 {
		return fmt.Errorf("invalid difficulty %d", c.Difficulty)
	}
	if c.MinCount <= 0 || c.MaxCount < c.MinCount {
		return fmt.Errorf("difficulty %d: invalid count range [%d,%d]", c.Difficulty, c.MinCount, c.MaxCount)
	}
	weights := c.Weights()
	total := 0
	for _, weight := range weights {
		if weight < 0 {
			return fmt.Errorf("difficulty %d: negative rarity weight", c.Difficulty)
		}
		total += weight
	}
	if total <= 0 {
		return fmt.Errorf("difficulty %d: all rarity weights are zero", c.Difficulty)
	}
	return nil
}

// EnemyParty 敌队伍配置：进战敌人序列与威胁等级。
type EnemyParty struct {
	ID       int
	EnemyIDs []int
	Threat   int
}

// EnemyPartyTable 敌队伍配置表，并预建按威胁等级分组的队伍池。
type EnemyPartyTable struct {
	byID     map[int]EnemyParty
	byThreat map[int][]EnemyParty
}

// Get 按队伍 id 查询。
func (t *EnemyPartyTable) Get(id int) (EnemyParty, bool) {
	party, ok := t.byID[id]
	return party, ok
}

// Pool 返回指定威胁等级的全部队伍，顺序即配置表顺序——
// 生成器按此池的下标做随机取模，顺序变动会改变摆位结果，不得重排。
func (t *EnemyPartyTable) Pool(threat int) []EnemyParty {
	return t.byThreat[threat]
}

// ThreatCount 返回有队伍登记的威胁等级数（启动日志摘要用）。
func (t *EnemyPartyTable) ThreatCount() int {
	return len(t.byThreat)
}

// LoadEnemyParties 读取并校验 Luban 导出的 tbenemypartyconfig.json。
func LoadEnemyParties(path string) (*EnemyPartyTable, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("read enemy party table: %w", err)
	}
	var raw []rawEnemyParty
	if err := json.Unmarshal(data, &raw); err != nil {
		return nil, fmt.Errorf("parse enemy party table %s: %w", path, err)
	}
	if len(raw) == 0 {
		return nil, fmt.Errorf("enemy party table %s: empty", path)
	}
	table := &EnemyPartyTable{
		byID:     make(map[int]EnemyParty, len(raw)),
		byThreat: make(map[int][]EnemyParty),
	}
	for _, rp := range raw {
		if rp.ID <= 0 {
			return nil, fmt.Errorf("enemy party table %s: invalid id %d", path, rp.ID)
		}
		if _, dup := table.byID[rp.ID]; dup {
			return nil, fmt.Errorf("enemy party table %s: duplicate id %d", path, rp.ID)
		}
		if len(rp.EnemyIDs) == 0 {
			return nil, fmt.Errorf("enemy party table %s: id %d has no enemy", path, rp.ID)
		}
		if rp.ThreatConfig <= 0 {
			return nil, fmt.Errorf("enemy party table %s: id %d: invalid threatConfig %d", path, rp.ID, rp.ThreatConfig)
		}
		for _, enemyID := range rp.EnemyIDs {
			if enemyID <= 0 {
				return nil, fmt.Errorf("enemy party table %s: id %d: invalid enemy id %d", path, rp.ID, enemyID)
			}
		}
		party := EnemyParty{
			ID:       rp.ID,
			EnemyIDs: append([]int(nil), rp.EnemyIDs...),
			Threat:   rp.ThreatConfig,
		}
		table.byID[party.ID] = party
		table.byThreat[party.Threat] = append(table.byThreat[party.Threat], party)
	}
	return table, nil
}
