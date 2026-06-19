# MES 설정 기반(Config Foundation) Implementation Plan — Plan 1 / 3

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `appsettings.json`을 분류별(DRAM Module / DRAM Comp) 구조로 재편하고, DPAPI 암호화 비밀번호와 단일 설정 파일 로드/저장/마이그레이션 기반을 만들어, CLI 자동화가 새 설정으로 동작하게 한다.

**Architecture:** 설정 모델(`Models.cs`)을 플랫에서 분류별로 재구성하고, 순수 함수 `SecretProtector`(DPAPI)와 `ConfigStore`(JSON 로드/저장/레거시 마이그레이션)를 신설한다. `UnimesApp`은 파트 분류로 분류별 설정을 선택해 사용한다. UI는 Plan 3에서 추가. 이 플랜만으로도 CLI 실행이 동작한다.

**Tech Stack:** .NET 8 (`net8.0-windows`), C#, System.Text.Json, `System.Security.Cryptography.ProtectedData`(DPAPI), xUnit.

**범위:** 이 플랜은 설정 모델·영속화·로그인/워크플로우 재배선까지다. 엑셀 리포트는 Plan 2, GUI/테마는 Plan 3.

**스펙 참조:** [docs/superpowers/specs/2026-06-19-mes-main-settings-design.md](../specs/2026-06-19-mes-main-settings-design.md) §4, §5, §9.

---

## File Structure

생성:
- `src/UnimesAutomation/SecretProtector.cs` — DPAPI 암호화/복호화 순수 래퍼.
- `src/UnimesAutomation/ConfigStore.cs` — `appsettings.json` 로드/저장/레거시 마이그레이션.
- `tests/UnimesAutomation.Tests/SecretProtectorTests.cs`
- `tests/UnimesAutomation.Tests/ConfigStoreTests.cs`

수정:
- `src/UnimesAutomation/Models.cs` — 설정 모델 분류별 재구성, `dpapi` 비밀번호 필드.
- `src/UnimesAutomation/BinIdResolver.cs` — `Resolve` 시그니처를 분류별 공정키로 변경.
- `src/UnimesAutomation/UnimesApp.cs` — 분류별 설정 선택, BIN 값 분류별 적용(첫 행), `dpapi` 로그인.
- `src/UnimesAutomation/Program.cs` — `ConfigStore.Load` 사용.
- `src/UnimesAutomation/UnimesAutomation.csproj` — ProtectedData 패키지.
- `tests/UnimesAutomation.Tests/BinIdResolverTests.cs` — 새 시그니처에 맞춤.
- `appsettings.example.json` — 새 형태로 교체.

---

## Task 1: ProtectedData 패키지 추가

**Files:**
- Modify: `src/UnimesAutomation/UnimesAutomation.csproj`

- [ ] **Step 1: csproj에 PackageReference 추가**

`</PropertyGroup>` 다음에 ItemGroup을 추가한다:

```xml
  <ItemGroup>
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
  </ItemGroup>
```

- [ ] **Step 2: 복원/빌드 확인**

Run: `dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj`
Expected: 빌드 성공(경고만 가능), 패키지 복원됨.

- [ ] **Step 3: Commit**

```bash
git add src/UnimesAutomation/UnimesAutomation.csproj
git commit -m "build: add ProtectedData package for DPAPI"
```

---

## Task 2: SecretProtector (DPAPI 래퍼) — TDD

**Files:**
- Create: `src/UnimesAutomation/SecretProtector.cs`
- Test: `tests/UnimesAutomation.Tests/SecretProtectorTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`tests/UnimesAutomation.Tests/SecretProtectorTests.cs`:

```csharp
using UnimesAutomation;
using Xunit;

public class SecretProtectorTests
{
    [Fact]
    public void Encrypt_then_Decrypt_roundtrips()
    {
        var plain = "@Fhfflzlem306";
        var enc = SecretProtector.Encrypt(plain);

        Assert.False(string.IsNullOrEmpty(enc));
        Assert.NotEqual(plain, enc);              // 평문이 그대로 노출되지 않음
        Assert.Equal(plain, SecretProtector.Decrypt(enc));
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Equal("", SecretProtector.Encrypt(""));
        Assert.Equal("", SecretProtector.Decrypt(""));
    }

    [Fact]
    public void Decrypt_invalid_returns_empty()
    {
        Assert.Equal("", SecretProtector.Decrypt("not-a-valid-base64-or-blob!!"));
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj --filter SecretProtectorTests`
Expected: 컴파일 실패(`SecretProtector` 미정의).

- [ ] **Step 3: 구현 작성**

`src/UnimesAutomation/SecretProtector.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace UnimesAutomation;

// Windows DPAPI(CurrentUser)로 비밀번호를 암호화/복호화한다. 평문은 어떤 파일에도 저장하지 않는다.
public static class SecretProtector
{
    public static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return "";
        }

        var bytes = Encoding.UTF8.GetBytes(plain);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Decrypt(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
        {
            return "";
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(encrypted);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            // 다른 계정/PC에서 저장된 값이거나 손상된 값이면 복호화 불가 → 빈 값.
            return "";
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj --filter SecretProtectorTests`
Expected: 3개 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/UnimesAutomation/SecretProtector.cs tests/UnimesAutomation.Tests/SecretProtectorTests.cs
git commit -m "feat: add DPAPI SecretProtector for password encryption"
```

---

## Task 3: 설정 모델 분류별 재구성 + 소비처 재배선

이 태스크는 `Models.cs`를 바꾸면 `UnimesApp`/`BinIdResolver`/`Program`/기존 테스트가 동시에 깨지므로 **하나의 빌드 그린 단위**로 처리한다. 검증은 `dotnet build` + `dotnet test`.

**Files:**
- Modify: `src/UnimesAutomation/Models.cs`
- Modify: `src/UnimesAutomation/BinIdResolver.cs`
- Modify: `src/UnimesAutomation/UnimesApp.cs`
- Modify: `tests/UnimesAutomation.Tests/BinIdResolverTests.cs`

- [ ] **Step 1: `Models.cs`에서 기존 `ItemInfoConfig`/`BinInfoConfig`와 `RootConfig`의 해당 프로퍼티를 새 구조로 교체**

`RootConfig`의 `ItemInfo`/`BinInfo` 프로퍼티와 `CreateDefault()`의 해당 초기화, 그리고 `ItemInfoConfig`/`BinInfoConfig` 클래스를 아래로 교체한다. `LoginConfig`에는 `dpapi` 필드를 추가한다. 다른 클래스(`AppConfig`, `SafetyConfig`, `WorkflowConfig`, `WorkScope`, `PartRequest`, `PartResult`, `RuntimePaths`, `CommandLineOptions`)는 유지.

`RootConfig`의 프로퍼티 영역(기존 `ItemInfo`/`BinInfo` 두 줄)을 다음으로 교체:

```csharp
    [JsonPropertyName("options")]
    public OptionsConfig Options { get; set; } = new();

    [JsonPropertyName("categories")]
    public CategoriesConfig Categories { get; set; } = new();

    [JsonPropertyName("global")]
    public GlobalConfig Global { get; set; } = new();

    public CategoryConfig? ResolveCategory(PartClass cls) => cls switch
    {
        PartClass.Module => Categories.DramModule,
        PartClass.Comp => Categories.DramComp,
        _ => null
    };
```

`CreateDefault()` 안의 `ItemInfo = new ItemInfoConfig(), BinInfo = new BinInfoConfig()` 줄을 다음으로 교체:

```csharp
            Options = new OptionsConfig(),
            Categories = new CategoriesConfig(),
            Global = new GlobalConfig()
```

`LoginConfig`에 비밀번호 암호화 필드를 추가(기존 프로퍼티 사이에):

```csharp
    [JsonPropertyName("passwordEncrypted")]
    public string PasswordEncrypted { get; set; } = "";

    [JsonIgnore]
    public bool UseDpapiPassword =>
        string.Equals(PasswordMode, "dpapi", StringComparison.OrdinalIgnoreCase);
```

기존 `ItemInfoConfig`와 `BinInfoConfig` 클래스 **전체**를 삭제하고 다음 클래스들로 교체:

```csharp
public sealed class OptionsConfig
{
    [JsonPropertyName("defectWarehouses")]
    public List<string> DefectWarehouses { get; set; } = ["제품 폐기창고", "COMPONENT 폐기창고"];

    [JsonPropertyName("binTypes")]
    public List<string> BinTypes { get; set; } = ["Normal-1"];

    [JsonPropertyName("retestThs")]
    public List<string> RetestThs { get; set; } = ["H", "L"];

    [JsonPropertyName("binCompletes")]
    public List<string> BinCompletes { get; set; } = ["Y", "N"];
}

public sealed class CategoriesConfig
{
    [JsonPropertyName("dramModule")]
    public CategoryConfig DramModule { get; set; } = CategoryConfig.DefaultModule();

    [JsonPropertyName("dramComp")]
    public CategoryConfig DramComp { get; set; } = CategoryConfig.DefaultComp();
}

public sealed class CategoryConfig
{
    [JsonPropertyName("itemInfo")]
    public ItemInfoValues ItemInfo { get; set; } = new();

    [JsonPropertyName("binInfo")]
    public BinInfoValues BinInfo { get; set; } = new();

    public static CategoryConfig DefaultModule() => new()
    {
        ItemInfo = new ItemInfoValues { DefectWarehouse = "제품 폐기창고" },
        BinInfo = new BinInfoValues
        {
            ProcessSearchKey = "M050",
            Rows = [BinRowConfig.Default("M050")]
        }
    };

    public static CategoryConfig DefaultComp() => new()
    {
        ItemInfo = new ItemInfoValues { DefectWarehouse = "COMPONENT 폐기창고" },
        BinInfo = new BinInfoValues
        {
            ProcessSearchKey = "C010",
            Rows = [BinRowConfig.Default("C010")]
        }
    };
}

public sealed class ItemInfoValues
{
    [JsonPropertyName("binManage")]
    public string BinManage { get; set; } = "Y";

    [JsonPropertyName("turnKey")]
    public string TurnKey { get; set; } = "N";

    [JsonPropertyName("assemblyIn")]
    public string AssemblyIn { get; set; } = "Y";

    [JsonPropertyName("defectWarehouse")]
    public string DefectWarehouse { get; set; } = "";
}

public sealed class BinInfoValues
{
    [JsonPropertyName("processSearchKey")]
    public string ProcessSearchKey { get; set; } = "";

    [JsonPropertyName("rows")]
    public List<BinRowConfig> Rows { get; set; } = [];
}

public sealed class BinRowConfig
{
    [JsonPropertyName("processName")]
    public string ProcessName { get; set; } = "";

    [JsonPropertyName("binType")]
    public string BinType { get; set; } = "Normal-1";

    [JsonPropertyName("retestNo")]
    public string RetestNo { get; set; } = "0";

    [JsonPropertyName("binComplete")]
    public string BinComplete { get; set; } = "Y";

    [JsonPropertyName("retestTh")]
    public string RetestTh { get; set; } = "H";

    public static BinRowConfig Default(string processName) => new() { ProcessName = processName };
}

public sealed class GlobalConfig
{
    [JsonPropertyName("recoveryPart")]
    public string RecoveryPart { get; set; } = "RMRDAG58A1B-GPWRRWM7";

    [JsonPropertyName("itemInfoMenuName")]
    public string ItemInfoMenuName { get; set; } = "품목정보관리";

    [JsonPropertyName("binInfoMenuName")]
    public string BinInfoMenuName { get; set; } = "품목별 BIN 정보 관리";
}
```

- [ ] **Step 2: `BinIdResolver.cs`의 `Resolve` 시그니처를 분류별 공정키로 변경**

`public static BinInfoTarget? Resolve(string partNo, BinInfoConfig config)` 를 다음으로 교체하고, 본문에서 `config.ModuleProcessKey`/`config.CompProcessKey` 참조를 새 파라미터로 바꾼다:

```csharp
    public static BinInfoTarget? Resolve(string partNo, string moduleProcessKey, string compProcessKey)
    {
        var code = (partNo ?? "").Trim();
        var cls = PartClassifier.Classify(code);

        if (cls == PartClass.Module)
        {
            if (code.Length < 6 || !ModuleDensityGb.TryGetValue(code.Substring(4, 2), out var gb))
            {
                return null;
            }

            return new BinInfoTarget(cls, moduleProcessKey, $"RAM_Module_Normal_{gb}GB");
        }

        if (cls == PartClass.Comp)
        {
            if (code.Length < 5 || !CompDensity.TryGetValue(code.Substring(3, 2), out var info))
            {
                return null;
            }

            var name = info.Ddr5
                ? $"DRAM_Comp_D5_XMP72_Bin_{info.Gb}Gb"
                : $"DRAM_Comp_Bin_{info.Gb}Gb";
            return new BinInfoTarget(cls, compProcessKey, name);
        }

        return null;
    }
```

- [ ] **Step 3: `BinIdResolverTests.cs`를 새 시그니처에 맞춤**

상단 `private static readonly BinInfoConfig Cfg = new();` 줄을 삭제하고, 세 곳의 호출
`BinIdResolver.Resolve(part, Cfg)` 를 `BinIdResolver.Resolve(part, "M050", "C010")` 로 바꾼다.
(기대값 "M050"/"C010"은 그대로 통과한다.)

- [ ] **Step 4: `UnimesApp.cs` 재배선 — 품목정보관리 영역**

다음 치환을 적용한다(행 번호는 현재 기준, 앵커 문자열로 찾을 것):

- `_config.ItemInfo.MenuName` → `_config.Global.ItemInfoMenuName` (2곳: `NavigateToMenuByF3Async(...)` 호출, `FindItemInfoWindow`의 `=> FindNamedWindow(mainWindow, ...)`).
- `_config.ItemInfo.RecoveryPart` → `_config.Global.RecoveryPart`.
- `_config.ItemInfo.ModuleDefectWarehouse` → `_config.Categories.DramModule.ItemInfo.DefectWarehouse` (2곳).
- `_config.ItemInfo.CompDefectWarehouse` → `_config.Categories.DramComp.ItemInfo.DefectWarehouse` (2곳).
- 품목정보 셀 목표값(현재 `_config.ItemInfo.BinManage/TurnKey/AssemblyIn`, 약 257~259행) 블록을
  파트 분류 기준으로 바꾼다. 해당 `result` 생성 직전에 분류 변수가 있다면 사용하고, 없으면 위에서 쓰는
  분류 변수(`classification`/`cls`)를 재사용한다:

  ```csharp
                var categoryItem = (_config.ResolveCategory(classification)?.ItemInfo) ?? new ItemInfoValues();
                // ...
                BinManage = categoryItem.BinManage,
                TurnKey = categoryItem.TurnKey,
                AssemblyIn = categoryItem.AssemblyIn,
  ```

  (이 영역에서 쓰는 분류 변수명은 248행 부근의 `cls`/`classification` switch와 동일해야 한다. 실제 변수명을
  확인 후 `_config.ResolveCategory(<그 변수>)`로 맞춘다.)

- [ ] **Step 5: `UnimesApp.cs` 재배선 — BIN 정보관리 영역**

- `_config.BinInfo.MenuName` → `_config.Global.BinInfoMenuName` (4곳).
- `BinIdResolver.Resolve(request.PartNo, _config.BinInfo)` →
  `BinIdResolver.Resolve(request.PartNo, _config.Categories.DramModule.BinInfo.ProcessSearchKey, _config.Categories.DramComp.BinInfo.ProcessSearchKey)`.
- BIN 행 채우기(약 1433~1441행)의 평면 값 참조를, 해당 파트 분류의 **행 설정**에서 가져오게 바꾼다.
  채우기 직전에 분류·행을 해석:

  ```csharp
        var binCategory = _config.ResolveCategory(target.Class);
        var binRow = binCategory?.BinInfo.Rows.FirstOrDefault() ?? new BinRowConfig();
  ```

  그리고:
  - `_config.BinInfo.BinType` → `binRow.BinType`
  - `_config.BinInfo.RetestNo` → `binRow.RetestNo`
  - `_config.BinInfo.BinComplete` → `binRow.BinComplete`
  - `_config.BinInfo.RetestTh` → `binRow.RetestTh`

  (`target`은 513행 부근 `BinIdResolver.Resolve` 결과 변수다. 채우기 코드가 `target`을 볼 수 있는
  스코프인지 확인하고, 아니면 같은 분류값 `target.Class`를 그 스코프로 전달한다.)

- [ ] **Step 6: 빌드 확인**

Run: `dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj`
Expected: 성공. 실패 시 누락된 `_config.ItemInfo.*`/`_config.BinInfo.*` 참조를 모두 위 규칙대로 치환.

- [ ] **Step 7: 테스트 확인**

Run: `dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj`
Expected: 기존 + SecretProtector 테스트 모두 PASS.

- [ ] **Step 8: Commit**

```bash
git add src/UnimesAutomation/Models.cs src/UnimesAutomation/BinIdResolver.cs src/UnimesAutomation/UnimesApp.cs tests/UnimesAutomation.Tests/BinIdResolverTests.cs
git commit -m "refactor: restructure config into DRAM Module/Comp categories"
```

---

## Task 4: BIN 다중 행 — 실행 루프는 Flash로 보류 (결정 B)

행 추가/삭제는 설정 모델·UI(Plan 3)에서 다중 행을 **저장**할 수 있게 둔다. 하지만 자동화 **실행**은
현재 분류별 **첫 행**(`Rows.FirstOrDefault()`)만 적용한다 — Task 3에서 이미 그렇게 배선됨.

**근거:** BIN 삽입 루프(`UnimesApp.cs` ~503-650)는 라이브 검증까지 끝난 가장 fragile한 코드이고,
중간 실패 시 `continue`로 그 파트를 건너뛰는 제어흐름을 쓴다. 이를 `foreach (row in rows)`로 감싸면
`continue` 의미가 안쪽 행 루프로 바뀌어 **DRAM 1행 경로까지 회귀**한다(실패해도 저장으로 진행). 제대로
하려면 per-row 시퀀스를 성공/실패 헬퍼로 추출해 제어흐름을 다시 짜야 하는데, 이 다중 행 경로는
DRAM(1행)으로는 실행되지 않아 **지금 라이브 검증이 불가능**하다. 검증 불가능한 복잡성을 fragile
코드에 넣지 않는다(프로젝트 원칙).

**Files:**
- Modify: `src/UnimesAutomation/UnimesApp.cs` (의도 주석만)

- [ ] **Step 1: 실행 지점에 의도 주석 추가**

`FillFixedBinCells` 호출부(약 622행) 위에:

```csharp
                // 실행은 분류별 첫 행만 적용한다. 모델/설정은 다중 행을 담을 수 있으나,
                // 다중 행 실행은 라이브 검증이 가능한 Flash 도입 시점에 추가한다(스펙 §4).
                var binRow = (_config.ResolveCategory(target.Class)?.BinInfo.Rows.FirstOrDefault()) ?? new BinRowConfig();
                FillFixedBinCells(row, binRow);
```

- [ ] **Step 2: 빌드 확인 + 커밋**

Run: `dotnet build ./src/UnimesAutomation/UnimesAutomation.csproj` → 성공.

```bash
git add src/UnimesAutomation/UnimesApp.cs docs/superpowers/specs/2026-06-19-mes-main-settings-design.md docs/superpowers/plans/2026-06-19-mes-config-foundation.md
git commit -m "docs: defer multi-row BIN execution to Flash milestone (decision B)"
```

> **향후(Flash 도입 시):** per-row 시퀀스를 헬퍼로 추출 → 행 루프 구성 → 실 MES 다중 행 설정으로
> 라이브 검증. 그때까지 실행은 첫 행만 적용.

---

## Task 5: ConfigStore (로드/저장/마이그레이션) — TDD

**Files:**
- Create: `src/UnimesAutomation/ConfigStore.cs`
- Test: `tests/UnimesAutomation.Tests/ConfigStoreTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`tests/UnimesAutomation.Tests/ConfigStoreTests.cs`:

```csharp
using System.IO;
using UnimesAutomation;
using Xunit;

public class ConfigStoreTests
{
    [Fact]
    public void Save_then_Load_roundtrips_categories()
    {
        var cfg = RootConfig.CreateDefault();
        cfg.Categories.DramModule.ItemInfo.TurnKey = "Y";
        cfg.Categories.DramComp.BinInfo.ProcessSearchKey = "C999";

        var path = Path.Combine(Path.GetTempPath(), $"unimes_cfg_{Guid.NewGuid():N}.json");
        try
        {
            ConfigStore.Save(path, cfg);
            var loaded = ConfigStore.Load(path);

            Assert.Equal("Y", loaded.Categories.DramModule.ItemInfo.TurnKey);
            Assert.Equal("C999", loaded.Categories.DramComp.BinInfo.ProcessSearchKey);
            Assert.Equal("제품 폐기창고", loaded.Categories.DramModule.ItemInfo.DefectWarehouse);
            Assert.Single(loaded.Categories.DramModule.BinInfo.Rows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_legacy_flat_config_builds_categories()
    {
        var legacy = """
        {
          "login": { "userId": "22402002", "passwordMode": "env" },
          "safety": { "dryRun": true, "saveEnabled": false },
          "itemInfo": {
            "menuName": "품목정보관리", "binManage": "Y", "turnKey": "N", "assemblyIn": "Y",
            "moduleDefectWarehouse": "제품 폐기창고", "compDefectWarehouse": "COMPONENT 폐기창고",
            "recoveryPart": "RMRDAG58A1B-GPWRRWM7"
          },
          "binInfo": {
            "menuName": "품목별 BIN 정보 관리", "moduleProcessKey": "M050", "compProcessKey": "C010",
            "binType": "Normal-1", "retestNo": "0", "binComplete": "Y", "retestTh": "H"
          }
        }
        """;
        var path = Path.Combine(Path.GetTempPath(), $"unimes_legacy_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, legacy);
        try
        {
            var loaded = ConfigStore.Load(path);

            Assert.Equal("제품 폐기창고", loaded.Categories.DramModule.ItemInfo.DefectWarehouse);
            Assert.Equal("COMPONENT 폐기창고", loaded.Categories.DramComp.ItemInfo.DefectWarehouse);
            Assert.Equal("M050", loaded.Categories.DramModule.BinInfo.ProcessSearchKey);
            Assert.Equal("C010", loaded.Categories.DramComp.BinInfo.ProcessSearchKey);
            Assert.Equal("H", loaded.Categories.DramModule.BinInfo.Rows[0].RetestTh);
            Assert.Equal("품목별 BIN 정보 관리", loaded.Global.BinInfoMenuName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var loaded = ConfigStore.Load(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.json"));
        Assert.Equal("M050", loaded.Categories.DramModule.BinInfo.ProcessSearchKey);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj --filter ConfigStoreTests`
Expected: 컴파일 실패(`ConfigStore` 미정의).

- [ ] **Step 3: 구현 작성**

`src/UnimesAutomation/ConfigStore.cs`:

```csharp
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnimesAutomation;

// appsettings.json 단일 파일을 읽고 쓴다. 구버전(플랫 itemInfo/binInfo)은 분류 구조로 마이그레이션한다.
public static class ConfigStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static RootConfig Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return RootConfig.CreateDefault();
        }

        var json = File.ReadAllText(path);
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        if (node is null)
        {
            return RootConfig.CreateDefault();
        }

        if (node["categories"] is not null)
        {
            return node.Deserialize<RootConfig>(ReadOptions) ?? RootConfig.CreateDefault();
        }

        return MigrateLegacy(node);
    }

    public static void Save(string path, RootConfig config)
    {
        var json = JsonSerializer.Serialize(config, WriteOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static RootConfig MigrateLegacy(JsonNode node)
    {
        var cfg = RootConfig.CreateDefault();

        if (node["login"] is JsonNode login)
        {
            cfg.Login = login.Deserialize<LoginConfig>(ReadOptions) ?? cfg.Login;
        }
        if (node["safety"] is JsonNode safety)
        {
            cfg.Safety = safety.Deserialize<SafetyConfig>(ReadOptions) ?? cfg.Safety;
        }
        if (node["app"] is JsonNode app)
        {
            cfg.App = app.Deserialize<AppConfig>(ReadOptions) ?? cfg.App;
        }
        if (node["workflow"] is JsonNode workflow)
        {
            cfg.Workflow = workflow.Deserialize<WorkflowConfig>(ReadOptions) ?? cfg.Workflow;
        }

        var ii = node["itemInfo"];
        var bi = node["binInfo"];

        static string S(JsonNode? n, string key, string fallback)
            => n?[key]?.GetValue<string>() ?? fallback;

        cfg.Global.ItemInfoMenuName = S(ii, "menuName", cfg.Global.ItemInfoMenuName);
        cfg.Global.BinInfoMenuName = S(bi, "menuName", cfg.Global.BinInfoMenuName);
        cfg.Global.RecoveryPart = S(ii, "recoveryPart", cfg.Global.RecoveryPart);

        var binManage = S(ii, "binManage", "Y");
        var turnKey = S(ii, "turnKey", "N");
        var assemblyIn = S(ii, "assemblyIn", "Y");
        var moduleWh = S(ii, "moduleDefectWarehouse", "제품 폐기창고");
        var compWh = S(ii, "compDefectWarehouse", "COMPONENT 폐기창고");

        var moduleKey = S(bi, "moduleProcessKey", "M050");
        var compKey = S(bi, "compProcessKey", "C010");
        var binType = S(bi, "binType", "Normal-1");
        var retestNo = S(bi, "retestNo", "0");
        var binComplete = S(bi, "binComplete", "Y");
        var retestTh = S(bi, "retestTh", "H");

        cfg.Categories.DramModule = new CategoryConfig
        {
            ItemInfo = new ItemInfoValues
            {
                BinManage = binManage, TurnKey = turnKey, AssemblyIn = assemblyIn,
                DefectWarehouse = moduleWh
            },
            BinInfo = new BinInfoValues
            {
                ProcessSearchKey = moduleKey,
                Rows = [new BinRowConfig
                {
                    ProcessName = moduleKey, BinType = binType, RetestNo = retestNo,
                    BinComplete = binComplete, RetestTh = retestTh
                }]
            }
        };

        cfg.Categories.DramComp = new CategoryConfig
        {
            ItemInfo = new ItemInfoValues
            {
                BinManage = binManage, TurnKey = turnKey, AssemblyIn = assemblyIn,
                DefectWarehouse = compWh
            },
            BinInfo = new BinInfoValues
            {
                ProcessSearchKey = compKey,
                Rows = [new BinRowConfig
                {
                    ProcessName = compKey, BinType = binType, RetestNo = retestNo,
                    BinComplete = binComplete, RetestTh = retestTh
                }]
            }
        };

        return cfg;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj --filter ConfigStoreTests`
Expected: 3개 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/UnimesAutomation/ConfigStore.cs tests/UnimesAutomation.Tests/ConfigStoreTests.cs
git commit -m "feat: add ConfigStore with legacy migration"
```

---

## Task 6: Program이 ConfigStore를 사용 + dpapi 로그인 분기

**Files:**
- Modify: `src/UnimesAutomation/Program.cs`
- Modify: `src/UnimesAutomation/UnimesApp.cs`

- [ ] **Step 1: `Program.LoadConfig`가 `ConfigStore.Load`를 쓰도록 변경**

`LoadConfig` 메서드의 마지막 부분(현재 `File.ReadAllText` + `JsonSerializer.Deserialize<RootConfig>` 블록)을
다음으로 교체:

```csharp
        var fullPath = Path.GetFullPath(configPath);
        logger.Info($"Loading config: {fullPath}");
        return ConfigStore.Load(fullPath);
```

(상단의 `using System.Text.Json;`은 다른 곳에서 안 쓰면 제거. 빌드 경고로 확인.)

- [ ] **Step 2: `UnimesApp.ResolveLoginCredentials`에 `dpapi` 분기 추가**

`var mode = (_config.Login.PasswordMode ?? "").Trim().ToLowerInvariant();` 바로 다음에 추가:

```csharp
        if (mode == "dpapi")
        {
            var userId = string.IsNullOrWhiteSpace(_config.Login.UserId)
                ? GetEnvironmentValue(_config.Login.UserIdEnvironmentVariable)
                : _config.Login.UserId;
            var password = SecretProtector.Decrypt(_config.Login.PasswordEncrypted);

            if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrEmpty(password))
            {
                return (userId, password, "dpapi");
            }

            throw new InvalidOperationException(
                "login.passwordMode=dpapi 이지만 복호화된 비밀번호가 비어 있습니다. 설정 창에서 비밀번호를 다시 입력하세요.");
        }
```

- [ ] **Step 3: 빌드 + 테스트 확인**

Run: `dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj` 그리고
`dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj`
Expected: 빌드 성공, 모든 테스트 PASS.

- [ ] **Step 4: Commit**

```bash
git add src/UnimesAutomation/Program.cs src/UnimesAutomation/UnimesApp.cs
git commit -m "feat: load via ConfigStore and support dpapi login mode"
```

---

## Task 7: appsettings.example.json 새 형태로 교체

**Files:**
- Modify: `appsettings.example.json`

- [ ] **Step 1: 새 형태로 전체 교체**

```json
{
  "login": {
    "userId": "22402002",
    "passwordMode": "dpapi",
    "passwordEncrypted": "",
    "userIdEnvironmentVariable": "UNIMES_USER_ID",
    "passwordEnvironmentVariable": "UNIMES_PASSWORD",
    "language": "한국어",
    "system": "UNIMES"
  },
  "safety": { "dryRun": true, "saveEnabled": false },
  "app": {
    "launchPath": "%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\Bizentro\\UNIMES - 1 .appref-ms",
    "windowTitleContains": ["UNIMES"],
    "windowTitleExcludes": ["UNIERP"],
    "processNameHints": ["UNIMES", "SetupMES", "Bizentro.App.MAIN.ClientAgent", "Bizentro.App.MAIN.Shell"],
    "launchTimeoutSeconds": 90,
    "loginTimeoutSeconds": 180,
    "popupTimeoutSeconds": 20,
    "uiDumpMaxDepth": 12,
    "launchMode": "attachOrLaunch"
  },
  "workflow": {
    "enabled": true,
    "inputPartsPath": "input_parts.csv",
    "searchDelayMilliseconds": 1200,
    "stopOnFirstFailure": false,
    "showCompletionDialog": true
  },
  "options": {
    "defectWarehouses": ["제품 폐기창고", "COMPONENT 폐기창고"],
    "binTypes": ["Normal-1"],
    "retestThs": ["H", "L"],
    "binCompletes": ["Y", "N"]
  },
  "categories": {
    "dramModule": {
      "itemInfo": { "binManage": "Y", "turnKey": "N", "assemblyIn": "Y", "defectWarehouse": "제품 폐기창고" },
      "binInfo": {
        "processSearchKey": "M050",
        "rows": [{ "processName": "M050", "binType": "Normal-1", "retestNo": "0", "binComplete": "Y", "retestTh": "H" }]
      }
    },
    "dramComp": {
      "itemInfo": { "binManage": "Y", "turnKey": "N", "assemblyIn": "Y", "defectWarehouse": "COMPONENT 폐기창고" },
      "binInfo": {
        "processSearchKey": "C010",
        "rows": [{ "processName": "C010", "binType": "Normal-1", "retestNo": "0", "binComplete": "Y", "retestTh": "H" }]
      }
    }
  },
  "global": {
    "recoveryPart": "RMRDAG58A1B-GPWRRWM7",
    "itemInfoMenuName": "품목정보관리",
    "binInfoMenuName": "품목별 BIN 정보 관리"
  }
}
```

- [ ] **Step 2: 로드 확인(스모크)**

Run: `dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --config .\appsettings.example.json --dump-only --no-launch`
Expected: 설정 로드 로그 출력 후 정상 종료(파싱 에러 없음).

- [ ] **Step 3: Commit**

```bash
git add appsettings.example.json
git commit -m "docs: update example config to category-based shape"
```

---

## Self-Review (작성자 체크 — 완료)

- **스펙 커버리지**: §4 모델 재구성(Task 3) / §5 영속화·DPAPI·마이그레이션(Task 2,5,6) / §9 분류별 배선(Task 3), 다중 행 실행은 Flash 보류(Task 4, 결정 B). 엑셀 리포트(§8)·GUI(§6,7)·테마(§14)·save-test 제거(§10)는 Plan 2/3.
- **플레이스홀더**: 신규 단위는 전체 코드 포함. `UnimesApp` 치환은 앵커 문자열+규칙으로 명시(거대 파일이라 줄 단위 붙여넣기 대신 정확한 old→new 제시).
- **타입 일관성**: `ItemInfoValues`/`BinInfoValues`/`BinRowConfig`/`CategoryConfig`/`OptionsConfig`/`GlobalConfig`/`ResolveCategory`/`SecretProtector`/`ConfigStore` 명칭이 태스크 전반에서 동일.

## 라이브 검증(이 플랜 완료 후)

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj
dotnet test  .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj
```

그리고 실제 MES에서 새 `appsettings.json`(또는 마이그레이션된 기존 파일)으로 품목정보관리/BIN 1건 미리보기(dryRun) 실행 → 로그로 분류별 값이 맞게 적용되는지 확인(스펙 원칙).

---

## 다음 플랜 (로드맵)

- **Plan 2 — 엑셀 결과 리포트**: `BinResult` 모델, `UnimesApp` BIN 결과 수집, `ResultWorkbook`(ClosedXML) 2시트(품목정보관리/BIN 정보관리, 한글 컬럼, 처리일시), `CsvFiles.WriteResults` 대체. (스펙 §8)
- **Plan 3 — GUI + 다크 HUD 테마**: `MainForm`, `SettingsForm` + `CategorySettingsControl`, GUI 진입(`Program`), 설정 창↔`ConfigStore` 연결, Approach 2 백그라운드 실행+창 안 로그, 안전 토글+확인, 다크 테마, 구 다이얼로그/`appsettings.save-test.json`/save-test 런처 제거. (스펙 §3,6,7,10,14)
