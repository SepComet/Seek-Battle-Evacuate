// Package core 实现局外经济的服务器侧纯逻辑：
// 状态模型、操作集应用（校验+合并）与迁移初始化。不涉及任何 IO。
package core

import (
	"errors"

	"sbe-server/internal/protocol"
)

// ItemDef 校验所需的物品配置字段。
type ItemDef struct {
	StackLimit int
	EquipSlot  int // 0=不可穿戴，1=武器，2=防具
	Rarity     int // 稀有度：1=White 2=Green 3=Blue 4=Gold 5=Red（内容生成分桶用）
}

// 稀有度枚举（与客户端 Rarity 一致，None=0 不参与掉落分桶）。
const (
	RarityWhite = 1
	RarityGreen = 2
	RarityBlue  = 3
	RarityGold  = 4
	RarityRed   = 5
)

// RarityOrder 掉落分桶的稀有度顺序，与客户端 ResourcePointGenerator.RarityOrder 一致。
var RarityOrder = [5]int{RarityWhite, RarityGreen, RarityBlue, RarityGold, RarityRed}

// ItemTable 物品配置表查询接口，由配置加载层实现。
type ItemTable interface {
	Get(itemID int) (ItemDef, bool)
}

// Limits 校验所需的全局约束与难度枚举，由配置加载层组装
// （MaxPlayerPartySize/BackpackSlotCount 取自 tbglobalconfig，Difficulties 取自 tbdifficultyconfig）。
type Limits struct {
	MaxPlayerPartySize int
	BackpackSlotCount  int
	Difficulties       map[int]bool
}

// Stack 无实例身份的物品堆（携带计划条目）。
type Stack struct {
	ItemID int
	Count  int
}

// SettleMeta 结算上报的战绩元数据，落 settlements 审计列。
type SettleMeta struct {
	Outcome    int
	Difficulty int
	Seed       int64
	StartedAt  int64
	EndedAt    int64
}

// Row 仓库物品行。装备行以 Wearer>0 表示穿在对应角色身上（此时网格坐标无意义）。
type Row struct {
	UID     int64
	ItemID  int
	Count   int
	X       int
	Y       int
	Rotated bool
	Wearer  int
}

// Character 花名册角色，顺序即招募次序（出战队列由 State.Party 单独表达）。
type Character struct {
	CID int
}

// State 一名玩家的完整局外状态。
// Party/Difficulty/Carried 为战备配置：空队列、难度 0、空携带 = 未配置（合法编辑中间态）。
type State struct {
	Rows        []Row
	Characters  []Character
	Party       []int   // 出战角色 ID 及顺序
	Difficulty  int     // 难度枚举，0=未配置
	Carried     []Stack // 携带计划
	NextUID     int64
	Initialized bool
}

// ErrRejected 表示操作未通过校验；错误信息面向日志与调试。
var ErrRejected = errors.New("op rejected")

// ToSnapshot 导出为协议快照。
func (s State) ToSnapshot() protocol.Snapshot {
	snap := protocol.Snapshot{
		Items:       make([]protocol.SnapRow, 0, len(s.Rows)),
		Characters:  make([]protocol.SnapChar, 0, len(s.Characters)),
		Initialized: s.Initialized,
	}
	for _, r := range s.Rows {
		snap.Items = append(snap.Items, protocol.SnapRow{
			UID: r.UID, ItemID: r.ItemID, Count: r.Count,
			X: r.X, Y: r.Y, R: r.Rotated, Wearer: r.Wearer,
		})
	}
	for _, c := range s.Characters {
		snap.Characters = append(snap.Characters, protocol.SnapChar{CID: c.CID})
	}
	snap.Party = make([]int, len(s.Party))
	copy(snap.Party, s.Party)
	snap.Difficulty = s.Difficulty
	snap.Carried = make([]protocol.Stack, len(s.Carried))
	for i, st := range s.Carried {
		snap.Carried[i] = protocol.Stack{ItemID: st.ItemID, Count: st.Count}
	}
	return snap
}

// Clone 深拷贝，保证应用操作时输入状态不被修改（原子性）。
func (s State) Clone() State {
	out := State{
		Rows:        make([]Row, len(s.Rows)),
		Characters:  make([]Character, len(s.Characters)),
		Party:       make([]int, len(s.Party)),
		Difficulty:  s.Difficulty,
		Carried:     make([]Stack, len(s.Carried)),
		NextUID:     s.NextUID,
		Initialized: s.Initialized,
	}
	copy(out.Rows, s.Rows)
	copy(out.Characters, s.Characters)
	copy(out.Party, s.Party)
	copy(out.Carried, s.Carried)
	return out
}

func (s *State) findRow(uid int64) int {
	for i := range s.Rows {
		if s.Rows[i].UID == uid {
			return i
		}
	}
	return -1
}

func (s *State) hasCharacter(cid int) bool {
	for _, c := range s.Characters {
		if c.CID == cid {
			return true
		}
	}
	return false
}

// wornRowInSlot 返回角色在某槽位当前穿戴的行索引，未找到返回 -1。
func (s *State) wornRowInSlot(items ItemTable, cid, slot int) int {
	for i := range s.Rows {
		if s.Rows[i].Wearer == cid {
			if def, ok := items.Get(s.Rows[i].ItemID); ok && def.EquipSlot == slot {
				return i
			}
		}
	}
	return -1
}
