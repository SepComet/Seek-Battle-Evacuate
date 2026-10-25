// sbe-server：局外状态上云的 WebSocket 服务端入口。
//
// 开发（内存存储，重启丢数据）：
//
//	go run ./cmd/sbe-server -addr 127.0.0.1:7777
//
// 生产（MySQL，先执行 migrations/0001_init.sql 与 0002_loadout.sql）：
//
//	go build -o bin/sbe-server ./cmd/sbe-server
//	./bin/sbe-server -store mysql -dsn "user:pass@tcp(127.0.0.1:3306)/sbe?charset=utf8mb4&parseTime=true"
package main

import (
	"flag"
	"log"
	"net/http"
	"os"

	"sbe-server/internal/boot"
	"sbe-server/internal/globaltable"
	"sbe-server/internal/itemtable"
	"sbe-server/internal/server"
)

func main() {
	addr := flag.String("addr", "127.0.0.1:7777", "监听地址")
	path := flag.String("path", "/ws", "WebSocket 路径")
	items := flag.String("items", "config/tbitemconfig.json", "Luban 导出的物品表 JSON 路径")
	global := flag.String("global", "config/tbglobalconfig.json", "Luban 导出的全局配置 JSON 路径")
	diffs := flag.String("difficulties", "config/tbdifficultyconfig.json", "Luban 导出的难度表 JSON 路径")
	storeKind := flag.String("store", "memory", "存储类型：memory | mysql")
	dsn := flag.String("dsn", "", "MySQL DSN（-store mysql 时必填）")
	flag.Parse()

	table, err := itemtable.Load(*items)
	if err != nil {
		log.Fatalf("load item table: %v", err)
	}

	limits, err := globaltable.Load(*global, *diffs)
	if err != nil {
		log.Fatalf("load global table: %v", err)
	}

	st, closeStore, err := boot.OpenStore(*storeKind, *dsn)
	if err != nil {
		log.Fatalf("open store: %v", err)
	}
	defer func() { _ = closeStore() }()

	app := &server.Server{Store: st, Items: table, Limits: limits}
	log.Printf("sbe-server listening on ws://%s%s (store=%s, items=%d, maxParty=%d)",
		*addr, *path, *storeKind, len(table), limits.MaxPlayerPartySize)

	srv := &http.Server{Addr: *addr, Handler: app.Handler(*path)}
	if err := srv.ListenAndServe(); err != nil {
		log.Printf("server exited: %v", err)
		os.Exit(1)
	}
}
