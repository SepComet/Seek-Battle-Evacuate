package protocol

import (
	"encoding/json"
	"reflect"
	"testing"
)

// 本文件锁死协议 v4 的线上字段名与形状（契约 §7「线上字段」）：
// 客户端按 JSON 名解析，改名即断代，因此这里逐字断言 wire format，
// 而不是只断言结构体字段。

// TestVersionIsV4 协议版本（契约 §7：protocol.Version = 4，与客户端 NetMsg.Version 一致）。
func TestVersionIsV4(t *testing.T) {
	if Version != 4 {
		t.Fatalf("Version = %d, want 4", Version)
	}
}

// TestRunMessageTypes 三条新消息的类型字符串是线上契约。
func TestRunMessageTypes(t *testing.T) {
	cases := map[string]string{
		MsgStartRun:  "start_run",
		MsgEncounter: "encounter",
		MsgLoot:      "loot",
	}
	for got, want := range cases {
		if got != want {
			t.Fatalf("message type = %q, want %q", got, want)
		}
	}
}

// TestStartRunRequestWireFormat start_run 请求（契约 §7）。
func TestStartRunRequestWireFormat(t *testing.T) {
	raw, err := json.Marshal(Envelope{T: MsgStartRun, V: Version, RID: 7, Difficulty: 2, Resume: true})
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}
	want := `{"t":"start_run","v":4,"rid":7,"difficulty":2,"resume":true}`
	if string(raw) != want {
		t.Fatalf("start_run request =\n%s\nwant\n%s", raw, want)
	}
}

// TestStartRunResponseWireFormat start_run 响应（契约 §7），含 runDifficulty/consumed/looted。
func TestStartRunResponseWireFormat(t *testing.T) {
	raw, err := json.Marshal(Envelope{
		T: MsgOK, V: Version, RID: 8,
		RunID:           "abc123",
		Seed:            123456789,
		RunDifficulty:   2,
		SpawnPoint:      &Vec2{X: 1, Y: -16},
		ExtractionPoint: &Vec2{X: 30, Y: -3},
		Consumed:        []string{"r2"},
		Looted:          []LootRecord{{PointID: "r2", ItemIDs: []int{5001, 5003}}},
	})
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}
	want := `{"t":"ok","v":4,"rid":8,"runId":"abc123","seed":123456789,` +
		`"spawnPoint":{"x":1,"y":-16},"extractionPoint":{"x":30,"y":-3},"runDifficulty":2,` +
		`"consumed":["r2"],"looted":[{"pointId":"r2","itemIds":[5001,5003]}]}`
	if string(raw) != want {
		t.Fatalf("start_run response =\n%s\nwant\n%s", raw, want)
	}
}

// TestEncounterWireFormat encounter 请求与响应（契约 §7）。
func TestEncounterWireFormat(t *testing.T) {
	raw, err := json.Marshal(Envelope{T: MsgEncounter, V: Version, RID: 9, RunID: "abc123", PointID: "e3"})
	if err != nil {
		t.Fatalf("marshal request: %v", err)
	}
	wantRequest := `{"t":"encounter","v":4,"rid":9,"runId":"abc123","pointId":"e3"}`
	if string(raw) != wantRequest {
		t.Fatalf("encounter request =\n%s\nwant\n%s", raw, wantRequest)
	}

	raw, err = json.Marshal(Envelope{T: MsgOK, V: Version, RID: 9, EnemyPartyID: 4101, Threat: 2})
	if err != nil {
		t.Fatalf("marshal response: %v", err)
	}
	wantResponse := `{"t":"ok","v":4,"rid":9,"enemyPartyId":4101,"threat":2}`
	if string(raw) != wantResponse {
		t.Fatalf("encounter response =\n%s\nwant\n%s", raw, wantResponse)
	}
}

// TestLootWireFormat loot 请求与响应（契约 §7）。
func TestLootWireFormat(t *testing.T) {
	raw, err := json.Marshal(Envelope{T: MsgLoot, V: Version, RID: 10, RunID: "abc123", PointID: "r2"})
	if err != nil {
		t.Fatalf("marshal request: %v", err)
	}
	wantRequest := `{"t":"loot","v":4,"rid":10,"runId":"abc123","pointId":"r2"}`
	if string(raw) != wantRequest {
		t.Fatalf("loot request =\n%s\nwant\n%s", raw, wantRequest)
	}

	raw, err = json.Marshal(Envelope{T: MsgOK, V: Version, RID: 10, ItemIDs: []int{5001, 5003}})
	if err != nil {
		t.Fatalf("marshal response: %v", err)
	}
	wantResponse := `{"t":"ok","v":4,"rid":10,"itemIds":[5001,5003]}`
	if string(raw) != wantResponse {
		t.Fatalf("loot response =\n%s\nwant\n%s", raw, wantResponse)
	}
}

// TestSettleBattleLootWireFormat settle 新增的可选字段 battleLoot（契约 §7）。
func TestSettleBattleLootWireFormat(t *testing.T) {
	raw, err := json.Marshal(Envelope{
		T: MsgSettle, V: Version, RID: 11, RunID: "abc123",
		Outcome: 1, Difficulty: 2, Seed: 123456789, StartedAt: 1000, EndedAt: 2000,
		BattleLoot: []Stack{{ItemID: 5001, Count: 2}},
	})
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}
	want := `{"t":"settle","v":4,"rid":11,"runId":"abc123","outcome":1,"difficulty":2,"seed":123456789,` +
		`"startedAt":1000,"endedAt":2000,"battleLoot":[{"itemId":5001,"count":2}]}`
	if string(raw) != want {
		t.Fatalf("settle request =\n%s\nwant\n%s", raw, want)
	}
}

// TestRunFieldsRoundTrip 新字段反序列化后原样回读（客户端→服务器方向的请求体）。
func TestRunFieldsRoundTrip(t *testing.T) {
	body := `{"t":"start_run","v":4,"rid":12,"difficulty":3,"resume":true,"runId":"r-1","pointId":"e2"}`
	var env Envelope
	if err := json.Unmarshal([]byte(body), &env); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}
	if env.T != MsgStartRun || env.V != 4 || env.RID != 12 || env.Difficulty != 3 || !env.Resume ||
		env.RunID != "r-1" || env.PointID != "e2" {
		t.Fatalf("round trip mismatch: %+v", env)
	}

	settle := `{"t":"settle","v":4,"rid":13,"runId":"r-1","battleLoot":[{"itemId":5301,"count":3},{"itemId":5001,"count":1}]}`
	if err := json.Unmarshal([]byte(settle), &env); err != nil {
		t.Fatalf("unmarshal settle: %v", err)
	}
	want := []Stack{{ItemID: 5301, Count: 3}, {ItemID: 5001, Count: 1}}
	if !reflect.DeepEqual(env.BattleLoot, want) {
		t.Fatalf("battleLoot round trip = %+v, want %+v", env.BattleLoot, want)
	}
}

// TestLegacyFieldsKeepTheirNames 既有字段名与语义不可随 v4 改名（契约 §7「既有消息不变」）。
func TestLegacyFieldsKeepTheirNames(t *testing.T) {
	raw, err := json.Marshal(Envelope{
		T: MsgSettle, V: Version, RID: 14, RunID: "r-1", Outcome: 2,
		Difficulty: 1, Seed: 42, StartedAt: 1, EndedAt: 2,
		Ops: []Op{{K: OpAdd, TID: 1, ItemID: 5001, Count: 1}},
	})
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}
	var fields map[string]json.RawMessage
	if err := json.Unmarshal(raw, &fields); err != nil {
		t.Fatalf("unmarshal fields: %v", err)
	}
	for _, name := range []string{"t", "v", "rid", "runId", "outcome", "difficulty", "seed", "startedAt", "endedAt", "ops"} {
		if _, ok := fields[name]; !ok {
			t.Fatalf("settle lost field %q: %s", name, raw)
		}
	}
	// seed 语义不变：仍是本局主种子（同一个 int64 字段）。
	if string(fields["seed"]) != "42" {
		t.Fatalf("seed = %s, want 42", fields["seed"])
	}
}
