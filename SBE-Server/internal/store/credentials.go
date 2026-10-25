// 凭据工具：用户名/密码校验与口令哈希（pbkdf2-hmac-sha256）。
package store

import (
	"crypto/pbkdf2"
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/base64"
	"fmt"
	"strconv"
	"strings"
	"unicode/utf8"

	"sbe-server/internal/core"
)

// pbkdf2 参数。210k 迭代在服务端约 100ms/次（登录路径独占，可接受），
// 原型期安全/体验平衡；上线若需提高强度，改此常量即可（旧哈希带 iter 字段仍可校验）。
const (
	pbkdf2Iterations = 210000
	pbkdf2KeyLen     = 32
	pbkdf2SaltLen    = 16
	hashAlgName      = "pbkdf2-sha256"
)

// ValidateUsername 校验并规范化用户名：trim 后 2~32 个字符、不含控制字符、区分大小写。
func ValidateUsername(raw string) (string, error) {
	name := strings.TrimSpace(raw)
	n := utf8.RuneCountInString(name)
	if n < 2 || n > 32 {
		return "", fmt.Errorf("%w: username length must be 2-32 characters", core.ErrRejected)
	}
	for _, r := range name {
		if r < 0x20 || r == 0x7f {
			return "", fmt.Errorf("%w: username contains control characters", core.ErrRejected)
		}
	}
	return name, nil
}

// ValidatePassword 校验密码：6~64 个字符（不 trim，含控制字符即拒绝）。
func ValidatePassword(raw string) error {
	n := utf8.RuneCountInString(raw)
	if n < 6 || n > 64 {
		return fmt.Errorf("%w: password length must be 6-64 characters", core.ErrRejected)
	}
	for _, r := range raw {
		if r < 0x20 || r == 0x7f {
			return fmt.Errorf("%w: password contains control characters", core.ErrRejected)
		}
	}
	return nil
}

// HashPassword 生成可存储的口令哈希，格式 pbkdf2-sha256$iter$saltB64$keyB64。
func HashPassword(password string) (string, error) {
	salt := make([]byte, pbkdf2SaltLen)
	if _, err := rand.Read(salt); err != nil {
		return "", err
	}
	key, err := pbkdf2.Key(sha256.New, password, salt, pbkdf2Iterations, pbkdf2KeyLen)
	if err != nil {
		return "", err
	}
	return fmt.Sprintf("%s$%d$%s$%s",
		hashAlgName,
		pbkdf2Iterations,
		base64.RawStdEncoding.EncodeToString(salt),
		base64.RawStdEncoding.EncodeToString(key)), nil
}

// VerifyPassword 以常量时间比较校验口令；存储格式非法一律视为不匹配。
func VerifyPassword(stored, password string) bool {
	parts := strings.Split(stored, "$")
	if len(parts) != 4 || parts[0] != hashAlgName {
		return false
	}
	iter, err := strconv.Atoi(parts[1])
	if err != nil || iter <= 0 {
		return false
	}
	salt, err := base64.RawStdEncoding.DecodeString(parts[2])
	if err != nil {
		return false
	}
	want, err := base64.RawStdEncoding.DecodeString(parts[3])
	if err != nil || len(want) == 0 {
		return false
	}
	got, err := pbkdf2.Key(sha256.New, password, salt, iter, len(want))
	if err != nil {
		return false
	}
	return subtle.ConstantTimeCompare(got, want) == 1
}
