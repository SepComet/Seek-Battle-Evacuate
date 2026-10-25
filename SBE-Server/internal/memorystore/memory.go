// Package memorystore 进程内存储实现：单机开发与测试用。
package memorystore

import (
	"context"
	"fmt"
	"sort"
	"strings"
	"sync"

	"sbe-server/internal/core"
	"sbe-server/internal/protocol"
	"sbe-server/internal/store"
)

// account 一名账号：登录凭据哈希与局外状态。
type account struct {
	username     string
	passwordHash string
	state        core.State
}

// settleRecord runId 的持有者与首次结算后的状态。
type settleRecord struct {
	owner int64
	state core.State
}

// Store 内存实现。
type Store struct {
	mu sync.Mutex

	usersByID       map[int64]*account
	usersByUsername map[string]int64
	tokens          map[string]int64
	settles         map[string]settleRecord      // runId -> 持有者与首次结算后的状态（同用户幂等回放；他人占用拒绝）
	runs            map[int64][]protocol.SnapRun // userID -> 战绩审计记录（追加序，查询时排序）
	mintUID         func() (int64, error)        // uid 铸造器（雪花）
}

// New 创建内存存储。mintUID 为 uid 铸造函数（通常注入 snowflake.Node.Next）。
func New(mintUID func() (int64, error)) *Store {
	return &Store{
		usersByID:       make(map[int64]*account),
		usersByUsername: make(map[string]int64),
		tokens:          make(map[string]int64),
		settles:         make(map[string]settleRecord),
		runs:            make(map[int64][]protocol.SnapRun),
		mintUID:         mintUID,
	}
}

// Register 实现 store.Store。
func (s *Store) Register(_ context.Context, username, password string) (int64, string, error) {
	name, err := store.ValidateUsername(username)
	if err != nil {
		return 0, "", err
	}
	if err := store.ValidatePassword(password); err != nil {
		return 0, "", err
	}
	hash, err := store.HashPassword(password)
	if err != nil {
		return 0, "", err
	}
	uid, err := s.mintUID()
	if err != nil {
		return 0, "", err
	}
	token, err := store.NewToken()
	if err != nil {
		return 0, "", err
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	if _, taken := s.usersByUsername[name]; taken {
		return 0, "", store.ErrUsernameTaken
	}
	s.usersByUsername[name] = uid
	s.usersByID[uid] = &account{username: name, passwordHash: hash}
	s.tokens[token] = uid
	return uid, token, nil
}

// Login 实现 store.Store。
func (s *Store) Login(_ context.Context, username, password string) (int64, string, error) {
	name := strings.TrimSpace(username)
	token, err := store.NewToken()
	if err != nil {
		return 0, "", err
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	uid, ok := s.usersByUsername[name]
	if !ok {
		return 0, "", store.ErrInvalidCredentials
	}
	acc := s.usersByID[uid]
	if !store.VerifyPassword(acc.passwordHash, password) {
		return 0, "", store.ErrInvalidCredentials
	}
	s.tokens[token] = uid
	return uid, token, nil
}

// Auth 实现 store.Store。
func (s *Store) Auth(_ context.Context, token string) (int64, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	userID, ok := s.tokens[token]
	if !ok {
		return 0, store.ErrUnknownToken
	}
	return userID, nil
}

// Load 实现 store.Store。
func (s *Store) Load(_ context.Context, userID int64) (core.State, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	acc, ok := s.usersByID[userID]
	if !ok {
		return core.State{}, fmt.Errorf("store: unknown user %d", userID)
	}
	return acc.state.Clone(), nil
}

// ApplySettle 实现 store.Store。
// runId 全局唯一：同用户重复提交回放首次快照；已被其他用户占用的 runId 拒绝。
func (s *Store) ApplySettle(_ context.Context, userID int64, runID string, meta core.SettleMeta, ops []protocol.Op, items core.ItemTable, limits core.Limits) (core.State, bool, error) {
	if runID == "" || len(runID) > 64 {
		return core.State{}, false, fmt.Errorf("%w: invalid runId", core.ErrRejected)
	}

	s.mu.Lock()
	defer s.mu.Unlock()

	acc, ok := s.usersByID[userID]
	if !ok {
		return core.State{}, false, fmt.Errorf("store: unknown user %d", userID)
	}
	if prev, done := s.settles[runID]; done {
		if prev.owner != userID {
			return core.State{}, false, fmt.Errorf("%w: runId %s already used by another user", core.ErrRejected, runID)
		}
		return prev.state.Clone(), true, nil
	}

	next, err := core.Apply(acc.state, ops, items, limits)
	if err != nil {
		return core.State{}, false, err
	}
	acc.state = next
	s.settles[runID] = settleRecord{owner: userID, state: next}
	s.runs[userID] = append(s.runs[userID], protocol.SnapRun{
		RunID: runID, Outcome: meta.Outcome, Difficulty: meta.Difficulty,
		Seed: meta.Seed, StartedAt: meta.StartedAt, EndedAt: meta.EndedAt,
	})
	return next.Clone(), false, nil
}

// ApplyOps 实现 store.Store。
func (s *Store) ApplyOps(_ context.Context, userID int64, ops []protocol.Op, items core.ItemTable, limits core.Limits) (core.State, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	acc, ok := s.usersByID[userID]
	if !ok {
		return core.State{}, fmt.Errorf("store: unknown user %d", userID)
	}
	next, err := core.Apply(acc.state, ops, items, limits)
	if err != nil {
		return core.State{}, err
	}
	acc.state = next
	return next.Clone(), nil
}

// Migrate 实现 store.Store。
func (s *Store) Migrate(_ context.Context, userID int64, payload protocol.MigratePayload, items core.ItemTable, limits core.Limits) (core.State, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	acc, ok := s.usersByID[userID]
	if !ok {
		return core.State{}, fmt.Errorf("store: unknown user %d", userID)
	}
	if acc.state.Initialized {
		return core.State{}, store.ErrAlreadyInitialized
	}
	next, err := core.Migrate(acc.state, payload, items, limits)
	if err != nil {
		return core.State{}, err
	}
	acc.state = next
	return next.Clone(), nil
}

// History 实现 store.Store。
func (s *Store) History(_ context.Context, userID int64, limit int) ([]protocol.SnapRun, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	all := s.runs[userID]
	out := make([]protocol.SnapRun, len(all))
	copy(out, all)
	// startedAt 倒序；同刻按 runId 倒序保证排序稳定
	sort.Slice(out, func(i, j int) bool {
		if out[i].StartedAt != out[j].StartedAt {
			return out[i].StartedAt > out[j].StartedAt
		}
		return out[i].RunID > out[j].RunID
	})
	if limit > 0 && len(out) > limit {
		out = out[:limit]
	}
	return out, nil
}
