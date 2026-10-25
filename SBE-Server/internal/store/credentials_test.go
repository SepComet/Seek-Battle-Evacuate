package store

import (
	"errors"
	"strings"
	"testing"

	"sbe-server/internal/core"
)

func TestHashPasswordRoundTrip(t *testing.T) {
	hash, err := HashPassword("s3cret-pass")
	if err != nil {
		t.Fatalf("hash: %v", err)
	}
	if !VerifyPassword(hash, "s3cret-pass") {
		t.Fatal("correct password must verify")
	}
	if VerifyPassword(hash, "wrong-pass") {
		t.Fatal("wrong password must not verify")
	}
	// 每次哈希使用随机盐，两次结果不同但均可校验
	other, err := HashPassword("s3cret-pass")
	if err != nil {
		t.Fatalf("hash again: %v", err)
	}
	if other == hash {
		t.Fatal("hashes must be salted (deterministic output)")
	}
	if !VerifyPassword(other, "s3cret-pass") {
		t.Fatal("second hash must verify")
	}
}

func TestVerifyPasswordRejectsMalformedStoredHash(t *testing.T) {
	for _, bad := range []string{"", "plaintext", "md5$1$abc$def", "pbkdf2-sha256$abc$####$####", "pbkdf2-sha256$1$AAAA$####"} {
		if VerifyPassword(bad, "whatever") {
			t.Fatalf("malformed stored hash %q must not verify", bad)
		}
	}
}

func TestValidateUsername(t *testing.T) {
	name, err := ValidateUsername("  玩家_01 ")
	if err != nil || name != "玩家_01" {
		t.Fatalf("valid username rejected: name=%q err=%v", name, err)
	}
	for _, bad := range []string{"", " ", "a", strings.Repeat("字", 33), "bad\x01name"} {
		if _, err := ValidateUsername(bad); !errors.Is(err, core.ErrRejected) {
			t.Fatalf("username %q must be rejected, got %v", bad, err)
		}
	}
}

func TestValidatePassword(t *testing.T) {
	if err := ValidatePassword("123456"); err != nil {
		t.Fatalf("valid password rejected: %v", err)
	}
	if err := ValidatePassword("a b c d e f"); err != nil {
		t.Fatalf("spaces allowed in password: %v", err)
	}
	for _, bad := range []string{"", "12345", strings.Repeat("x", 65), "bad\x7fpw"} {
		if err := ValidatePassword(bad); !errors.Is(err, core.ErrRejected) {
			t.Fatalf("password %q must be rejected, got %v", bad, err)
		}
	}
}
