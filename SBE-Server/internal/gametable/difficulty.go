// 难度表（tbdifficultyconfig.json）的威胁权重加载：
// 生成器按 difficulty tier 取敌点威胁等级的加权选项。
// 与 tbglobalconfig 的 tier 集合分开加载——那份只用于校验难度合法性，
// 这份服务于内容生成，职责不混。
package gametable

import (
	"encoding/json"
	"fmt"
	"os"
)

type rawThreatWeight struct {
	Threat int `json:"threat"`
	Weight int `json:"weight"`
}

type rawDifficulty struct {
	Tier         int               `json:"tier"`
	Name         string            `json:"name"`
	EnemyParties []rawThreatWeight `json:"enemyParties"`
}

// ThreatWeight 威胁等级的权重项。切片顺序即配置顺序，也是生成器加权累减的顺序，
// 重排会改变生成结果，不得重排。
type ThreatWeight struct {
	Threat int
	Weight int
}

// Difficulty 一个难度档位的威胁权重配置。
type Difficulty struct {
	Tier    int
	Weights []ThreatWeight
}

// DifficultyTable 难度表：tier → 威胁权重选项。
type DifficultyTable struct {
	byTier map[int]Difficulty
}

// NewDifficultyTable 用已解析的难度行构造难度表（测试与装配用）；重复 tier 时后一行覆盖前一行。
func NewDifficultyTable(difficulties []Difficulty) *DifficultyTable {
	table := &DifficultyTable{byTier: make(map[int]Difficulty, len(difficulties))}
	for _, difficulty := range difficulties {
		table.byTier[difficulty.Tier] = difficulty
	}
	return table
}

// Weights 返回指定难度档位的威胁权重选项；该档位未配置时 ok=false
// （调用方须报错，不静默兜底）。
func (t *DifficultyTable) Weights(tier int) ([]ThreatWeight, bool) {
	difficulty, ok := t.byTier[tier]
	if !ok {
		return nil, false
	}
	return difficulty.Weights, true
}

// Len 返回难度档位数（启动日志摘要用）。
func (t *DifficultyTable) Len() int {
	return len(t.byTier)
}

// LoadDifficulties 读取并校验 Luban 导出的 tbdifficultyconfig.json 的威胁权重部分。
func LoadDifficulties(path string) (*DifficultyTable, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("read difficulty table: %w", err)
	}
	var raw []rawDifficulty
	if err := json.Unmarshal(data, &raw); err != nil {
		return nil, fmt.Errorf("parse difficulty table %s: %w", path, err)
	}
	if len(raw) == 0 {
		return nil, fmt.Errorf("difficulty table %s: empty", path)
	}
	table := NewDifficultyTable(nil)
	for _, d := range raw {
		if d.Tier <= 0 {
			return nil, fmt.Errorf("difficulty table %s: invalid tier %d", path, d.Tier)
		}
		if _, dup := table.byTier[d.Tier]; dup {
			return nil, fmt.Errorf("difficulty table %s: duplicate tier %d", path, d.Tier)
		}
		if len(d.EnemyParties) == 0 {
			return nil, fmt.Errorf("difficulty table %s: tier %d has no enemy party weight", path, d.Tier)
		}
		weights := make([]ThreatWeight, 0, len(d.EnemyParties))
		total := 0
		seen := make(map[int]bool, len(d.EnemyParties))
		for _, w := range d.EnemyParties {
			// 威胁枚举：0=None 1=Low 2=Middle 3=High（与客户端 EnemyPartyThreatLevel 一致）
			if w.Threat < 0 || w.Threat > 3 {
				return nil, fmt.Errorf("difficulty table %s: tier %d: invalid threat %d", path, d.Tier, w.Threat)
			}
			if w.Weight < 0 {
				return nil, fmt.Errorf("difficulty table %s: tier %d: negative weight for threat %d",
					path, d.Tier, w.Threat)
			}
			if seen[w.Threat] {
				return nil, fmt.Errorf("difficulty table %s: tier %d: duplicate threat %d", path, d.Tier, w.Threat)
			}
			seen[w.Threat] = true
			total += w.Weight
			weights = append(weights, ThreatWeight{Threat: w.Threat, Weight: w.Weight})
		}
		if total <= 0 {
			return nil, fmt.Errorf("difficulty table %s: tier %d: all threat weights are zero", path, d.Tier)
		}
		table.byTier[d.Tier] = Difficulty{Tier: d.Tier, Weights: weights}
	}
	return table, nil
}
