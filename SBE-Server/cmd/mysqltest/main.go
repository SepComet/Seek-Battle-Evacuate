// mysqltest 一键准备一次性 MySQL 集成测试库并运行 mysqlstore 集成用例。
//
// 用法（在 SBE-Server 目录下，或经 tools/mysql_integration_test 脚本）：
//
//	go run ./cmd/mysqltest -user root -pass secret [-host 127.0.0.1] [-port 3306]
//
// 行为：DROP 并重建固定名称的 sbe_integration_test 库 → 依次应用 migrations/*.sql
// → 以 SBE_TEST_MYSQL_DSN 运行 go test ./internal/mysqlstore -v 并透传退出码。
// 库名固定且每次运行清空重建，绝不用于业务数据。
package main

import (
	"database/sql"
	"flag"
	"fmt"
	"os"
	"os/exec"
	"sort"
	"strings"

	mysql "github.com/go-sql-driver/mysql"
)

const testDBName = "sbe_integration_test"

func main() {
	if err := run(); err != nil {
		fmt.Fprintf(os.Stderr, "mysqltest: %v\n", err)
		os.Exit(1)
	}
}

func run() error {
	user := flag.String("user", "root", "MySQL 用户名")
	pass := flag.String("pass", "", "MySQL 密码")
	host := flag.String("host", "127.0.0.1", "MySQL 主机")
	port := flag.Int("port", 3306, "MySQL 端口")
	migrations := flag.String("migrations", "migrations", "迁移 SQL 目录（相对当前目录）")
	flag.Parse()

	addr := fmt.Sprintf("%s:%d", *host, *port)
	params := map[string]string{"charset": "utf8mb4", "parseTime": "true"}

	admin, err := sql.Open("mysql", (&mysql.Config{
		User: *user, Passwd: *pass, Net: "tcp", Addr: addr, Params: params,
	}).FormatDSN())
	if err != nil {
		return err
	}
	defer func() { _ = admin.Close() }()
	if err := admin.Ping(); err != nil {
		return fmt.Errorf("connect %s (检查服务在跑、账号密码正确): %w", addr, err)
	}

	fmt.Printf("== 重建一次性测试库 %s @ %s ==\n", testDBName, addr)
	for _, stmt := range []string{
		"DROP DATABASE IF EXISTS " + testDBName,
		fmt.Sprintf("CREATE DATABASE %s CHARACTER SET utf8mb4", testDBName),
	} {
		if _, err := admin.Exec(stmt); err != nil {
			return fmt.Errorf("%q: %w", stmt, err)
		}
	}

	testCfg := &mysql.Config{
		User: *user, Passwd: *pass, Net: "tcp", Addr: addr,
		DBName: testDBName, Params: params,
	}
	testDB, err := sql.Open("mysql", testCfg.FormatDSN())
	if err != nil {
		return err
	}
	defer func() { _ = testDB.Close() }()

	files, err := migrationFiles(*migrations)
	if err != nil {
		return err
	}
	fmt.Println("== 应用迁移 ==")
	for _, file := range files {
		raw, err := os.ReadFile(file)
		if err != nil {
			return err
		}
		statements := splitStatements(string(raw))
		for i, stmt := range statements {
			if _, err := testDB.Exec(stmt); err != nil {
				return fmt.Errorf("%s statement %d: %w", file, i+1, err)
			}
		}
		fmt.Printf("   %s (%d statements)\n", file, len(statements))
	}

	dsn := testCfg.FormatDSN()
	fmt.Println("== 运行 go test ./internal/mysqlstore -v ==")
	test := exec.Command("go", "test", "./internal/mysqlstore", "-v")
	test.Env = append(os.Environ(), "SBE_TEST_MYSQL_DSN="+dsn)
	test.Stdout = os.Stdout
	test.Stderr = os.Stderr
	if err := test.Run(); err != nil {
		return fmt.Errorf("集成用例失败: %w", err)
	}
	fmt.Println("== 集成用例全部通过 ==")
	return nil
}

func migrationFiles(dir string) ([]string, error) {
	entries, err := os.ReadDir(dir)
	if err != nil {
		return nil, fmt.Errorf("read migrations dir: %w", err)
	}
	var files []string
	for _, entry := range entries {
		if !entry.IsDir() && strings.HasSuffix(entry.Name(), ".sql") {
			files = append(files, dir+"/"+entry.Name())
		}
	}
	sort.Strings(files)
	if len(files) == 0 {
		return nil, fmt.Errorf("no .sql files under %s", dir)
	}
	return files, nil
}

// splitStatements 去除 -- 注释行后按分号切分；约束：迁移文件不得在字符串字面量里写分号。
func splitStatements(sqlText string) []string {
	var lines []string
	for _, line := range strings.Split(sqlText, "\n") {
		if strings.HasPrefix(strings.TrimSpace(line), "--") {
			continue
		}
		lines = append(lines, line)
	}
	var out []string
	for _, part := range strings.Split(strings.Join(lines, "\n"), ";") {
		if stmt := strings.TrimSpace(part); stmt != "" {
			out = append(out, stmt)
		}
	}
	return out
}
