// 双端可移植的伪随机数与子种子派生（exploration-online-v1 契约 §1 / §2）。
//
// C# 侧逐位同构实现见
// SBE-Client/Assets/GameMain/Scripts/Runtime/CustomComponent/Exploration/Random/
// （SplitMix64.cs、ExplorationSeed.cs）；两端由种子向量测试锁死
// （internal/mapgen/vectors_test.go 的已知值表与完整生成向量）。
//
// 禁止在内容生成里使用 math/rand：生成结果必须只由 (masterSeed, 配置) 决定。
package mapgen

// SplitMix64 的常数（契约 §1）。
const (
	gamma = 0x9E3779B97F4A7C15
	mix1  = 0xBF58476D1CE4E5B9
	mix2  = 0x94D049BB133111EB
)

// 子种子类型常量（契约 §2，取值与 C# ExplorationSeed.Kind* 一致）。
const (
	KindResource   = 1 // 物资点
	KindEnemy      = 2 // 敌点
	KindExtraction = 3 // 撤离点
	KindSpawn      = 4 // 出生点
)

// SplitMix64 有状态伪随机数发生器。
type SplitMix64 struct {
	state uint64
}

// NewSplitMix64 以给定种子创建发生器。
func NewSplitMix64(seed uint64) *SplitMix64 {
	return &SplitMix64{state: seed}
}

// Next 推进状态并返回下一个 64 位随机值。
func (r *SplitMix64) Next() uint64 {
	r.state += gamma
	return mix(r.state)
}

// NextInt 返回 [min, maxExclusive) 内的 int；maxExclusive > min 由调用方保证。
// 跨度按 int64 计算：Go 的 int 是 64 位，但入参可能跨越 int32 边界
// （如 int.MinValue..int.MaxValue），直接相减再转 uint64 在 32 位语义下会溢出。
func (r *SplitMix64) NextInt(min, maxExclusive int) int {
	span := uint64(int64(maxExclusive) - int64(min))
	return min + int(r.Next()%span)
}

// Hash64 契约 §1 的无状态哈希：先做 gamma 累加再混合。
func Hash64(value uint64) uint64 {
	return mix(value + gamma)
}

func mix(z uint64) uint64 {
	z = (z ^ (z >> 30)) * mix1
	z = (z ^ (z >> 27)) * mix2
	return z ^ (z >> 31)
}

// SubSeed 由主种子、点位类型与条目下标派生 64 位子种子（契约 §2）：
// Hash64(Hash64(Hash64(master) ^ kind) ^ index)。
// 每个随机消费点从独立子种子起步，条目之间不共享随机流。
func SubSeed(master int64, kind, index int) uint64 {
	h := Hash64(uint64(master))
	h = Hash64(h ^ uint64(kind))
	return Hash64(h ^ uint64(index))
}
