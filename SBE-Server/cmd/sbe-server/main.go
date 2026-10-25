// sbe-server：局外状态上云的 WebSocket 服务端入口。
//
// 开发（内存存储，重启丢数据）：
//
//	go run ./cmd/sbe-server -addr 127.0.0.1:7777
//
// 生产（MySQL，先依次执行 migrations/0001_init.sql、0002_loadout.sql、
// 0003_warehouse_uid_per_user.sql、0004_account_auth.sql）：
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
	"sbe-server/internal/server"
	"sbe-server/internal/snowflake"
)

func main() {
	addr := flag.String("addr", "127.0.0.1:7777", "监听地址")
	path := flag.String("path", "/ws", "WebSocket 路径")
	items := flag.String("items", "config/tbitemconfig.json", "Luban 导出的物品表 JSON 路径")
	global := flag.String("global", "config/tbglobalconfig.json", "Luban 导出的全局配置 JSON 路径")
	diffs := flag.String("difficulties", "config/tbdifficultyconfig.json", "Luban 导出的难度表 JSON 路径")
	mapPath := flag.String("map", "config/tbmapdefinition.json", "编辑器导出的地图定义 JSON 路径")
	resourcePoints := flag.String("resourcepoints", "config/tbresourcepointconfig.json", "Luban 导出的物资点配置 JSON 路径")
	enemyParties := flag.String("enemyparties", "config/tbenemypartyconfig.json", "Luban 导出的敌队伍配置 JSON 路径")
	storeKind := flag.String("store", "memory", "存储类型：memory | mysql")
	dsn := flag.String("dsn", "", "MySQL DSN（-store mysql 时必填）")
	workerID := flag.Int64("worker-id", 1, "雪花 uid 铸号的 worker id（0~1023，多实例部署必须互异）")
	flag.Parse()

	cfg, err := loadConfigs(*items, *global, *diffs, *mapPath, *resourcePoints, *enemyParties)
	if err != nil {
		log.Fatalf("%v", err)
	}
	table := cfg.Catalog.Table

	uidGen, err := snowflake.New(*workerID)
	if err != nil {
		log.Fatalf("snowflake: %v", err)
	}

	st, closeStore, err := boot.OpenStore(*storeKind, *dsn, uidGen)
	if err != nil {
		log.Fatalf("open store: %v", err)
	}
	defer func() { _ = closeStore() }()

	app := &server.Server{Store: st, Items: table, Limits: cfg.Limits}
	log.Printf("sbe-server listening on ws://%s%s (store=%s, items=%d, maxParty=%d, worker=%d)",
		*addr, *path, *storeKind, len(table), cfg.Limits.MaxPlayerPartySize, *workerID)
	log.Printf("exploration content: rooms=%d resourcePoints=%d enemySpawnPoints=%d playerSpawns=%d extractions=%d",
		len(cfg.Layout.Rooms), len(cfg.Layout.ResourcePoints), len(cfg.Layout.EnemySpawnPoints),
		len(cfg.Layout.PlayerSpawnPoints), len(cfg.Layout.ExtractionPoints))
	log.Printf("exploration tables: resourcePointTypes=%d enemyPartyThreats=%d",
		cfg.ResourcePoints.Len(), cfg.EnemyParties.ThreatCount())

	srv := &http.Server{Addr: *addr, Handler: app.Handler(*path)}
	if err := srv.ListenAndServe(); err != nil {
		log.Printf("server exited: %v", err)
		os.Exit(1)
	}
}
