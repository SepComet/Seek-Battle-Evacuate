// Package memorystore 进程内存储实现：单机开发与测试用。
package memorystore

import (
	"context"
	"fmt"
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
	settles       map[string]core.State // runId -> 首次结算后的状态（幂等命中时返回）
	nextUserID    int64
}

// New 创建内存存储。
func New() *Store {
	return &Store{
		usersByDevice: make(map[string]int64),
		usersByID:     make(map[int64]*user),
		tokens:        make(map[string]int64),
		settles:       make(map[string]core.State),
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
func (s *Store) ApplySettle(_ context.Context, userID int64, runID string, outcome int, ops []protocol.Op, items core.ItemTable) (core.State, bool, error) {
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

	next, err := core.Apply(u.state, ops, items)
	if err != nil {
		return core.State{}, false, err
	}
	u.state = next
	s.settles[runID] = next
	_ = outcome // 结局仅记录用途（审计），内存实现不落库
	return next.Clone(), false, nil
}

// ApplyOps 实现 store.Store。
func (s *Store) ApplyOps(_ context.Context, userID int64, ops []protocol.Op, items core.ItemTable) (core.State, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	u, ok := s.usersByID[userID]
	if !ok {
		return core.State{}, fmt.Errorf("store: unknown user %d", userID)
	}
	next, err := core.Apply(u.state, ops, items)
	if err != nil {
		return core.State{}, err
	}
	u.state = next
	return next.Clone(), nil
}

// Migrate 实现 store.Store。
func (s *Store) Migrate(_ context.Context, userID int64, payload protocol.MigratePayload, items core.ItemTable) (core.State, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	u, ok := s.usersByID[userID]
	if !ok {
		return core.State{}, fmt.Errorf("store: unknown user %d", userID)
	}
	if u.state.Initialized {
		return core.State{}, store.ErrAlreadyInitialized
	}
	next, err := core.Migrate(u.state, payload, items)
	if err != nil {
		return core.State{}, err
	}
	u.state = next
	return next.Clone(), nil
}
