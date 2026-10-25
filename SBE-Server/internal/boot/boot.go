// Package boot 根据启动参数装配存储实现。
package boot

import (
	"fmt"

	"sbe-server/internal/memorystore"
	"sbe-server/internal/store"
)

// OpenStore 打开指定类型的存储。默认（memory）单机开发用，数据不落盘。
func OpenStore(kind, dsn string) (st store.Store, closeFn func() error, err error) {
	switch kind {
	case "", "memory":
		return memorystore.New(), func() error { return nil }, nil
	default:
		return nil, nil, fmt.Errorf("unknown store kind %q (mysql 需以 -tags sbe_mysql 构建)", kind)
	}
}
