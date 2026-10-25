package mapdef

import (
	"os"
	"path/filepath"
	"testing"
)

// 真实导出产物：编辑器菜单 Utility/Map/Export Map Definition JSON 的输出经 sync_config 同步到 config。
const realMapPath = "../../config/tbmapdefinition.json"

func TestLoadRepositoryExport(t *testing.T) {
	layout, err := Load(realMapPath)
	if err != nil {
		t.Fatalf("Load(%s): %v", realMapPath, err)
	}

	if len(layout.Rooms) != 9 {
		t.Fatalf("rooms = %d, want 9", len(layout.Rooms))
	}
	if len(layout.ResourcePoints) != 7 {
		t.Fatalf("resourcePoints = %d, want 7", len(layout.ResourcePoints))
	}
	if len(layout.EnemySpawnPoints) != 5 {
		t.Fatalf("enemySpawnPoints = %d, want 5", len(layout.EnemySpawnPoints))
	}
	if len(layout.PlayerSpawnPoints) != 3 {
		t.Fatalf("playerSpawnPoints = %d, want 3", len(layout.PlayerSpawnPoints))
	}
	if len(layout.ExtractionPoints) != 2 {
		t.Fatalf("extractionPoints = %d, want 2", len(layout.ExtractionPoints))
	}

	// 房间顺序与客户端资产列表序一致（点位 ID 依赖列表下标，不得重排）
	wantRooms := []Room{
		{Type: 0, MinX: -23, MinY: -5, MaxX: -13, MaxY: 6},
		{Type: 0, MinX: -6, MinY: -5, MaxX: 14, MaxY: 6},
		{Type: 0, MinX: -3, MinY: -19, MaxX: 10, MaxY: -12},
		{Type: 0, MinX: 23, MinY: -5, MaxX: 36, MaxY: 7},
		{Type: 0, MinX: -5, MinY: 11, MaxX: 11, MaxY: 18},
		{Type: 1, MinX: -12, MinY: -1, MaxX: -7, MaxY: 2},
		{Type: 1, MinX: 2, MinY: 7, MaxX: 5, MaxY: 10},
		{Type: 1, MinX: 2, MinY: -11, MaxX: 5, MaxY: -6},
		{Type: 1, MinX: 15, MinY: -1, MaxX: 22, MaxY: 2},
	}
	for i, want := range wantRooms {
		if layout.Rooms[i] != want {
			t.Fatalf("room %d = %+v, want %+v", i, layout.Rooms[i], want)
		}
	}

	// 物资点：前两个是资源点 1，第三个是资源点 2（与资产顺序一致）
	wantResourcePoints := []ResourcePoint{
		{X: -4, Y: 5, ResourcePointID: 1},
		{X: 1, Y: -4, ResourcePointID: 1},
		{X: 5, Y: 5, ResourcePointID: 2},
		{X: -22, Y: 2, ResourcePointID: 3},
		{X: -20, Y: -3, ResourcePointID: 2},
		{X: -16, Y: 5, ResourcePointID: 4},
		{X: -3, Y: 14, ResourcePointID: 4},
	}
	for i, want := range wantResourcePoints {
		if layout.ResourcePoints[i] != want {
			t.Fatalf("resource point %d = %+v, want %+v", i, layout.ResourcePoints[i], want)
		}
	}

	for i, point := range layout.EnemySpawnPoints {
		if layout.RoomIndexAt(point) < 0 {
			t.Fatalf("enemy spawn point %d (%g,%g) is not inside any room", i, point.X, point.Y)
		}
	}
}

func TestRoomIndexAtMatchesEnemyOwnership(t *testing.T) {
	layout, err := Load(realMapPath)
	if err != nil {
		t.Fatalf("Load(%s): %v", realMapPath, err)
	}

	// 真实资产里每个敌人点的归属房间（按导出列表序）
	want := []int{0, 0, 1, 4, 4}
	for i, wantRoom := range want {
		if got := layout.RoomIndexAt(layout.EnemySpawnPoints[i]); got != wantRoom {
			t.Fatalf("enemy spawn point %d belongs to room %d, want %d", i, got, wantRoom)
		}
	}
}

func TestRoomContainsUsesTileInterval(t *testing.T) {
	room := Room{MinX: -23, MinY: -5, MaxX: -13, MaxY: 6}
	cases := []struct {
		name  string
		point Vec2
		want  bool
	}{
		{"inside", Vec2{X: -20, Y: 0}, true},
		{"min corner", Vec2{X: -23, Y: -5}, true},
		{"max tile corner", Vec2{X: -13, Y: 6}, true},
		{"inside max tile", Vec2{X: -12.5, Y: 6.5}, true},
		{"before min x", Vec2{X: -23.1, Y: 0}, false},
		{"after max x tile", Vec2{X: -12, Y: 0}, false},
		{"after max y tile", Vec2{X: -20, Y: 7}, false},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			if got := room.Contains(c.point); got != c.want {
				t.Fatalf("Contains(%+v) = %v, want %v", c.point, got, c.want)
			}
		})
	}
}

func TestLoadRejectsEnemyPointOutsideRooms(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "tbmapdefinition.json")
	// 敌点 (40,40) 不在唯一房间内
	invalid := `{
  "rooms": [{"type": 0, "minX": 0, "minY": 0, "maxX": 5, "maxY": 5}],
  "resourcePoints": [{"x": 1, "y": 1, "resourcePointId": 1}],
  "enemySpawnPoints": [{"x": 40, "y": 40}],
  "playerSpawnPoints": [{"x": 1, "y": 1}],
  "extractionPoints": [{"x": 2, "y": 2}]
}`
	if err := os.WriteFile(path, []byte(invalid), 0o644); err != nil {
		t.Fatalf("write fixture: %v", err)
	}
	if _, err := Load(path); err == nil {
		t.Fatal("want error for enemy spawn point outside every room")
	}
}

func TestLoadRejectsInvalidLayouts(t *testing.T) {
	cases := []struct{ name, json string }{
		{"no rooms", `{"rooms": [],
			"resourcePoints": [{"x":0,"y":0,"resourcePointId":1}],
			"enemySpawnPoints": [{"x":0,"y":0}],
			"playerSpawnPoints": [{"x":0,"y":0}],
			"extractionPoints": [{"x":0,"y":0}]}`},
		{"no resource points", `{"rooms": [{"type":0,"minX":0,"minY":0,"maxX":5,"maxY":5}],
			"resourcePoints": [],
			"enemySpawnPoints": [{"x":0,"y":0}],
			"playerSpawnPoints": [{"x":0,"y":0}],
			"extractionPoints": [{"x":0,"y":0}]}`},
		{"no enemy spawn points", `{"rooms": [{"type":0,"minX":0,"minY":0,"maxX":5,"maxY":5}],
			"resourcePoints": [{"x":0,"y":0,"resourcePointId":1}],
			"enemySpawnPoints": [],
			"playerSpawnPoints": [{"x":0,"y":0}],
			"extractionPoints": [{"x":0,"y":0}]}`},
		{"no player spawn points", `{"rooms": [{"type":0,"minX":0,"minY":0,"maxX":5,"maxY":5}],
			"resourcePoints": [{"x":0,"y":0,"resourcePointId":1}],
			"enemySpawnPoints": [{"x":0,"y":0}],
			"playerSpawnPoints": [],
			"extractionPoints": [{"x":0,"y":0}]}`},
		{"no extraction points", `{"rooms": [{"type":0,"minX":0,"minY":0,"maxX":5,"maxY":5}],
			"resourcePoints": [{"x":0,"y":0,"resourcePointId":1}],
			"enemySpawnPoints": [{"x":0,"y":0}],
			"playerSpawnPoints": [{"x":0,"y":0}],
			"extractionPoints": []}`},
		{"invalid resource point id", `{"rooms": [{"type":0,"minX":0,"minY":0,"maxX":5,"maxY":5}],
			"resourcePoints": [{"x":0,"y":0,"resourcePointId":0}],
			"enemySpawnPoints": [{"x":0,"y":0}],
			"playerSpawnPoints": [{"x":0,"y":0}],
			"extractionPoints": [{"x":0,"y":0}]}`},
		{"inverted room bounds", `{"rooms": [{"type":0,"minX":5,"minY":5,"maxX":0,"maxY":0}],
			"resourcePoints": [{"x":0,"y":0,"resourcePointId":1}],
			"enemySpawnPoints": [{"x":0,"y":0}],
			"playerSpawnPoints": [{"x":0,"y":0}],
			"extractionPoints": [{"x":0,"y":0}]}`},
		{"malformed json", "{"},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			path := filepath.Join(t.TempDir(), "tbmapdefinition.json")
			if err := os.WriteFile(path, []byte(c.json), 0o644); err != nil {
				t.Fatalf("write fixture: %v", err)
			}
			if _, err := Load(path); err == nil {
				t.Fatal("want validation error")
			}
		})
	}
}

func TestLoadMissingFileReturnsError(t *testing.T) {
	if _, err := Load(filepath.Join(t.TempDir(), "nope.json")); err == nil {
		t.Fatal("want error for missing file")
	}
}
