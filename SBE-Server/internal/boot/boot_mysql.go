//go:build sbe_mysql
// +build sbe_mysql

package boot

import (
	"fmt"

	"sbe-server/internal/memorystore"
	"sbe-server/internal/mysqlstore"
	"sbe-server/internal/store"
)

// OpenStore 打开指定类型的存储；mysql 需要有效 dsn。
func OpenStore(kind, dsn string) (st store.Store, closeFn func() error, err error) {
	switch kind {
	case "", "memory":
		return memorystore.New(), func() error { return nil }, nil
	case "mysql":
		if dsn == "" {
			return nil, nil, fmt.Errorf("mysql store requires -dsn")
		}
		ms, err := mysqlstore.Open(dsn)
		if err != nil {
			return nil, nil, err
		}
		return ms, ms.Close, nil
	default:
		return nil, nil, fmt.Errorf("unknown store kind %q", kind)
	}
}
