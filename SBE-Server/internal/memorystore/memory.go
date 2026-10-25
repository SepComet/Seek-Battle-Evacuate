// Package memorystore 进程内存储实现：单机开发与测试用。
package memorystore

import (
	"context"
	"fmt"
	"sort"
	"sync"

	"sbe-server/internal/core"
	"sbe-server/internal/protocol"
	"sbe-server/internal/store"
)

type user struct {
	state core.State
}

// Store 内存实现。
type Store struct {
	mu sync.Mutex

	usersByDevice map[string]int64
	usersByID     map[int64]*user
	tokens        map[string]int64
	settles       map[string]core.State        // runId -> 首次结算后的状态（幂等命中时返回）
	runs          map[int64][]protocol.SnapRun // userID -> 战绩审计记录（追加序，查询时排序）
	nextUserID    int64
}

// New 创建内存存储。
func New() *Store {
	return &Store{
		usersByDevice: make(map[string]int64),
		usersByID:     make(map[int64]*user),
		tokens:        make(map[string]int64),
		settles:       make(map[string]core.State),
		runs:          make(map[int64][]protocol.SnapRun),
	}
}

// Register 实现 store.Store。
func (s *Store) Register(_ context.Context, device string) (int64, string, error) {
	if device == "" || len(device) > 64 {
		return 0, "", fmt.Errorf("%w: invalid device", core.ErrRejected)
	}
	token, err := store.NewToken()
	if err != nil {
		return 0, "", err
	}

	s.mu.Lock()
	defer s.mu.Unlock()

	userID, ok := s.usersByDevice[device]
	if !ok {
		s.nextUserID++
		userID = s.nextUserID
		s.usersByDevice[device] = userID
		s.usersByID[userID] = &user{}
	}
	s.tokens[token] = userID
	return userID, token, nil
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
	u, ok := s.usersByID[userID]
	if !ok {
		return core.State{}, fmt.Errorf("store: unknown user %d", userID)
	}
	return u.state.Clone(), nil
}

// ApplySettle 实现 store.Store。
func (s *Store) ApplySettle(_ context.Context, userID int64, runID string, meta core.SettleMeta, ops []protocol.Op, items core.ItemTable, limits core.Limits) (core.State, bool, error) {
	if runID == "" || len(runID) > 64 {
		return core.State{}, false, fmt.Errorf("%w: invalid runId", core.ErrRejected)
	}

	s.mu.Lock()
	defer s.mu.Unlock()

	u, ok := s.usersByID[userID]
	if !ok {
		return core.State{}, false, fmt.Errorf("store: unknown user %d", userID)
	}
	if prev, done := s.settles[runID]; done {
		return prev.Clone(), true, nil
	}

	next, err := core.Apply(u.state, ops, items, limits)
	if err != nil {
		return core.State{}, false, err
	}
	u.state = next
	s.settles[runID] = next
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

	u, ok := s.usersByID[userID]
	if !ok {
		return core.State{}, fmt.Errorf("store: unknown user %d", userID)
	}
	next, err := core.Apply(u.state, ops, items, limits)
	if err != nil {
		return core.State{}, err
	}
	u.state = next
	return next.Clone(), nil
}

// Migrate 实现 store.Store。
func (s *Store) Migrate(_ context.Context, userID int64, payload protocol.MigratePayload, items core.ItemTable, limits core.Limits) (core.State, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	u, ok := s.usersByID[userID]
	if !ok {
		return core.State{}, fmt.Errorf("store: unknown user %d", userID)
	}
	if u.state.Initialized {
		return core.State{}, store.ErrAlreadyInitialized
	}
	next, err := core.Migrate(u.state, payload, items, limits)
	if err != nil {
		return core.State{}, err
	}
	u.state = next
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
