package main

import (
	"archive/zip"
	"bytes"
	"encoding/json"
	"encoding/xml"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strconv"
	"strings"
)

type checkReport struct {
	Groups map[string]any `json:"groups"`
	Warnings []string     `json:"warnings,omitempty"`
	Summary  string       `json:"summary"`
}

func toolConsistencyCheck(args map[string]any) (string, error) {
	report := checkReport{Groups: map[string]any{}}
	totalViolations := 0
	totalWarnings := 0

	tableGroup, n, w := checkLubanTables()
	report.Groups["tables"] = tableGroup
	totalViolations += n
	totalWarnings += w

	markerGroup, n2 := checkGeneratedMarkers()
	report.Groups["generated_markers"] = markerGroup
	totalViolations += n2

	asmGroup, n3, w3 := checkAsmdefs()
	report.Groups["asmdefs"] = asmGroup
	totalViolations += n3
	totalWarnings += w3

	report.Summary = fmt.Sprintf("%d 组检查：%d 个违规，%d 个警告", len(report.Groups), totalViolations, totalWarnings)
	data, err := json.MarshalIndent(report, "", "  ")
	if err != nil {
		return "", err
	}
	return string(data), nil
}

// ---------- 组 1：Luban 表名多处一致 ----------

// nonLubanStems DataTables 下刻意存在、但不由 Luban 生成的产物，
// 不参与「产物必须有注册表」的一致性检查（见 contracts §8）。
// tbmapdefinition：地图固定布局，由编辑器菜单 Utility/Map/Export Map Definition JSON
// 把 MapDefinition 资产序列化而来；它随 sync_config 同步到 SBE-Server/config，
// 服务器据此重建固定布局，不是 Luban 表。
var nonLubanStems = map[string]bool{
	"tbmapdefinition": true,
}

type lubanTableRow struct {
	FullName  string
	ValueType string
	Input     string
	Output    string
	Mode      string
}

func checkLubanTables() (map[string]any, int, int) {
	violations := []string{}
	warnings := []string{}

	genDir := filepath.Join(repoRoot, "SBE-Client", "Assets", "GameMain", "Scripts", "Base", "Gen")
	dataDir := filepath.Join(repoRoot, "SBE-Client", "Assets", "GameMain", "DataTables")
	datasDir := filepath.Join(repoRoot, "SBE-Design", "Datas")
	tablesXlsx := filepath.Join(datasDir, "__tables__.xlsx")
	tablesCs := filepath.Join(genDir, "Tables.cs")
	lubanComponentCs := filepath.Join(repoRoot, "SBE-Client", "Assets", "GameMain", "Scripts", "Runtime", "CustomComponent", "Luban", "LubanComponent.cs")

	rows, err := readXlsxSheet(tablesXlsx)
	if err != nil {
		return groupResult(nil, []string{"读取 __tables__.xlsx 失败: " + err.Error() + "（若 Excel 正开着该文件，请先关闭）"}), 1, 0
	}
	tables, err := parseTablesRegistry(rows)
	if err != nil {
		return groupResult(nil, []string{"解析 __tables__.xlsx 注册行失败: " + err.Error()}), 1, 0
	}

	tablesCsData, err := os.ReadFile(tablesCs)
	if err != nil {
		return groupResult(nil, []string{"读取 Tables.cs 失败: " + err.Error()}), 1, 0
	}
	componentData, err := os.ReadFile(lubanComponentCs)
	if err != nil {
		return groupResult(nil, []string{"读取 LubanComponent.cs 失败: " + err.Error()}), 1, 0
	}

	loaderArgs := captureAll(`loader\("([^"]+)"\)`, string(tablesCsData))
	newClasses := captureAll(`new\s+(Tb\w+)\s*\(`, string(tablesCsData))
	tableNamesBlocks := captureAll(`TableNames\s*=\s*new\s+string\[\]\s*\{([^}]*?)\}`, string(componentData))
	tableNames := []string(nil)
	if len(tableNamesBlocks) > 0 {
		tableNames = captureAll(`"([^"]+)"`, tableNamesBlocks[0])
	} else {
		return groupResult(nil, []string{"LubanComponent.cs 中未找到 TableNames 数组定义"}), 1, 0
	}

	dataStems := map[string]bool{}
	if entries, err := os.ReadDir(dataDir); err == nil {
		for _, e := range entries {
			if e.IsDir() || strings.HasSuffix(e.Name(), ".meta") {
				continue
			}
			dataStems[strings.TrimSuffix(e.Name(), filepath.Ext(e.Name()))] = true
		}
	} else {
		violations = append(violations, "读取 DataTables 目录失败: "+err.Error())
	}

	genTbFiles := map[string]bool{}
	if entries, err := os.ReadDir(genDir); err == nil {
		for _, e := range entries {
			name := e.Name()
			if e.IsDir() || !strings.HasPrefix(name, "Tb") || !strings.HasSuffix(name, ".cs") {
				continue
			}
			genTbFiles[strings.TrimSuffix(name, ".cs")] = true
		}
	}

	expectedStems := map[string]bool{}
	registryLowers := map[string]bool{}
	registryNames := map[string]bool{}
	for _, row := range tables {
		if row.FullName == "" {
			continue
		}
		registryNames[row.FullName] = true
		lower := strings.ToLower(row.FullName)
		registryLowers[lower] = true

		if _, ok := genTbFiles[row.FullName]; !ok {
			violations = append(violations, fmt.Sprintf("表 %s：缺少生成类文件 Gen/%s.cs", row.FullName, row.FullName))
		}
		if row.ValueType != "" && row.ValueType[0] >= 'A' && row.ValueType[0] <= 'Z' {
			if _, err := os.Stat(filepath.Join(genDir, row.ValueType+".cs")); err != nil {
				violations = append(violations, fmt.Sprintf("表 %s：缺少 value_type 行类文件 Gen/%s.cs", row.FullName, row.ValueType))
			}
		}
		stem := row.Output
		if stem == "" {
			stem = lower
		}
		stem = strings.TrimSuffix(stem, ".json")
		expectedStems[stem] = true
		for _, ext := range []string{".json", ".bytes"} {
			if !dataStems[stem] {
				violations = append(violations, fmt.Sprintf("表 %s：DataTables 缺少产物 %s%s", row.FullName, stem, ext))
			}
		}
		if !contains(loaderArgs, lower) {
			violations = append(violations, fmt.Sprintf("表 %s：Tables.cs 构造参数缺少 loader(\"%s\")", row.FullName, lower))
		}
		if !contains(tableNames, lower) {
			violations = append(violations, fmt.Sprintf("表 %s：LubanComponent.TableNames 数组缺少 \"%s\"", row.FullName, lower))
		}
		if !contains(newClasses, row.FullName) {
			violations = append(violations, fmt.Sprintf("表 %s：Tables.cs 缺少 new %s( 构造", row.FullName, row.FullName))
		}
		if row.Input != "" {
			for _, part := range strings.Split(row.Input, ",") {
				inputName := strings.TrimSpace(strings.SplitN(part, "@", 2)[0])
				if inputName == "" {
					continue
				}
				if _, err := os.Stat(filepath.Join(datasDir, inputName)); err != nil {
					violations = append(violations, fmt.Sprintf("表 %s：input 工作簿不存在 Datas/%s", row.FullName, inputName))
				}
			}
		}
	}

	for _, arg := range loaderArgs {
		if !registryLowers[arg] {
			violations = append(violations, fmt.Sprintf("Tables.cs 构造参数 loader(\"%s\") 在 __tables__.xlsx 中没有对应注册行", arg))
		}
	}
	for _, name := range tableNames {
		if !registryLowers[name] {
			violations = append(violations, fmt.Sprintf("LubanComponent.TableNames 中的 \"%s\" 在 __tables__.xlsx 中没有对应注册行", name))
		}
	}
	var genTbSorted []string
	for name := range genTbFiles {
		genTbSorted = append(genTbSorted, name)
	}
	sort.Strings(genTbSorted)
	for _, name := range genTbSorted {
		if !registryNames[name] {
			violations = append(violations, fmt.Sprintf("Gen/%s.cs 未在 __tables__.xlsx 注册（stale 生成文件或表名不一致）", name))
		}
	}
	var dataSorted []string
	for stem := range dataStems {
		dataSorted = append(dataSorted, stem)
	}
	sort.Strings(dataSorted)
	for _, stem := range dataSorted {
		if expectedStems[stem] {
			continue
		}
		// 白名单：co-located 在 DataTables 下但不经 Luban 生成的产物（见 nonLubanStems）。
		if nonLubanStems[stem] {
			continue
		}
		violations = append(violations, fmt.Sprintf("DataTables/%s.* 无对应注册表（stale 产物或表名不一致）", stem))
	}

	if len(loaderArgs) > 0 && len(tableNames) > 0 && !sameOrder(loaderArgs, tableNames) {
		warnings = append(warnings, "Tables.cs 构造参数顺序与 LubanComponent.TableNames 数组顺序不一致（注释要求两者保持一致）")
	}

	return groupResult(map[string]any{
		"registry_count": len(tables),
		"loader_args":    len(loaderArgs),
		"table_names":    len(tableNames),
	}, violations), len(violations), len(warnings)
}

// parseTablesRegistry 从 __tables__.xlsx 首个 sheet 的行中提取注册行：
// 表头行是含 full_name 的行，其下每行一条注册（跳过 # 开头的注释行与空行）。
func parseTablesRegistry(rows [][]string) ([]lubanTableRow, error) {
	headerIdx := -1
	col := map[string]int{}
	for i, row := range rows {
		for _, cell := range row {
			if strings.TrimSpace(cell) == "full_name" {
				headerIdx = i
				for k, c := range row {
					col[strings.TrimSpace(c)] = k
				}
				break
			}
		}
		if headerIdx >= 0 {
			break
		}
	}
	if headerIdx < 0 {
		return nil, fmt.Errorf("未找到 full_name 表头行")
	}
	get := func(row []string, key string) string {
		idx, ok := col[key]
		if !ok || idx >= len(row) {
			return ""
		}
		return strings.TrimSpace(row[idx])
	}
	var tables []lubanTableRow
	for _, row := range rows[headerIdx+1:] {
		first := ""
		for _, c := range row {
			if strings.TrimSpace(c) != "" {
				first = strings.TrimSpace(c)
				break
			}
		}
		if first == "" || strings.HasPrefix(first, "#") {
			continue
		}
		tables = append(tables, lubanTableRow{
			FullName:  get(row, "full_name"),
			ValueType: get(row, "value_type"),
			Input:     get(row, "input"),
			Output:    get(row, "output"),
			Mode:      get(row, "mode"),
		})
	}
	return tables, nil
}

// ---------- 组 2：生成文件头标记 ----------

func checkGeneratedMarkers() (map[string]any, int) {
	missing := []string{}
	checked := 0

	genDir := filepath.Join(repoRoot, "SBE-Client", "Assets", "GameMain", "Scripts", "Base", "Gen")
	uiDir := filepath.Join(repoRoot, "SBE-Client", "Assets", "GameMain", "Scripts", "UI")

	entries, err := os.ReadDir(genDir)
	if err == nil {
		for _, e := range entries {
			if e.IsDir() || !strings.HasSuffix(e.Name(), ".cs") {
				continue
			}
			checked++
			if !hasAutoGeneratedMarker(filepath.Join(genDir, e.Name())) {
				missing = append(missing, filepath.ToSlash(filepath.Join(genDir, e.Name()))+" 缺少 <auto-generated> 标记")
			}
		}
	} else {
		missing = append(missing, "读取 Gen 目录失败: "+err.Error())
	}

	_ = filepath.WalkDir(uiDir, func(path string, d os.DirEntry, err error) error {
		if err != nil || d.IsDir() {
			return nil
		}
		name := d.Name()
		if !strings.HasSuffix(name, ".cs") {
			return nil
		}
		if strings.HasSuffix(name, ".Logic.cs") {
			return nil // 手写 partial，不需要标记
		}
		if !strings.HasSuffix(name, "Form.cs") && !strings.HasSuffix(name, "View.cs") {
			return nil
		}
		// UI 根目录散置的 Form/View（如 GameFramework 自带的 DialogForm、手写的 UpdateResourceForm）
		// 不是 UIAssetsTools 产物，不查标记；生成的 Form/View 都在各自子目录内。
		if rel, relErr := filepath.Rel(uiDir, path); relErr == nil && !strings.ContainsRune(rel, filepath.Separator) {
			return nil
		}
		checked++
		if !hasAutoGeneratedMarker(path) {
			missing = append(missing, filepath.ToSlash(path)+" 缺少 <auto-generated> 标记")
		}
		return nil
	})

	return map[string]any{"checked": checked, "violations": missing}, len(missing)
}

func hasAutoGeneratedMarker(path string) bool {
	data, err := os.ReadFile(path)
	if err != nil {
		return false
	}
	if len(data) > 4096 {
		data = data[:4096]
	}
	return bytes.Contains(data, []byte("<auto-generated"))
}

// ---------- 组 3：asmdef 引用可解析 ----------

type asmdefFile struct {
	Name       string   `json:"name"`
	References []string `json:"references"`
}

func checkAsmdefs() (map[string]any, int, int) {
	assetsDir := filepath.Join(repoRoot, "SBE-Client", "Assets")
	type asmdefInfo struct {
		path string
		def  asmdefFile
		guid string
	}
	var infos []asmdefInfo
	guidToName := map[string]string{}
	nameSet := map[string]bool{}

	_ = filepath.WalkDir(assetsDir, func(path string, d os.DirEntry, err error) error {
		if err != nil || d.IsDir() {
			return nil
		}
		if strings.HasSuffix(path, ".asmdef") {
			data, err := os.ReadFile(path)
			if err != nil {
				return nil
			}
			var def asmdefFile
			if err := json.Unmarshal(data, &def); err != nil {
				return nil
			}
			infos = append(infos, asmdefInfo{path: path, def: def})
			nameSet[def.Name] = true
		}
		return nil
	})

	// meta 与 asmdef 一一配对：.meta 的 guid 对应同目录同名 .asmdef。
	for i := range infos {
		metaPath := infos[i].path + ".meta"
		data, err := os.ReadFile(metaPath)
		if err != nil {
			continue
		}
		m := regexp.MustCompile(`guid:\s*([0-9a-fA-F]+)`).FindSubmatch(data)
		if m == nil {
			continue
		}
		guid := strings.ToLower(string(m[1]))
		infos[i].guid = guid
		if _, exists := guidToName[guid]; !exists {
			guidToName[guid] = infos[i].def.Name
		}
	}

	violations := []string{}
	warnings := []string{}
	checked := len(infos)

	// 二级注册表：Unity 包程序集的 asmdef.meta 在 Library/PackageCache 下（不存在则跳过）。
	packageGuids := map[string]bool{}
	packageCacheDir := filepath.Join(repoRoot, "SBE-Client", "Library", "PackageCache")
	_ = filepath.WalkDir(packageCacheDir, func(path string, d os.DirEntry, err error) error {
		if err != nil || d.IsDir() || !strings.HasSuffix(path, ".asmdef.meta") {
			return nil
		}
		data, err := os.ReadFile(path)
		if err != nil {
			return nil
		}
		m := regexp.MustCompile(`guid:\s*([0-9a-fA-F]+)`).FindSubmatch(data)
		if m != nil {
			packageGuids[strings.ToLower(string(m[1]))] = true
		}
		return nil
	})

	// 重复 GUID 检查
	guidOwners := map[string][]string{}
	for _, info := range infos {
		if info.guid == "" {
			violations = append(violations, filepath.ToSlash(info.path)+" 的 .meta 缺少 guid")
			continue
		}
		guidOwners[info.guid] = append(guidOwners[info.guid], info.def.Name)
	}
	var ownerGuids []string
	for g := range guidOwners {
		ownerGuids = append(ownerGuids, g)
	}
	sort.Strings(ownerGuids)
	for _, g := range ownerGuids {
		if owners := guidOwners[g]; len(owners) > 1 {
			violations = append(violations, fmt.Sprintf("GUID %s 被多个 asmdef 使用: %s", g, strings.Join(owners, ", ")))
		}
	}

	for _, info := range infos {
		for _, ref := range info.def.References {
			if len(ref) > 5 && strings.EqualFold(ref[:5], "GUID:") {
				guid := strings.ToLower(ref[5:])
				if _, ok := guidToName[guid]; ok {
					continue
				}
				if packageGuids[guid] {
					continue // Unity 包程序集（Library/PackageCache）
				}
				warnings = append(warnings, fmt.Sprintf("%s 引用的 %s 在 Assets 与 Library/PackageCache 中均未找到（可能是未安装的包或内置模块，若编译报缺失再处理）",
					filepath.ToSlash(info.path), ref))
			} else if ref != "" && !nameSet[ref] {
				warnings = append(warnings, fmt.Sprintf("%s 的名称引用 \"%s\" 在 Assets 下没有对应 asmdef（可能是包/预编译程序集）",
					filepath.ToSlash(info.path), ref))
			}
		}
	}

	return map[string]any{"checked": checked, "violations": violations, "warnings": warnings}, len(violations), len(warnings)
}

// ---------- 小工具 ----------

func groupResult(extra map[string]any, violations []string) map[string]any {
	result := map[string]any{"violations": violations}
	for k, v := range extra {
		result[k] = v
	}
	return result
}

func captureAll(pattern, s string) []string {
	re := regexp.MustCompile(pattern)
	matches := re.FindAllStringSubmatch(s, -1)
	out := make([]string, 0, len(matches))
	for _, m := range matches {
		if len(m) > 1 {
			out = append(out, strings.TrimSpace(m[1]))
		}
	}
	return out
}

func contains(list []string, target string) bool {
	for _, v := range list {
		if v == target {
			return true
		}
	}
	return false
}

func sameOrder(a, b []string) bool {
	if len(a) != len(b) {
		return false
	}
	for i := range a {
		if a[i] != b[i] {
			return false
		}
	}
	return true
}

// ---------- 最小 xlsx 读取（首个 sheet，全列） ----------

// readXlsxSheet 读取 xlsx 首个 sheet，返回按行、按列对齐的文本。
// 只覆盖本项目所需：shared string / str / inlineStr / 数字原样。
func readXlsxSheet(path string) ([][]string, error) {
	zr, err := zip.OpenReader(path)
	if err != nil {
		return nil, err
	}
	defer func() { _ = zr.Close() }()

	find := func(name string) *zip.File {
		for _, f := range zr.File {
			if f.Name == name {
				return f
			}
		}
		return nil
	}

	shared := map[int]string{}
	if f := find("xl/sharedStrings.xml"); f != nil {
		data, err := readZipFile(f)
		if err != nil {
			return nil, fmt.Errorf("读取 sharedStrings: %w", err)
		}
		shared, err = parseSharedStrings(data)
		if err != nil {
			return nil, fmt.Errorf("解析 sharedStrings: %w", err)
		}
	}

	sheetPath := "xl/worksheets/sheet1.xml"
	if f := find("xl/workbook.xml"); f != nil {
		data, err := readZipFile(f)
		if err == nil {
			if rid := firstSheetRid(data); rid != "" {
				if relFile := find("xl/_rels/workbook.xml.rels"); relFile != nil {
					relData, err := readZipFile(relFile)
					if err == nil {
						if target := relTarget(relData, rid); target != "" {
							if strings.HasPrefix(target, "/") {
								sheetPath = target[1:]
							} else {
								sheetPath = "xl/" + target
							}
						}
					}
				}
			}
		}
	}

	sheetFile := find(sheetPath)
	if sheetFile == nil {
		return nil, fmt.Errorf("xlsx 中未找到 %s", sheetPath)
	}
	data, err := readZipFile(sheetFile)
	if err != nil {
		return nil, err
	}
	return parseSheetRows(data, shared)
}

func readZipFile(f *zip.File) ([]byte, error) {
	rc, err := f.Open()
	if err != nil {
		return nil, err
	}
	defer func() { _ = rc.Close() }()
	return io.ReadAll(rc)
}

func parseSharedStrings(data []byte) (map[int]string, error) {
	dec := xml.NewDecoder(bytes.NewReader(data))
	out := map[int]string{}
	idx := 0
	inT := false
	var cur strings.Builder
	for {
		tok, err := dec.Token()
		if err == io.EOF {
			break
		}
		if err != nil {
			return nil, err
		}
		switch t := tok.(type) {
		case xml.StartElement:
			switch t.Name.Local {
			case "si":
				cur.Reset()
			case "t":
				inT = true
			}
		case xml.EndElement:
			switch t.Name.Local {
			case "t":
				inT = false
			case "si":
				out[idx] = cur.String()
				idx++
			}
		case xml.CharData:
			if inT {
				cur.Write(t)
			}
		}
	}
	return out, nil
}

// firstSheetRid 返回 workbook.xml 中第一个 sheet 的 r:id。
func firstSheetRid(data []byte) string {
	dec := xml.NewDecoder(bytes.NewReader(data))
	for {
		tok, err := dec.Token()
		if err != nil {
			return ""
		}
		if se, ok := tok.(xml.StartElement); ok && se.Name.Local == "sheet" {
			for _, attr := range se.Attr {
				if attr.Name.Local == "id" && strings.Contains(attr.Name.Space, "relationships") {
					return attr.Value
				}
			}
		}
	}
}

func relTarget(data []byte, rid string) string {
	dec := xml.NewDecoder(bytes.NewReader(data))
	for {
		tok, err := dec.Token()
		if err != nil {
			return ""
		}
		if se, ok := tok.(xml.StartElement); ok && se.Name.Local == "Relationship" {
			var id, target string
			for _, attr := range se.Attr {
				switch attr.Name.Local {
				case "Id":
					id = attr.Value
				case "Target":
					target = attr.Value
				}
			}
			if id == rid {
				return target
			}
		}
	}
}

func parseSheetRows(data []byte, shared map[int]string) ([][]string, error) {
	dec := xml.NewDecoder(bytes.NewReader(data))
	var rows [][]string
	curRow := map[int]string{}
	curCol := -1
	curT := ""
	inV := false
	inInline := false
	var buf strings.Builder

	flushRow := func() {
		if len(curRow) == 0 {
			return
		}
		maxCol := -1
		for c := range curRow {
			if c > maxCol {
				maxCol = c
			}
		}
		row := make([]string, maxCol+1)
		for c, v := range curRow {
			row[c] = v
		}
		rows = append(rows, row)
	}

	for {
		tok, err := dec.Token()
		if err == io.EOF {
			break
		}
		if err != nil {
			return nil, err
		}
		switch t := tok.(type) {
		case xml.StartElement:
			switch t.Name.Local {
			case "row":
				curRow = map[int]string{}
			case "c":
				curCol = -1
				curT = ""
				buf.Reset()
				for _, attr := range t.Attr {
					switch attr.Name.Local {
					case "r":
						curCol = colIndexOf(attr.Value)
					case "t":
						curT = attr.Value
					}
				}
			case "v":
				inV = true
				buf.Reset()
			case "is":
				inInline = true
				buf.Reset()
			}
		case xml.CharData:
			if inV || inInline {
				buf.Write(t)
			}
		case xml.EndElement:
			switch t.Name.Local {
			case "v":
				inV = false
				if curCol >= 0 {
					curRow[curCol] = cellText(curT, buf.String(), shared)
				}
			case "is":
				inInline = false
				if curCol >= 0 {
					curRow[curCol] = buf.String()
				}
			case "row":
				flushRow()
			}
		}
	}
	return rows, nil
}

func cellText(t, raw string, shared map[int]string) string {
	switch t {
	case "s":
		idx, err := strconv.Atoi(strings.TrimSpace(raw))
		if err != nil {
			return raw
		}
		if v, ok := shared[idx]; ok {
			return v
		}
		return raw
	default:
		return raw
	}
}

// colIndexOf 把 "B3" 这类单元格引用转成 0 基列号。
func colIndexOf(ref string) int {
	n := 0
	for _, r := range ref {
		switch {
		case r >= 'A' && r <= 'Z':
			n = n*26 + int(r-'A') + 1
		case r >= 'a' && r <= 'z':
			n = n*26 + int(r-'a') + 1
		default:
			return n - 1
		}
	}
	return n - 1
}
