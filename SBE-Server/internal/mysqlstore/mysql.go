// Package mysqlstore 基于 MySQL 的存储实现（部署用）。
package mysqlstore

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"strings"

	mysql "github.com/go-sql-driver/mysql"

	"sbe-server/internal/core"
	"sbe-server/internal/protocol"
	"sbe-server/internal/store"
)

// mysqlErrDupEntry MySQL ER_DUP_ENTRY：唯一键冲突。
const mysqlErrDupEntry uint16 = 1062

// Store MySQL 存储。
type Store struct {
	db      *sql.DB
	mintUID func() (int64, error) // uid 铸造器（雪花）
	// initialCredits 注册发放的初始信用点（tbglobalconfig.initialCredits）。
	initialCredits int64
}

// Open 打开数据库连接池。mintUID 为 uid 铸造函数（通常注入 snowflake.Node.Next），
// initialCredits 为注册账号发放的初始信用点。
func Open(dsn string, mintUID func() (int64, error), initialCredits int64) (*Store, error) {
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
	return &Store{db: db, mintUID: mintUID, initialCredits: initialCredits}, nil
}

// Close 关闭连接池。
func (s *Store) Close() error { return s.db.Close() }

// Register 实现 store.Store：校验凭据、铸 uid、落账号与 token。
// 用户名唯一由 uk_users_username 兜底，冲突映射为 ErrUsernameTaken。
func (s *Store) Register(ctx context.Context, username, password string) (int64, string, error) {
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

	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return 0, "", err
	}
	defer func() { _ = tx.Rollback() }()

	if _, err := tx.ExecContext(ctx,
		`INSERT INTO users (uid, username, password_hash, credits) VALUES (?,?,?,?)`, uid, name, hash, s.initialCredits); err != nil {
		var me *mysql.MySQLError
		if errors.As(err, &me) && me.Number == mysqlErrDupEntry {
			return 0, "", store.ErrUsernameTaken
		}
		return 0, "", err
	}
	if _, err := tx.ExecContext(ctx,
		`INSERT INTO auth_tokens (token, user_id) VALUES (?, ?)`, token, uid); err != nil {
		return 0, "", err
	}
	if err := tx.Commit(); err != nil {
		return 0, "", err
	}
	return uid, token, nil
}

// Login 实现 store.Store：校验口令后铸造并落 token。
// 用户不存在与密码错误统一返回 ErrInvalidCredentials，防用户名枚举。
func (s *Store) Login(ctx context.Context, username, password string) (int64, string, error) {
	name := strings.TrimSpace(username)
	token, err := store.NewToken()
	if err != nil {
		return 0, "", err
	}

	var uid int64
	var storedHash string
	err = s.db.QueryRowContext(ctx,
		`SELECT uid, password_hash FROM users WHERE username = ?`, name).Scan(&uid, &storedHash)
	if errors.Is(err, sql.ErrNoRows) {
		return 0, "", store.ErrInvalidCredentials
	}
	if err != nil {
		return 0, "", err
	}
	if !store.VerifyPassword(storedHash, password) {
		return 0, "", store.ErrInvalidCredentials
	}

	if _, err := s.db.ExecContext(ctx,
		`INSERT INTO auth_tokens (token, user_id) VALUES (?, ?)`, token, uid); err != nil {
		return 0, "", err
	}
	return uid, token, nil
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

// Load 实现 store.Store。只读快照不加锁：pull 无需与在途写互斥，
// 读到写事务提交前的时点状态对客户端是合法视图，不应为此排队。
func (s *Store) Load(ctx context.Context, userID int64) (core.State, error) {
	return s.load(ctx, s.db, userID, false)
}

// loadForUpdate 在事务内读取用户状态并对 users 行加排他锁（当前读），
// 作为同用户读-改-写的串行化点：并发写事务在此排队，拿到锁即前任已提交。
// 事务内若在本调用之前已有普通 SELECT（一致性读），REPEATABLE READ 会沿用
// 更早建立的快照导致锁后仍读到旧数据——此时须先用 lockUser 拿锁。
func (s *Store) loadForUpdate(ctx context.Context, q queryer, userID int64) (core.State, error) {
	return s.load(ctx, q, userID, true)
}

func (s *Store) load(ctx context.Context, q queryer, userID int64, forUpdate bool) (core.State, error) {
	state := core.State{}

	lock := ""
	if forUpdate {
		lock = " FOR UPDATE"
	}
	if err := q.QueryRowContext(ctx,
		`SELECT initialized, credits, COALESCE((SELECT MAX(uid) FROM warehouse_items WHERE user_id = ?), 0) FROM users WHERE uid = ?`+lock,
		userID, userID).Scan(&state.Initialized, &state.Credits, &state.NextUID); err != nil {
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
	if err := chars.Err(); err != nil {
		return core.State{}, err
	}

	// 战备配置（v2 起才有行；缺失 = 未配置）
	var difficulty int
	var partyJSON, carriedJSON sql.NullString
	err = q.QueryRowContext(ctx,
		`SELECT difficulty, party_json, carried_json FROM user_loadout WHERE user_id = ?`, userID).
		Scan(&difficulty, &partyJSON, &carriedJSON)
	if err == nil {
		state.Difficulty = difficulty
		if partyJSON.Valid && partyJSON.String != "" && partyJSON.String != "null" {
			if err := json.Unmarshal([]byte(partyJSON.String), &state.Party); err != nil {
				return core.State{}, fmt.Errorf("parse party_json: %w", err)
			}
		}
		if carriedJSON.Valid && carriedJSON.String != "" && carriedJSON.String != "null" {
			var carried []core.Stack
			if err := json.Unmarshal([]byte(carriedJSON.String), &carried); err != nil {
				return core.State{}, fmt.Errorf("parse carried_json: %w", err)
			}
			state.Carried = carried
		}
	} else if !errors.Is(err, sql.ErrNoRows) {
		return core.State{}, err
	}
	return state, nil
}

// rewrite 全量重写用户经济行（在小规模数据量下简单可靠）。
// 注意必须显式写回 uid：UID 是服务器铸造的、按用户独立的稳定身份（0003 复合主键），
// 客户端会引用它，重排或重新铸号会使已下发的 uid 全部失效。
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
			`INSERT INTO warehouse_items (uid, user_id, item_id, count, x, y, is_rotated, wearer) VALUES (?,?,?,?,?,?,?,?)`,
			r.UID, userID, r.ItemID, r.Count, r.X, r.Y, rotated, r.Wearer); err != nil {
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

	partyJSON, err := json.Marshal(state.Party)
	if err != nil {
		return err
	}
	carried := make([]protocol.Stack, len(state.Carried))
	for i, st := range state.Carried {
		carried[i] = protocol.Stack{ItemID: st.ItemID, Count: st.Count}
	}
	carriedJSON, err := json.Marshal(carried)
	if err != nil {
		return err
	}
	if _, err := tx.ExecContext(ctx,
		`INSERT INTO user_loadout (user_id, difficulty, party_json, carried_json) VALUES (?,?,?,?)
		 ON DUPLICATE KEY UPDATE difficulty = VALUES(difficulty), party_json = VALUES(party_json), carried_json = VALUES(carried_json)`,
		userID, state.Difficulty, string(partyJSON), string(carriedJSON)); err != nil {
		return err
	}
	return nil
}

// lockUser 锁定用户行（当前读），必须是写事务的第一条语句：
// 之后的一致性读才会以持锁后的数据建立快照，避免 REPEATABLE READ 读到锁前旧数据。
func (s *Store) lockUser(ctx context.Context, tx *sql.Tx, userID int64) error {
	var id int64
	if err := tx.QueryRowContext(ctx,
		`SELECT uid FROM users WHERE uid = ? FOR UPDATE`, userID).Scan(&id); err != nil {
		return fmt.Errorf("lock user %d: %w", userID, err)
	}
	return nil
}

// ApplySettle 实现 store.Store。
// runId 全局唯一（settlements 主键）：同用户重复提交回放首次快照；
// 已被其他用户占用的 runId 拒绝，防止借幂等回放读取他人快照。
func (s *Store) ApplySettle(ctx context.Context, userID int64, runID string, meta core.SettleMeta, ops []protocol.Op, items core.ItemTable, limits core.Limits) (core.State, bool, error) {
	if runID == "" || len(runID) > 64 {
		return core.State{}, false, fmt.Errorf("%w: invalid runId", core.ErrRejected)
	}

	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return core.State{}, false, err
	}
	defer func() { _ = tx.Rollback() }()

	// 先锁用户行再做幂等检查：幂等检查本身是一致性读，
	// 锁在其之前才能保证后续 loadForUpdate 读到的是持锁后的最新状态。
	if err := s.lockUser(ctx, tx, userID); err != nil {
		return core.State{}, false, err
	}

	var snapshotJSON string
	err = tx.QueryRowContext(ctx,
		`SELECT snapshot_json FROM settlements WHERE run_id = ? AND user_id = ?`, runID, userID).Scan(&snapshotJSON)
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
	next, err := core.Apply(state, ops, items, limits)
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
		`INSERT INTO settlements (run_id, user_id, outcome, difficulty, seed, started_at, ended_at, snapshot_json) VALUES (?,?,?,?,?,?,?,?)`,
		runID, userID, meta.Outcome, meta.Difficulty, meta.Seed, meta.StartedAt, meta.EndedAt, string(snapJSON)); err != nil {
		var me *mysql.MySQLError
		if errors.As(err, &me) && me.Number == mysqlErrDupEntry {
			// 同用户重复已被幂等检查拦截，主键冲突只剩 runId 被其他用户占用一种来源。
			return core.State{}, false, fmt.Errorf("%w: runId %s already used by another user", core.ErrRejected, runID)
		}
		return core.State{}, false, err
	}
	if err := tx.Commit(); err != nil {
		return core.State{}, false, err
	}
	return next, false, nil
}

// ApplyOps 实现 store.Store。
func (s *Store) ApplyOps(ctx context.Context, userID int64, ops []protocol.Op, items core.ItemTable, limits core.Limits) (core.State, error) {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return core.State{}, err
	}
	defer func() { _ = tx.Rollback() }()

	state, err := s.loadForUpdate(ctx, tx, userID)
	if err != nil {
		return core.State{}, err
	}
	next, err := core.Apply(state, ops, items, limits)
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
func (s *Store) Migrate(ctx context.Context, userID int64, payload protocol.MigratePayload, items core.ItemTable, limits core.Limits) (core.State, error) {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return core.State{}, err
	}
	defer func() { _ = tx.Rollback() }()

	var initialized bool
	if err := tx.QueryRowContext(ctx,
		`SELECT initialized FROM users WHERE uid = ? FOR UPDATE`, userID).Scan(&initialized); err != nil {
		return core.State{}, err
	}
	if initialized {
		return core.State{}, store.ErrAlreadyInitialized
	}

	state, err := s.loadForUpdate(ctx, tx, userID)
	if err != nil {
		return core.State{}, err
	}
	next, err := core.Migrate(state, payload, items, limits)
	if err != nil {
		return core.State{}, err
	}
	if err := rewrite(ctx, tx, userID, next); err != nil {
		return core.State{}, err
	}
	if _, err := tx.ExecContext(ctx, `UPDATE users SET initialized = 1 WHERE uid = ?`, userID); err != nil {
		return core.State{}, err
	}
	if err := tx.Commit(); err != nil {
		return core.State{}, err
	}
	return next, nil
}

// History 实现 store.Store。
func (s *Store) History(ctx context.Context, userID int64, limit int) ([]protocol.SnapRun, error) {
	rows, err := s.db.QueryContext(ctx,
		`SELECT run_id, outcome, difficulty, seed, started_at, ended_at
		 FROM settlements WHERE user_id = ?
		 ORDER BY started_at DESC, run_id DESC LIMIT ?`, userID, limit)
	if err != nil {
		return nil, err
	}
	defer func() { _ = rows.Close() }()
	out := make([]protocol.SnapRun, 0)
	for rows.Next() {
		var r protocol.SnapRun
		if err := rows.Scan(&r.RunID, &r.Outcome, &r.Difficulty, &r.Seed, &r.StartedAt, &r.EndedAt); err != nil {
			return nil, err
		}
		out = append(out, r)
	}
	return out, rows.Err()
}

type queryer interface {
	QueryContext(ctx context.Context, query string, args ...any) (*sql.Rows, error)
	QueryRowContext(ctx context.Context, query string, args ...any) *sql.Row
}

func snapshotToState(snap protocol.Snapshot) core.State {
	state := core.State{Initialized: snap.Initialized, Credits: snap.Credits}
	for _, r := range snap.Items {
		state.Rows = append(state.Rows, core.Row{
			UID: r.UID, ItemID: r.ItemID, Count: r.Count,
			X: r.X, Y: r.Y, Rotated: r.R, Wearer: r.Wearer,
		})
	}
	for _, c := range snap.Characters {
		state.Characters = append(state.Characters, core.Character{CID: c.CID})
	}
	state.Party = snap.Party
	state.Difficulty = snap.Difficulty
	for _, st := range snap.Carried {
		state.Carried = append(state.Carried, core.Stack{ItemID: st.ItemID, Count: st.Count})
	}
	state.NextUID = int64(len(state.Rows))
	for _, r := range state.Rows {
		if r.UID > state.NextUID {
			state.NextUID = r.UID
		}
	}
	return state
}
