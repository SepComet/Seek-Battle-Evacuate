// Package store 定义经济状态的持久化接口与错误。
// 实现方负责并发安全与事务性；核心逻辑在 internal/core（纯函数）。
package store

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"errors"

	"sbe-server/internal/core"
	"sbe-server/internal/protocol"
)

// ErrUnknownToken token 不存在或已过期。
var ErrUnknownToken = errors.New("store: unknown token")

// ErrAlreadyInitialized 账号已完成初始化，拒绝再次迁移。
var ErrAlreadyInitialized = errors.New("store: already initialized")

// ErrUsernameTaken 注册时用户名已被占用。
var ErrUsernameTaken = errors.New("store: username taken")

// ErrInvalidCredentials 登录时用户名不存在或密码错误（统一口径，防用户名枚举）。
var ErrInvalidCredentials = errors.New("store: invalid credentials")

// NewToken 生成随机登录令牌（32 位十六进制）。
func NewToken() (string, error) {
	var raw [16]byte
	if _, err := rand.Read(raw[:]); err != nil {
		return "", err
	}
	return hex.EncodeToString(raw[:]), nil
}

// Store 经济状态存储接口。所有方法须线程安全。
type Store interface {
	// Register 以用户名+密码注册新账号，返回新铸造的 uid 与 token。
	// 用户名格式非法返回 ErrRejected（core），已被占用返回 ErrUsernameTaken。
	Register(ctx context.Context, username, password string) (uid int64, token string, err error)

	// Login 校验用户名与密码，返回新铸造的 token。
	// 用户不存在与密码错误统一返回 ErrInvalidCredentials。
	Login(ctx context.Context, username, password string) (uid int64, token string, err error)

	// Auth 校验 token，返回所属用户。
	Auth(ctx context.Context, token string) (userID int64, err error)

	// Load 读取用户完整局外状态。
	Load(ctx context.Context, userID int64) (core.State, error)

	// ApplySettle 应用一次结算。runId 全局唯一：同用户重复提交返回首次结果与 already=true；
	// runId 已被其他用户占用时返回 rejected（回放按归属过滤，防止读取他人快照）。
	// meta 落战绩审计列（幂等命中时不重复写）。
	ApplySettle(ctx context.Context, userID int64, runID string, meta core.SettleMeta, ops []protocol.Op, items core.ItemTable, limits core.Limits) (state core.State, already bool, err error)

	// ApplyOps 应用一批大厅操作（无 runId 幂等，幂等由会话层 rid 缓存负责）。
	ApplyOps(ctx context.Context, userID int64, ops []protocol.Op, items core.ItemTable, limits core.Limits) (core.State, error)

	// Migrate 初始化用户状态；已初始化时返回 ErrAlreadyInitialized。
	Migrate(ctx context.Context, userID int64, payload protocol.MigratePayload, items core.ItemTable, limits core.Limits) (core.State, error)

	// History 返回用户最近战绩，按 startedAt 倒序，最多 limit 条。
	History(ctx context.Context, userID int64, limit int) ([]protocol.SnapRun, error)
}
