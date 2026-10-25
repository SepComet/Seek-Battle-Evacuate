//go:build sbe_mysql
// +build sbe_mysql

// Package mysqlstore 基于 MySQL 的存储实现（部署用）。
// 需要网络获取驱动后以 -tags sbe_mysql 构建，见 README。
package mysqlstore

import (
	"context"
	"crypto/rand"
	"database/sql"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"

	_ "github.com/go-sql-driver/mysql"

	"sbe-server/internal/core"
	"sbe-server/internal/protocol"
	"sbe-server/internal/store"
)

// Store MySQL 存储。
type Store struct {
	db *sql.DB
}

// Open 打开数据库连接池。
func Open(dsn string) (*Store, error) {
	db, err := sql.Open("mysql", dsn)
	if err != nil {
		return nil, err
	}
	db.SetMaxOpenConns(16)
	db.SetMaxIdleConns(8)
	if err := db.Ping(); err != nil {
		_ = db.Close()
		return nil, fmt.Errorf("mysql ping: %w", err)
	}
	return &Store{db: db}, nil
}

// Close 关闭连接池。
func (s *Store) Close() error { return s.db.Close() }

func newToken() (string, error) {
	var raw [16]byte
	if _, err := rand.Read(raw[:]); err != nil {
		return "", err
	}
	return hex.EncodeToString(raw[:]), nil
}

// Register 实现 store.Store。
func (s *Store) Register(ctx context.Context, device string) (int64, string, error) {
	if device == "" || len(device) > 64 {
		return 0, "", fmt.Errorf("%w: invalid device", core.ErrRejected)
	}
	token, err := newToken()
	if err != nil {
		return 0, "", err
	}

	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return 0, "", err
	}
	defer func() { _ = tx.Rollback() }()

	var userID int64
	err = tx.QueryRowContext(ctx,
		`SELECT id FROM users WHERE device_uuid = ? FOR UPDATE`, device).Scan(&userID)
	if errors.Is(err, sql.ErrNoRows) {
		res, err := tx.ExecContext(ctx,
			`INSERT INTO users (device_uuid) VALUES (?)`, device)
		if err != nil {
			return 0, "", err
		}
		userID, err = res.LastInsertId()
		if err != nil {
			return 0, "", err
		}
	} else if err != nil {
		return 0, "", err
	}

	if _, err := tx.ExecContext(ctx,
		`INSERT INTO auth_tokens (token, user_id) VALUES (?, ?)`, token, userID); err != nil {
		return 0, "", err
	}
	if err := tx.Commit(); err != nil {
		return 0, "", err
	}
	return userID, token, nil
}

// Auth 实现 store.Store。
func (s *Store) Auth(ctx context.Context, token string) (int64, error) {
	var userID int64
	err := s.db.QueryRowContext(ctx,
		`SELECT user_id FROM auth_tokens WHERE token = ? AND (expires_at IS NULL OR expires_at > NOW())`,
		token).Scan(&userID)
	if errors.Is(err, sql.ErrNoRows) {
		return 0, store.ErrUnknownToken
	}
	return userID, err
}

// Load 实现 store.Store。
func (s *Store) Load(ctx context.Context, userID int64) (core.State, error) {
	state, err := s.loadForUpdate(ctx, s.db, userID)
	return state, err
}

func (s *Store) loadForUpdate(ctx context.Context, q queryer, userID int64) (core.State, error) {
	state := core.State{}

	if err := q.QueryRowContext(ctx,
		`SELECT initialized, COALESCE((SELECT MAX(uid) FROM warehouse_items WHERE user_id = ?), 0) FROM users WHERE id = ?`,
		userID, userID).Scan(&state.Initialized, &state.NextUID); err != nil {
		return core.State{}, fmt.Errorf("load user: %w", err)
	}

	rows, err := q.QueryContext(ctx,
		`SELECT uid, item_id, count, x, y, is_rotated, wearer FROM warehouse_items WHERE user_id = ?`, userID)
	if err != nil {
		return core.State{}, err
	}
	defer func() { _ = rows.Close() }()
	for rows.Next() {
		var r core.Row
		var rotated int
		if err := rows.Scan(&r.UID, &r.ItemID, &r.Count, &r.X, &r.Y, &rotated, &r.Wearer); err != nil {
			return core.State{}, err
		}
		r.Rotated = rotated != 0
		state.Rows = append(state.Rows, r)
	}
	if err := rows.Err(); err != nil {
		return core.State{}, err
	}

	chars, err := q.QueryContext(ctx,
		`SELECT character_id FROM user_characters WHERE user_id = ? ORDER BY sort_order, character_id`, userID)
	if err != nil {
		return core.State{}, err
	}
	defer func() { _ = chars.Close() }()
	for chars.Next() {
		var cid int
		if err := chars.Scan(&cid); err != nil {
			return core.State{}, err
		}
		state.Characters = append(state.Characters, core.Character{CID: cid})
	}
	return state, chars.Err()
}

// rewrite 全量重写用户经济行（在小规模数据量下简单可靠）。
func rewrite(ctx context.Context, tx *sql.Tx, userID int64, state core.State) error {
	if _, err := tx.ExecContext(ctx, `DELETE FROM warehouse_items WHERE user_id = ?`, userID); err != nil {
		return err
	}
	for _, r := range state.Rows {
		rotated := 0
		if r.Rotated {
			rotated = 1
		}
		if _, err := tx.ExecContext(ctx,
			`INSERT INTO warehouse_items (user_id, item_id, count, x, y, is_rotated, wearer) VALUES (?,?,?,?,?,?,?)`,
			userID, r.ItemID, r.Count, r.X, r.Y, rotated, r.Wearer); err != nil {
			return err
		}
	}
	if _, err := tx.ExecContext(ctx, `DELETE FROM user_characters WHERE user_id = ?`, userID); err != nil {
		return err
	}
	for i, c := range state.Characters {
		if _, err := tx.ExecContext(ctx,
			`INSERT INTO user_characters (user_id, character_id, sort_order) VALUES (?,?,?)`,
			userID, c.CID, i); err != nil {
			return err
		}
	}
	return nil
}

// ApplySettle 实现 store.Store。
func (s *Store) ApplySettle(ctx context.Context, userID int64, runID string, outcome int, ops []protocol.Op, items core.ItemTable) (core.State, bool, error) {
	if runID == "" || len(runID) > 64 {
		return core.State{}, false, fmt.Errorf("%w: invalid runId", core.ErrRejected)
	}

	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return core.State{}, false, err
	}
	defer func() { _ = tx.Rollback() }()

	var snapshotJSON string
	err = tx.QueryRowContext(ctx,
		`SELECT snapshot_json FROM settlements WHERE run_id = ?`, runID).Scan(&snapshotJSON)
	if err == nil {
		var snap protocol.Snapshot
		if err := json.Unmarshal([]byte(snapshotJSON), &snap); err != nil {
			return core.State{}, false, fmt.Errorf("replay settlement %s: %w", runID, err)
		}
		return snapshotToState(snap), true, nil
	}
	if !errors.Is(err, sql.ErrNoRows) {
		return core.State{}, false, err
	}

	state, err := s.loadForUpdate(ctx, tx, userID)
	if err != nil {
		return core.State{}, false, err
	}
	next, err := core.Apply(state, ops, items)
	if err != nil {
		return core.State{}, false, err
	}
	if err := rewrite(ctx, tx, userID, next); err != nil {
		return core.State{}, false, err
	}
	snapJSON, err := json.Marshal(next.ToSnapshot())
	if err != nil {
		return core.State{}, false, err
	}
	if _, err := tx.ExecContext(ctx,
		`INSERT INTO settlements (run_id, user_id, outcome, snapshot_json) VALUES (?,?,?,?)`,
		runID, userID, outcome, string(snapJSON)); err != nil {
		return core.State{}, false, err
	}
	if err := tx.Commit(); err != nil {
		return core.State{}, false, err
	}
	return next, false, nil
}

// ApplyOps 实现 store.Store。
func (s *Store) ApplyOps(ctx context.Context, userID int64, ops []protocol.Op, items core.ItemTable) (core.State, error) {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return core.State{}, err
	}
	defer func() { _ = tx.Rollback() }()

	state, err := s.loadForUpdate(ctx, tx, userID)
	if err != nil {
		return core.State{}, err
	}
	next, err := core.Apply(state, ops, items)
	if err != nil {
		return core.State{}, err
	}
	if err := rewrite(ctx, tx, userID, next); err != nil {
		return core.State{}, err
	}
	if err := tx.Commit(); err != nil {
		return core.State{}, err
	}
	return next, nil
}

// Migrate 实现 store.Store。
func (s *Store) Migrate(ctx context.Context, userID int64, payload protocol.MigratePayload, items core.ItemTable) (core.State, error) {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return core.State{}, err
	}
	defer func() { _ = tx.Rollback() }()

	var initialized bool
	if err := tx.QueryRowContext(ctx,
		`SELECT initialized FROM users WHERE id = ? FOR UPDATE`, userID).Scan(&initialized); err != nil {
		return core.State{}, err
	}
	if initialized {
		return core.State{}, store.ErrAlreadyInitialized
	}

	state, err := s.loadForUpdate(ctx, tx, userID)
	if err != nil {
		return core.State{}, err
	}
	next, err := core.Migrate(state, payload, items)
	if err != nil {
		return core.State{}, err
	}
	if err := rewrite(ctx, tx, userID, next); err != nil {
		return core.State{}, err
	}
	if _, err := tx.ExecContext(ctx, `UPDATE users SET initialized = 1 WHERE id = ?`, userID); err != nil {
		return core.State{}, err
	}
	if err := tx.Commit(); err != nil {
		return core.State{}, err
	}
	return next, nil
}

type queryer interface {
	QueryContext(ctx context.Context, query string, args ...any) (*sql.Rows, error)
	QueryRowContext(ctx context.Context, query string, args ...any) *sql.Row
}

func snapshotToState(snap protocol.Snapshot) core.State {
	state := core.State{Initialized: snap.Initialized}
	for _, r := range snap.Items {
		state.Rows = append(state.Rows, core.Row{
			UID: r.UID, ItemID: r.ItemID, Count: r.Count,
			X: r.X, Y: r.Y, Rotated: r.R, Wearer: r.Wearer,
		})
	}
	for _, c := range snap.Characters {
		state.Characters = append(state.Characters, core.Character{CID: c.CID})
	}
	state.NextUID = int64(len(state.Rows))
	for _, r := range state.Rows {
		if r.UID > state.NextUID {
			state.NextUID = r.UID
		}
	}
	return state
}
