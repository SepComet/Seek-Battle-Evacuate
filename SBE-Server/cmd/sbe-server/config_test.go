package main

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"sbe-server/internal/core"
)

// 真实运行时配置：tools/sync_config 从客户端 DataTables 同步到 config 的全套产物。
// 本用例即「服务器启动加载成功」的回归锚——任一路径/字段/校验变动导致启动失败，这里先红。
const (
	realItems        = "../../config/tbitemconfig.json"
	realGlobal       = "../../config/tbglobalconfig.json"
	realDifficulties = "../../config/tbdifficultyconfig.json"
	realMap          = "../../config/tbmapdefinition.json"
	realResources    = "../../config/tbresourcepointconfig.json"
	realParties      = "../../config/tbenemypartyconfig.json"
	serverConfig     = "../../config"
	clientDataTables = "../../../SBE-Client/Assets/GameMain/DataTables"
)

func TestLoadConfigsFromRealExports(t *testing.T) {
	cfg, err := loadConfigs(realItems, realGlobal, realDifficulties, realMap, realResources, realParties)
	if err != nil {
		t.Fatalf("loadConfigs: %v", err)
	}

	// 物品表带稀有度分桶（生成器按稀有度取模选物品）
	if len(cfg.Catalog.Table) == 0 {
		t.Fatal("item table is empty")
	}
	for _, rarity := range core.RarityOrder {
		if len(cfg.Catalog.RarityPool(rarity)) == 0 {
			t.Fatalf("rarity %d pool is empty", rarity)
		}
	}

	// 难度集合来自 tbdifficultyconfig，生成器按 tier 取掉落与威胁权重
	if len(cfg.Limits.Difficulties) == 0 {
		t.Fatal("difficulty set is empty")
	}

	// 固定地图布局：房间与四类点位候选都必须可用
	if len(cfg.Layout.Rooms) == 0 || len(cfg.Layout.ResourcePoints) == 0 ||
		len(cfg.Layout.EnemySpawnPoints) == 0 || len(cfg.Layout.PlayerSpawnPoints) == 0 ||
		len(cfg.Layout.ExtractionPoints) == 0 {
		t.Fatalf("map layout incomplete: %+v", cfg.Layout)
	}

	// 每张地图物资点的类型都必须在资源点表里（loadConfigs 已校验，这里再确认可查询）
	for i, point := range cfg.Layout.ResourcePoints {
		resource, ok := cfg.ResourcePoints.Get(point.ResourcePointID)
		if !ok {
			t.Fatalf("resource point %d: unknown resourcePointId %d", i, point.ResourcePointID)
		}
		// 掉落权重按难度可查，且三档难度都有配置（本原型难度 1~3）
		for difficulty := range cfg.Limits.Difficulties {
			if _, ok := resource.LootFor(difficulty); !ok {
				t.Fatalf("resource point %d: no loot config for difficulty %d", i, difficulty)
			}
		}
	}

	// 敌人点威胁池：难度表的每个非 None 威胁等级都必须有可用队伍，
	// 否则生成器在那档难度下会取到空池（契约 §3：pool 为空即报错）。
	for threat := 1; threat <= 3; threat++ {
		if len(cfg.EnemyParties.Pool(threat)) == 0 {
			t.Fatalf("threat %d party pool is empty", threat)
		}
	}
}

func TestLoadConfigsRejectsUnknownResourcePointID(t *testing.T) {
	// 地图引用了资源点表里不存在的类型 id：应在启动期暴露，而不是生成期才炸。
	dir := t.TempDir()
	mapPath := writeTemp(t, dir, "tbmapdefinition.json", `{
  "rooms": [{"type": 0, "minX": 0, "minY": 0, "maxX": 5, "maxY": 5}],
  "resourcePoints": [{"x": 1, "y": 1, "resourcePointId": 99}],
  "enemySpawnPoints": [{"x": 1, "y": 1}],
  "playerSpawnPoints": [{"x": 1, "y": 1}],
  "extractionPoints": [{"x": 2, "y": 2}]
}`)
	if _, err := loadConfigs(realItems, realGlobal, realDifficulties, mapPath, realResources, realParties); err == nil {
		t.Fatal("want error for unknown resourcePointId")
	}
}

func TestLoadConfigsRejectsEnemyPointOutsideRooms(t *testing.T) {
	// 硬性不变量的启动侧兜底：导出侧已拦，服务器加载时仍要拒绝（契约 §8）。
	dir := t.TempDir()
	mapPath := writeTemp(t, dir, "tbmapdefinition.json", `{
  "rooms": [{"type": 0, "minX": 0, "minY": 0, "maxX": 5, "maxY": 5}],
  "resourcePoints": [{"x": 1, "y": 1, "resourcePointId": 1}],
  "enemySpawnPoints": [{"x": 40, "y": 40}],
  "playerSpawnPoints": [{"x": 1, "y": 1}],
  "extractionPoints": [{"x": 2, "y": 2}]
}`)
	if _, err := loadConfigs(realItems, realGlobal, realDifficulties, mapPath, realResources, realParties); err == nil {
		t.Fatal("want error for enemy spawn point outside every room")
	}
}

func TestConfigSyncedFromClientDataTables(t *testing.T) {
	// 服务器配置面与客户端同源（contracts §8）：config/ 下的三份探索配置必须与
	// DataTables/ 的导出产物一致。改了表或重导地图却忘记跑 tools/sync_config 时，
	// 这条用例先红，避免服务器带着旧配置启动。
	cases := []string{
		"tbmapdefinition.json",
		"tbresourcepointconfig.json",
		"tbenemypartyconfig.json",
	}
	for _, name := range cases {
		t.Run(name, func(t *testing.T) {
			client := readNormalized(t, filepath.Join(clientDataTables, name))
			server := readNormalized(t, filepath.Join(serverConfig, name))
			if client != server {
				t.Fatalf("config/%s 与客户端 DataTables/%s 不一致，请重跑 tools/sync_config", name, name)
			}
		})
	}
}

func readNormalized(t *testing.T, path string) string {
	t.Helper()
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("read %s: %v", path, err)
	}
	return strings.ReplaceAll(string(data), "\r\n", "\n")
}

func writeTemp(t *testing.T, dir, name, content string) string {
	t.Helper()
	path := filepath.Join(dir, name)
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatalf("write %s: %v", name, err)
	}
	return path
}
