// Package mapdef 从编辑器导出的 tbmapdefinition.json 加载固定地图布局：
// 房间矩形与物资点/敌人点/出生点/撤离点的候选列表。
//
// 数据源与客户端同源——地图资产经 SepCore.Editor 的
// Utility/Map/Export Map Definition JSON 菜单导出为 JSON，
// 再由 tools/sync_config 同步到服务器 config 目录；服务器据此重建固定布局。
// 加载时校验硬性不变量：每个敌人点必须归属某个房间（违反即 error），
// 且房间与四类点位候选列表均不得为空。
package mapdef

import (
	"encoding/json"
	"fmt"
	"os"
)

// Vec2 瓦片坐标。
type Vec2 struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
}

// Room 房间矩形，覆盖瓦片闭区间 [MinX,MaxX] x [MinY,MaxY]。
// Type 为客户端 RoomType 枚举值：0=普通房间，1=走廊。
type Room struct {
	Type int     `json:"type"`
	MinX float64 `json:"minX"`
	MinY float64 `json:"minY"`
	MaxX float64 `json:"maxX"`
	MaxY float64 `json:"maxY"`
}

// Contains 判定点是否落在房间覆盖的瓦片范围内。
// 与客户端 RoomDefinition.Contains 逐字一致：房间覆盖瓦片闭区间 [Min,Max]，
// 瓦片 (x,y) 占据 [x,x+1)，因此判定用半开区间 [Min, Max+1)。
func (r Room) Contains(p Vec2) bool {
	return p.X >= r.MinX && p.X < r.MaxX+1 &&
		p.Y >= r.MinY && p.Y < r.MaxY+1
}

// ResourcePoint 物资点：瓦片坐标 + 物资点类型 id（对应 tbresourcepointconfig 的 id）。
type ResourcePoint struct {
	X               float64 `json:"x"`
	Y               float64 `json:"y"`
	ResourcePointID int     `json:"resourcePointId"`
}

// Position 返回物资点的瓦片坐标。
func (p ResourcePoint) Position() Vec2 {
	return Vec2{X: p.X, Y: p.Y}
}

// Layout 固定地图布局。各类点位列表的索引即客户端点位 ID 的下标
// （物资点 "r<i>"、敌点 "e<i>"，见跨票契约 §6），因此列表顺序不可重排。
type Layout struct {
	Rooms             []Room          `json:"rooms"`
	ResourcePoints    []ResourcePoint `json:"resourcePoints"`
	EnemySpawnPoints  []Vec2          `json:"enemySpawnPoints"`
	PlayerSpawnPoints []Vec2          `json:"playerSpawnPoints"`
	ExtractionPoints  []Vec2          `json:"extractionPoints"`
}

// RoomIndexAt 返回坐标所在房间的下标（列表序），不在任何房间内返回 -1。
// 重叠时取列表序最小的房间，生成侧据此记录敌人点的房间归属。
func (l *Layout) RoomIndexAt(p Vec2) int {
	for i := range l.Rooms {
		if l.Rooms[i].Contains(p) {
			return i
		}
	}
	return -1
}

// Validate 校验导出产物的硬性不变量，违反时返回 error。
func (l *Layout) Validate() error {
	if len(l.Rooms) == 0 {
		return fmt.Errorf("no rooms")
	}
	for i, room := range l.Rooms {
		if room.MaxX < room.MinX || room.MaxY < room.MinY {
			return fmt.Errorf("room %d: max bound is less than min bound", i)
		}
	}
	if len(l.ResourcePoints) == 0 {
		return fmt.Errorf("no resource points")
	}
	if len(l.EnemySpawnPoints) == 0 {
		return fmt.Errorf("no enemy spawn points")
	}
	if len(l.PlayerSpawnPoints) == 0 {
		return fmt.Errorf("no player spawn points")
	}
	if len(l.ExtractionPoints) == 0 {
		return fmt.Errorf("no extraction points")
	}
	for i, point := range l.ResourcePoints {
		if point.ResourcePointID <= 0 {
			return fmt.Errorf("resource point %d: invalid resourcePointId %d", i, point.ResourcePointID)
		}
	}
	// 房间归属不变量：每个敌人点必须落在某个房间内（走廊也是房间）。
	for i, point := range l.EnemySpawnPoints {
		if l.RoomIndexAt(point) < 0 {
			return fmt.Errorf("enemy spawn point %d (%g,%g) does not belong to any room", i, point.X, point.Y)
		}
	}
	return nil
}

// Load 读取并校验编辑器导出的地图定义 JSON。
func Load(path string) (*Layout, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("read map definition: %w", err)
	}
	var layout Layout
	if err := json.Unmarshal(data, &layout); err != nil {
		return nil, fmt.Errorf("parse map definition %s: %w", path, err)
	}
	if err := layout.Validate(); err != nil {
		return nil, fmt.Errorf("map definition %s: %w", path, err)
	}
	return &layout, nil
}
