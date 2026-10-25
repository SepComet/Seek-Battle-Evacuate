package itemtable

import (
	"os"
	"path/filepath"
	"testing"
)

const fixtureJSON = `[
  {"id": 5001, "stackLimit": 99, "equipSlot": 0},
  {"id": 5101, "stackLimit": 1, "equipSlot": 1},
  {"id": 5201, "stackLimit": 1, "equipSlot": 2}
]`

func writeFixture(t *testing.T) string {
	t.Helper()
	dir := t.TempDir()
	path := filepath.Join(dir, "tbitemconfig.json")
	if err := os.WriteFile(path, []byte(fixtureJSON), 0o644); err != nil {
		t.Fatalf("write fixture: %v", err)
	}
	return path
}

func TestLoadReadsLubanExport(t *testing.T) {
	table, err := Load(writeFixture(t))
	if err != nil {
		t.Fatalf("Load returned error: %v", err)
	}
	def, ok := table.Get(5101)
	if !ok || def.StackLimit != 1 || def.EquipSlot != 1 {
		t.Fatalf("item 5101 wrong: %+v ok=%v", def, ok)
	}
	if _, ok := table.Get(404); ok {
		t.Fatal("unknown item should miss")
	}
}

func TestLoadRejectsDuplicateID(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "tbitemconfig.json")
	dup := `[{"id":5001,"stackLimit":1},{"id":5001,"stackLimit":2}]`
	if err := os.WriteFile(path, []byte(dup), 0o644); err != nil {
		t.Fatalf("write fixture: %v", err)
	}
	if _, err := Load(path); err == nil {
		t.Fatal("want error for duplicate id")
	}
}

func TestLoadMissingFileReturnsError(t *testing.T) {
	if _, err := Load(filepath.Join(t.TempDir(), "nope.json")); err == nil {
		t.Fatal("want error for missing file")
	}
}
