// sbe-server：仓库上云 v1 的 WebSocket 服务端入口。
//
// 开发（内存存储，重启丢数据）：
//
//	go run ./cmd/sbe-server -addr 127.0.0.1:7777
//
// 生产（MySQL，需先取驱动依赖并执行 migrations/0001_init.sql）：
//
//	go get github.com/go-sql-driver/mysql
//	go build -tags sbe_mysql -o bin/sbe-server ./cmd/sbe-server
//	./bin/sbe-server -store mysql -dsn "user:pass@tcp(127.0.0.1:3306)/sbe?charset=utf8mb4&parseTime=true"
package main

import (
	"flag"
	"log"
	"net/http"
	"os"

	"sbe-server/internal/boot"
	"sbe-server/internal/itemtable"
	"sbe-server/internal/server"
)

func main() {
	addr := flag.String("addr", "127.0.0.1:7777", "监听地址")
	path := flag.String("path", "/ws", "WebSocket 路径")
	items := flag.String("items", "config/tbitemconfig.json", "Luban 导出的物品表 JSON 路径")
	storeKind := flag.String("store", "memory", "存储类型：memory | mysql")
	dsn := flag.String("dsn", "", "MySQL DSN（-store mysql 时必填）")
	flag.Parse()

	table, err := itemtable.Load(*items)
	if err != nil {
		log.Fatalf("load item table: %v", err)
	}

	st, closeStore, err := boot.OpenStore(*storeKind, *dsn)
	if err != nil {
		log.Fatalf("open store: %v", err)
	}
	defer func() { _ = closeStore() }()

	app := &server.Server{Store: st, Items: table}
	log.Printf("sbe-server listening on ws://%s%s (store=%s, items=%d)", *addr, *path, *storeKind, len(table))

	srv := &http.Server{Addr: *addr, Handler: app.Handler(*path)}
	if err := srv.ListenAndServe(); err != nil {
		log.Printf("server exited: %v", err)
		os.Exit(1)
	}
}
