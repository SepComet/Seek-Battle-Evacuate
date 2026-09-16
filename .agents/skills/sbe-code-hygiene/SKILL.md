---
name: sbe-code-hygiene
description: >-
  Inspect and clean up code in the Seek-Battle-Evacuate (SBE) project according to AGENTS.md
  null-checking rules, error handling specifications, and anti-defensive programming guidelines.
  Use this skill whenever the user asks to format code per AGENTS.md, clean up redundant null checks,
  remove over-defensive fallbacks, or review code hygiene.
---

# SBE Code Hygiene & Defensive Cleanup

本技能用于规范 SBE 项目中的 C# 代码质量，消除过度防御（Over-Defensive Programming）、冗余判空与静默吞错，严格对齐 `AGENTS.md` 规范与敏捷“Fail Fast”（快速失败）原则。

---

## 一、三项核心红线（AGENTS.md 必须遵守）

1. **声明处已初始化的变量，之后绝对不判空**
   - 坏：字段或局部变量已有 `= new List<T>()` / `= new Dictionary<...>()`，后续还写 `if (_list != null)`。
   - 好：直接使用 `_list.Add(...)`、`_list.Clear()`。

2. **严禁静态兜底与静默回退（Fail Fast 原则）**
   - 坏：找不到数据、缺少配置或解析异常时，返回硬编码默认值（如 `global != null ? global.Time : 250` 或 `if (cfg == null) return 10;`）。
   - 好：异常路径直接 `Log.Error(...)` 报错并中断执行（`return` / 抛异常），绝不用伪造数据让程序带病运行。

3. **UI 序列化字段与 View 自动绑定字段不做判空**
   - 坏：`if (View != null && View.sortButton != null)`、`[SerializeField] private Button _btn; ... if (_btn != null)`。
   - 好：默认信任 Inspector 和 `UIAssetsTools` 自动绑定。若没绑定本应报错暴露，不加空防御掩盖。

---

## 二、六项常见“没必要”的过度防御与坏味道

在 Unity / UGF / C# 实际开发中，以下写法通常是冗余、降低代码可读性且增加维护负担的：

### 1. 集合遍历前的非空/长度双重检查
- **没必要**：
  ```csharp
  if (list != null && list.Count > 0)
  {
      foreach (var item in list) { ... }
  }
  ```
- **原则**：若列表规范为非空对象（即使为空也是包含 0 个元素的空列表），`foreach` 会自动直接跳过，根本无需任何 `if` 守卫：
  ```csharp
  foreach (var item in list) { ... }
  ```

### 2. LINQ 与内建构造返回值的非空检查
- **没必要**：
  ```csharp
  var results = items.Where(x => x.Active).ToList();
  if (results != null) { ... } // ToList() 永远返回新 List 实例，永不为 null
  ```
- **原则**：直接使用 LINQ 投影/转换结果。

### 3. 内部私有辅助方法的层层重复防御
- **没必要**：公有入口方法已经做了入参断言/校验，拆分出来的内部私有方法 `private void ApplyCore(Item item)` 每一层都再写一遍 `if (item == null) return;`。
- **原则**：私有方法属于内部信任边界，保持干净直白，将校验集中在业务入口。

### 4. 字典的双重查找（Double Lookup）
- **没必要**：
  ```csharp
  if (_dict.ContainsKey(key))
  {
      var val = _dict[key];
      ...
  }
  ```
- **原则**：统一使用 `TryGetValue`，减少一次哈希哈希计算与冗余判断：
  ```csharp
  if (_dict.TryGetValue(key, out var val))
  {
      ...
  }
  ```

### 5. 静默吞异常的空 Try-Catch
- **没必要**：
  ```csharp
  try { DoSomething(); }
  catch (Exception) { /* 留空，或者只 Log.Warning 假装没事 */ }
  ```
- **原则**：非预期异常必须 `Log.Error(...)` 暴露上下文并附带具体错误信息，绝不能掩盖致命逻辑错误或数据存盘缺失。

### 6. MonoBehaviour/实例方法内的自我判空
- **没必要**：在普通实例方法内部写 `if (this == null)`，或对由 Unity 生命周期保证的对象进行反复探测。
- **原则**：信任正常的组件调用生命周期。

---

## 三、代码排查与清理步骤（Runbook）

当用户发出整理请求时，按以下流程执行：

1. **识别范围**：
   - 检查 `git status` 涉及修改的文件，或针对用户指定的代码文件。
2. **逐项审查**：
   - [ ] 检查是否有 `[SerializeField]`、`View.xxx` 的判空，有则移除。
   - [ ] 检查声明处赋值（如 `= new ...`）字段的二次判空，有则移除。
   - [ ] 检查是否有配置读取、存档读取的三元默认值回退（`?:` 魔法数字），改为 `Log.Error` 并中断。
   - [ ] 检查 `foreach` 外层是否有冗余的 `if (list != null && list.Count > 0)` 嵌套，若列表非空则拍平。
   - [ ] 检查是否有空 `catch` 吞错。
3. **推演逻辑边界（静态检查）**：
   - 确认移除防御后不会导致不可预知的崩溃；
   - 确认异常路径是否确实按规范输出了 `Log.Error`。
4. **汇报改动**：
   - 列出修改的文件路径；
   - 简要说明移除了哪些冗余防御或规范了哪些异常断言。
