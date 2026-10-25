package globaltable

import (
	"os"
	"path/filepath"
	"testing"
)

func TestLoadRepositoryExports(t *testing.T) {
	limits, err := Load("../../config/tbglobalconfig.json", "../../config/tbdifficultyconfig.json")
	if err != nil {
		t.Fatal(err)
	}
	if limits.MaxPlayerPartySize != 4 || limits.BackpackSlotCount != 30 || !limits.Difficulties[1] || !limits.Difficulties[3] || limits.Difficulties[0] {
		t.Fatalf("unexpected exported limits: %+v", limits)
	}
}

func TestLoadRejectsInvalidExports(t *testing.T) {
	cases := []struct{ name, global, difficulties string }{
		{"missing global row", `[]`, `[{"tier":1}]`},
		{"multiple global rows", `[{"maxPlayerPartySize":4,"backpackSlotCount":30},{"maxPlayerPartySize":4,"backpackSlotCount":30}]`, `[{"tier":1}]`},
		{"invalid party limit", `[{"maxPlayerPartySize":0,"backpackSlotCount":30}]`, `[{"tier":1}]`},
		{"invalid backpack limit", `[{"maxPlayerPartySize":4,"backpackSlotCount":0}]`, `[{"tier":1}]`},
		{"empty difficulties", `[{"maxPlayerPartySize":4,"backpackSlotCount":30}]`, `[]`},
		{"duplicate tier", `[{"maxPlayerPartySize":4,"backpackSlotCount":30}]`, `[{"tier":1},{"tier":1}]`},
		{"zero tier", `[{"maxPlayerPartySize":4,"backpackSlotCount":30}]`, `[{"tier":0}]`},
		{"malformed global", `{`, `[{"tier":1}]`},
		{"malformed difficulty", `[{"maxPlayerPartySize":4,"backpackSlotCount":30}]`, `{`},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			dir := t.TempDir()
			g, d := filepath.Join(dir, "global.json"), filepath.Join(dir, "difficulty.json")
			if err := os.WriteFile(g, []byte(c.global), 0600); err != nil {
				t.Fatal(err)
			}
			if err := os.WriteFile(d, []byte(c.difficulties), 0600); err != nil {
				t.Fatal(err)
			}
			if _, err := Load(g, d); err == nil {
				t.Fatal("expected invalid configuration error")
			}
		})
	}
}
