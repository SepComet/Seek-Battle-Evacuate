// Package run 管理探索局的服务器侧会话：开局分配种子并生成全图内容、交互校验与消耗记账、
// 结算额度对账、以及超时 run 的 TTL 清理。
//
// 设计要点（契约见 exploration-online-v1 的 notes/contracts.md §5~§7，
// 落点与坑见 notes/server-run-session.md）：
//
//   - 单用户单活跃 run，状态全在内存（崩溃丢局可接受，与原型阶段匹配）。
//   - start_run 幂等：已有活跃 run 就返回现状（含已消耗点与已开箱清单），
//     resume=true 且无活跃 run 是错误而不是新建——客户端据此不误建新局。
//   - encounter / loot 按 (runId, pointId) 幂等；encounter 不消耗点，
//     loot 首次成功即记入消耗，重试回放同一清单。
//   - settle 是唯一终结路径（Finish），校验带出物资 ⊆ 开箱所得 ∪ 携带入场 ∪ 战斗所得。
//   - 超时未结算的 run 由 Sweep 清理，防内存泄漏。
//
// 生成器（internal/mapgen）是纯函数：本包每个 run 只持有一份不可变的结果，重连时按点回放。
package run

import (
	"crypto/rand"
	"encoding/binary"
	"encoding/hex"
	"fmt"
	"strconv"
	"sync"
	"time"

	"sbe-server/internal/core"
	"sbe-server/internal/gametable"
	"sbe-server/internal/mapdef"
	"sbe-server/internal/mapgen"
)

// 服务器分配的主种子取值区间（契约 §5：int64，[1, 2147483647]，保证客户端 int 装得下）。
const (
	SeedMin int64 = 1
	SeedMax int64 = 2147483647
)

// DefaultTTL 活跃 run 的空闲存活时长。
const DefaultTTL = 4 * time.Hour

// defaultSweepInterval 后台清理的默认周期。
const defaultSweepInterval = 5 * time.Minute

// 错误信息片段：客户端不解析（契约 §7 错误码一节），但日志与测试要能一眼认出。
const (
	msgNoActiveRun = "no active run"
	msgRunMismatch = "runId does not match the active run"
)

// 点位主键前缀（契约 §6：物资点 "r<i>"、敌点 "e<i>"）。
const (
	resourcePointPrefix = "r"
	enemyPointPrefix    = "e"
)

// LootRecord 一个已开箱点的物品清单（start_run 重连恢复用）。
type LootRecord struct {
	PointID string
	ItemIDs []int
}

// View 活跃 run 的对外形态（start_run 响应）。
// Consumed 与 Looted 同序：都按开箱先后排列。
type View struct {
	RunID      string
	Seed       int64
	Difficulty int
	Spawn      mapdef.Vec2
	Extraction mapdef.Vec2
	Consumed   []string
	Looted     []LootRecord
}

// Config 管理器依赖的配置与可注入接缝。
type Config struct {
	// Layout 固定地图布局；Tables 生成器所需的四张配置表。两者必填。
	Layout *mapdef.Layout
	Tables *gametable.Tables
	// Seed 主种子分配器，缺省用 crypto/rand 落在 [SeedMin, SeedMax]。
	Seed func() (int64, error)
	// RunID 局标识生成器，缺省用 16 字节随机数的十六进制（32 字符）。
	RunID func() (string, error)
	// TTL 空闲存活时长，缺省 DefaultTTL。
	TTL time.Duration
	// Now 时钟，缺省 time.Now（测试注入以驱动 TTL）。
	Now func() time.Time
}

// Manager 单用户单活跃 run 的内存态会话表。
type Manager struct {
	mu       sync.Mutex
	cfg      Config
	sessions map[int64]*session
}

// session 一局探索的服务器侧状态。
type session struct {
	runID      string
	seed       int64
	difficulty int
	spawn      mapdef.Vec2
	extraction mapdef.Vec2
	content    *mapgen.Content

	// looted 已开箱清单，顺序即开箱先后；index 是 pointID → looted 下标。
	looted []LootRecord
	index  map[string]int

	startedAt   time.Time
	lastTouched time.Time
}

// NewManager 创建会话管理器。Layout/Tables 缺失时不 panic，Start 会返回配置错误。
func NewManager(cfg Config) *Manager {
	if cfg.Seed == nil {
		cfg.Seed = randomSeed
	}
	if cfg.RunID == nil {
		cfg.RunID = randomRunID
	}
	if cfg.TTL <= 0 {
		cfg.TTL = DefaultTTL
	}
	if cfg.Now == nil {
		cfg.Now = time.Now
	}
	return &Manager{cfg: cfg, sessions: make(map[int64]*session)}
}

// Start 开局。语义（契约 §7 start_run）：
//
//   - 已有活跃 run：无论 resume 真假都返回该 run 现状，不新建、不改难度。
//   - 无活跃 run 且 resume=true：ErrRejected（客户端据此提示本局已失效，不得重开）。
//   - 无活跃 run 且 resume=false：分配种子、生成全图内容、记下出生/撤离点。
//
// difficulty 必须是难度表中配置了威胁权重的 tier，否则 ErrRejected 且不留下活跃 run。
func (m *Manager) Start(userID int64, difficulty int, resume bool) (View, error) {
	m.mu.Lock()
	defer m.mu.Unlock()

	now := m.cfg.Now()
	if existing := m.sessions[userID]; existing != nil {
		// 幂等：重连恢复也走这里，因此续期 LastTouched。
		existing.lastTouched = now
		return existing.view(), nil
	}
	if resume {
		return View{}, fmt.Errorf("%w: %s", core.ErrRejected, msgNoActiveRun)
	}
	if m.cfg.Layout == nil || m.cfg.Tables == nil {
		return View{}, fmt.Errorf("run: generator is not configured")
	}
	if _, ok := m.cfg.Tables.Difficulties.Weights(difficulty); !ok {
		return View{}, fmt.Errorf("%w: unknown difficulty %d", core.ErrRejected, difficulty)
	}

	seed, err := m.cfg.Seed()
	if err != nil {
		return View{}, fmt.Errorf("run: allocate seed: %w", err)
	}
	if seed < SeedMin || seed > SeedMax {
		// 分配器坏了是服务器缺陷，不是客户端请求问题：不夹取、不静默兜底。
		return View{}, fmt.Errorf("run: allocated seed %d out of range [%d,%d]", seed, SeedMin, SeedMax)
	}
	runID, err := m.cfg.RunID()
	if err != nil {
		return View{}, fmt.Errorf("run: allocate runId: %w", err)
	}
	if runID == "" {
		return View{}, fmt.Errorf("run: allocated empty runId")
	}

	content, err := mapgen.Generate(seed, difficulty, m.cfg.Layout, m.cfg.Tables)
	if err != nil {
		return View{}, fmt.Errorf("run: generate content (difficulty %d): %w", difficulty, err)
	}

	sess := &session{
		runID:       runID,
		seed:        seed,
		difficulty:  difficulty,
		spawn:       content.PlayerSpawnPoint,
		extraction:  content.ExtractionPoint,
		content:     content,
		index:       make(map[string]int),
		startedAt:   now,
		lastTouched: now,
	}
	m.sessions[userID] = sess
	return sess.view(), nil
}

// Encounter 撞敌校验：返回本局该敌点的权威队伍与威胁等级。
//
// 无活跃 run / runId 不匹配 / 点不是本局生成出的敌点（越界或威胁掷为 None）→ ErrRejected。
// encounter 不消耗点（打赢打输后敌人仍在，客户端自决何时重试），重复请求同结果。
func (m *Manager) Encounter(userID int64, runID, pointID string) (partyID int, threat int, err error) {
	m.mu.Lock()
	defer m.mu.Unlock()

	sess, err := m.active(userID, runID)
	if err != nil {
		return 0, 0, err
	}
	index, ok := parsePointIndex(pointID, enemyPointPrefix)
	if !ok || index >= len(sess.content.EnemyPoints) {
		return 0, 0, fmt.Errorf("%w: point %s does not exist", core.ErrRejected, pointID)
	}
	point := sess.content.EnemyPoints[index]
	if !point.Present {
		return 0, 0, fmt.Errorf("%w: point %s does not exist", core.ErrRejected, pointID)
	}

	sess.lastTouched = m.cfg.Now()
	return point.EnemyPartyID, point.Threat, nil
}

// Loot 开箱：返回该点的掉落清单。首次成功即标记消耗并记入开箱序；
// 已消耗的点重试回放同一清单（本阶段每点只有一份清单，开一次即清空）。
//
// 无活跃 run / runId 不匹配 / 点不是本局生成出的物资点 → ErrRejected。
func (m *Manager) Loot(userID int64, runID, pointID string) ([]int, error) {
	m.mu.Lock()
	defer m.mu.Unlock()

	sess, err := m.active(userID, runID)
	if err != nil {
		return nil, err
	}
	if existing, ok := sess.index[pointID]; ok {
		sess.lastTouched = m.cfg.Now()
		return copyInts(sess.looted[existing].ItemIDs), nil
	}

	index, ok := parsePointIndex(pointID, resourcePointPrefix)
	if !ok || index >= len(sess.content.ResourcePoints) {
		return nil, fmt.Errorf("%w: point %s does not exist", core.ErrRejected, pointID)
	}
	point := sess.content.ResourcePoints[index]
	if !point.Present {
		return nil, fmt.Errorf("%w: point %s does not exist", core.ErrRejected, pointID)
	}

	sess.index[pointID] = len(sess.looted)
	sess.looted = append(sess.looted, LootRecord{PointID: pointID, ItemIDs: copyInts(point.ItemIDs)})
	sess.lastTouched = m.cfg.Now()
	return copyInts(point.ItemIDs), nil
}

// ValidateSettle 结算额度对账：允许额度 = 开箱所得 + 携带入场 + 战斗所得，
// 任一 itemId 的带出数量超限即 ErrRejected（整批不落地）。
//
// 只在该用户有活跃 run 且 runId 匹配时校验；无活跃 run 或 runId 不认识时跳过
// （settle 可能来自旧客户端或幂等补报，保持 v3 兼容——边界见 ticket 04 Comments）。
func (m *Manager) ValidateSettle(userID int64, runID string, battleLoot []core.Stack,
	added map[int]int, carried []core.Stack) error {
	m.mu.Lock()
	defer m.mu.Unlock()

	sess := m.sessions[userID]
	if sess == nil || sess.runID != runID {
		return nil
	}

	for _, stack := range battleLoot {
		if stack.Count < 0 {
			return fmt.Errorf("%w: negative battle loot count for itemId %d", core.ErrRejected, stack.ItemID)
		}
	}

	allow := sess.lootedSums()
	addSums(allow, core.SumStacks(carried))
	addSums(allow, core.SumStacks(battleLoot))

	for itemID, count := range added {
		if count > allow[itemID] {
			return fmt.Errorf("%w: itemId %d added %d exceeds allowance %d (looted + carried in + battle loot only)",
				core.ErrRejected, itemID, count, allow[itemID])
		}
	}
	return nil
}

// LootedSums 本局开箱所得的 itemId → 件数汇总（跨点累加）；run 不存在或 runId 不匹配返回 nil。
func (m *Manager) LootedSums(userID int64, runID string) map[int]int {
	m.mu.Lock()
	defer m.mu.Unlock()

	sess := m.sessions[userID]
	if sess == nil || sess.runID != runID {
		return nil
	}
	return sess.lootedSums()
}

// Active 报告该用户是否存在 runId 匹配的活跃 run。
// 供分派层在结算对账前判断是否需要取快照（无活跃 run 的对账整体跳过）。
func (m *Manager) Active(userID int64, runID string) bool {
	m.mu.Lock()
	defer m.mu.Unlock()

	sess := m.sessions[userID]
	return sess != nil && sess.runID == runID
}

// Finish 终结一局（settle 成功后的唯一清理路径）。runId 不匹配时什么也不做。
func (m *Manager) Finish(userID int64, runID string) {
	m.mu.Lock()
	defer m.mu.Unlock()

	sess := m.sessions[userID]
	if sess != nil && sess.runID == runID {
		delete(m.sessions, userID)
	}
}

// Sweep 清理空闲超过 TTL 的 run，返回清理数量。测试直接驱动它；
// 生产路径由 StartSweeper 的后台 goroutine 周期调用。
func (m *Manager) Sweep(now time.Time) int {
	m.mu.Lock()
	defer m.mu.Unlock()

	removed := 0
	for userID, sess := range m.sessions {
		if now.Sub(sess.lastTouched) > m.cfg.TTL {
			delete(m.sessions, userID)
			removed++
		}
	}
	return removed
}

// StartSweeper 启动周期清理的后台 goroutine，返回停止函数（幂等）。
// interval <= 0 时用 defaultSweepInterval。
func (m *Manager) StartSweeper(interval time.Duration) (stop func()) {
	if interval <= 0 {
		interval = defaultSweepInterval
	}
	done := make(chan struct{})
	var once sync.Once
	go func() {
		ticker := time.NewTicker(interval)
		defer ticker.Stop()
		for {
			select {
			case <-done:
				return
			case <-ticker.C:
				m.Sweep(m.cfg.Now())
			}
		}
	}()
	return func() { once.Do(func() { close(done) }) }
}

// active 取该用户的活跃 run，并校验 runId（调用方持锁）。
func (m *Manager) active(userID int64, runID string) (*session, error) {
	sess := m.sessions[userID]
	if sess == nil {
		return nil, fmt.Errorf("%w: %s", core.ErrRejected, msgNoActiveRun)
	}
	if sess.runID != runID {
		return nil, fmt.Errorf("%w: %s", core.ErrRejected, msgRunMismatch)
	}
	return sess, nil
}

// view 导出对外形态（调用方持锁；切片做深拷贝，调用方改动不影响会话）。
func (s *session) view() View {
	consumed := make([]string, len(s.looted))
	looted := make([]LootRecord, len(s.looted))
	for i, record := range s.looted {
		consumed[i] = record.PointID
		looted[i] = LootRecord{PointID: record.PointID, ItemIDs: copyInts(record.ItemIDs)}
	}
	return View{
		RunID:      s.runID,
		Seed:       s.seed,
		Difficulty: s.difficulty,
		Spawn:      s.spawn,
		Extraction: s.extraction,
		Consumed:   consumed,
		Looted:     looted,
	}
}

// lootedSums 开箱所得按 itemId 汇总（调用方持锁）。
func (s *session) lootedSums() map[int]int {
	sums := make(map[int]int)
	for _, record := range s.looted {
		for _, itemID := range record.ItemIDs {
			sums[itemID]++
		}
	}
	return sums
}

// parsePointIndex 把点位主键解析成地图列表下标（契约 §6：物资点 "r<i>"、敌点 "e<i>"）。
// 只接受规范十进制写法：空串、负数、"01" 这类非规范形式一律视为不存在。
func parsePointIndex(pointID, prefix string) (int, bool) {
	if len(pointID) <= len(prefix) || pointID[:len(prefix)] != prefix {
		return 0, false
	}
	digits := pointID[len(prefix):]
	if len(digits) > 1 && digits[0] == '0' {
		return 0, false
	}
	for i := 0; i < len(digits); i++ {
		if digits[i] < '0' || digits[i] > '9' {
			return 0, false
		}
	}
	index, err := strconv.Atoi(digits)
	if err != nil {
		return 0, false
	}
	return index, true
}

func copyInts(values []int) []int {
	if len(values) == 0 {
		return nil
	}
	return append([]int(nil), values...)
}

func addSums(into, from map[int]int) {
	for itemID, count := range from {
		into[itemID] += count
	}
}

// randomSeed 分配主种子：32 位随机取低 31 位，落在 [SeedMin, SeedMax]。
// 不走 math/rand（内容生成必须只由主种子与配置决定，种子来源反而是随机的）。
func randomSeed() (int64, error) {
	var raw [4]byte
	for {
		if _, err := rand.Read(raw[:]); err != nil {
			return 0, err
		}
		seed := int64(binary.BigEndian.Uint32(raw[:]) & 0x7FFFFFFF)
		if seed >= SeedMin {
			return seed, nil
		}
	}
}

// randomRunID 生成局标识：16 字节随机数的十六进制（32 字符，与登录 token 同形）。
func randomRunID() (string, error) {
	var raw [16]byte
	if _, err := rand.Read(raw[:]); err != nil {
		return "", err
	}
	return hex.EncodeToString(raw[:]), nil
}
