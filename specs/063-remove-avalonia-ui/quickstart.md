# Quickstart: Remove Avalonia UI — Implementation Guide

**Branch**: `063-remove-avalonia-ui` | **Date**: 2026-03-23  
**Prerequisite**: Branch checked out, issue #62 (Spectre.Console TUI) already merged to main.

---

## Goal

Execute the Avalonia removal in a safe, build-verified sequence. Every phase ends with a
successful `dotnet build`, allowing you to commit checkpoint-by-checkpoint with a green build.

---

## Phase A — Delete Physical UI Files

Delete all Avalonia source files. No .csproj changes yet — the existing
`<Compile Remove="..."/>` guards keep the build green during deletion.

```bash
cd /Users/mmorales/Dev/trashmail-panda

# 1. Delete Views
rm -rf src/TrashMailPanda/TrashMailPanda/Views/

# 2. Delete ViewModels
rm -rf src/TrashMailPanda/TrashMailPanda/ViewModels/

# 3. Delete Styles, Theming, Converters
rm -rf src/TrashMailPanda/TrashMailPanda/Styles/
rm -rf src/TrashMailPanda/TrashMailPanda/Theming/
rm -rf src/TrashMailPanda/TrashMailPanda/Converters/

# 4. Delete Avalonia app entry points
rm src/TrashMailPanda/TrashMailPanda/App.axaml
rm src/TrashMailPanda/TrashMailPanda/App.axaml.cs
rm src/TrashMailPanda/TrashMailPanda/ViewLocator.cs

# 5. Delete build artifacts + manifest
rm -rf src/TrashMailPanda/TrashMailPanda/.avalonia-build-tasks/
rm src/TrashMailPanda/TrashMailPanda/app.manifest

# 6. Delete legacy service
rm src/TrashMailPanda/TrashMailPanda/Services/StartupHostedService.cs

# 7. Delete ViewModel and Converter test files
rm src/Tests/TrashMailPanda.Tests/ViewModels/ProviderStatusCardViewModelTests.cs
rm src/Tests/TrashMailPanda.Tests/ViewModels/ProviderStatusDashboardViewModelTests.cs
rm src/Tests/TrashMailPanda.Tests/Converters/StatusConvertersTests.cs

# Verify build still passes (Compile Remove guards still in place)
dotnet build --configuration Release
```

---

## Phase B — Strip EnableAvaloniaUI Scaffolding from TrashMailPanda.csproj

Edit `src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj`:

**Before** (relevant sections):
```xml
<PropertyGroup>
  <EnableAvaloniaUI Condition="'$(EnableAvaloniaUI)' == ''">false</EnableAvaloniaUI>
  <TargetFramework>net9.0</TargetFramework>
  ...
</PropertyGroup>

<!-- Console-only build (default) -->
<PropertyGroup Condition="'$(EnableAvaloniaUI)' != 'true'">
  <OutputType>Exe</OutputType>
  <EnableDefaultAvaloniaItems>false</EnableDefaultAvaloniaItems>
</PropertyGroup>

<!-- Avalonia-enabled build (opt-in) -->
<PropertyGroup Condition="'$(EnableAvaloniaUI)' == 'true'">
  <OutputType>WinExe</OutputType>
  ...
</PropertyGroup>

<!-- Exclude Avalonia-specific source files when building without Avalonia -->
<ItemGroup Condition="'$(EnableAvaloniaUI)' != 'true'">
  <Compile Remove="Views\**" />
  ...
</ItemGroup>

<!-- Avalonia assets (opt-in only) -->
<ItemGroup Condition="'$(EnableAvaloniaUI)' == 'true'">
  <AvaloniaResource Include="Assets\**" />
</ItemGroup>

<!-- Avalonia packages (opt-in only) -->
<ItemGroup Condition="'$(EnableAvaloniaUI)' == 'true'">
  <PackageReference Include="Avalonia" Version="11.3.4" />
  ...
  <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.1" />
</ItemGroup>
```

**After** (clean form):
```xml
<PropertyGroup>
  <TargetFramework>net9.0</TargetFramework>
  <OutputType>Exe</OutputType>
  <Nullable>enable</Nullable>
  <EnableDefaultItems>true</EnableDefaultItems>
</PropertyGroup>
```

> The `Folder Include="Models\"` item, `appsettings.json` update item, and all project/package
> references that are NOT in the Avalonia-conditional `ItemGroup` are preserved unchanged.

Verify:
```bash
dotnet build --configuration Release
```

---

## Phase C — Strip Avalonia from TrashMailPanda.Tests.csproj

Edit `src/Tests/TrashMailPanda.Tests/TrashMailPanda.Tests.csproj`:

1. Delete the `EnableAvaloniaUI` property declaration.
2. Delete the `ItemGroup Condition="'$(EnableAvaloniaUI)' != 'true'"` exclusion block.
3. Delete `<PackageReference Include="Avalonia" Version="11.3.4" />`.

Verify:
```bash
dotnet build --configuration Release
```

---

## Phase D — Remove #if AVALONIA_UI Blocks from ServiceCollectionExtensions.cs

In `src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs`:

1. Remove at the top of file:
   ```csharp
   #if AVALONIA_UI
   using TrashMailPanda.ViewModels;
   #endif
   ```

2. Remove call-site in `AddTrashMailPandaServices`:
   ```csharp
   #if AVALONIA_UI
           // Add view models
           services.AddViewModels();
   #endif
   ```

3. Remove the entire `AddViewModels` method:
   ```csharp
   #if AVALONIA_UI
       private static IServiceCollection AddViewModels(this IServiceCollection services) { … }
   #endif
   ```

Verify:
```bash
dotnet build --configuration Release
```

---

## Phase E — Remove UIMode Enum Value and All Call Sites

### 5a. `Models/Console/OperationalMode.cs` — remove `UIMode`

Delete:
```csharp
/// <summary>
/// Launch Avalonia UI application mode (requires Storage and Gmail).
/// </summary>
UIMode,
```

### 5b. `Services/ApplicationOrchestrator.cs` — remove `UIMode` case

Delete the case block (approximately line 302):
```csharp
case OperationalMode.UIMode:
    EmitEvent(new StatusMessageEvent { Message = "[dim]This mode will launch the Avalonia desktop UI.[/]" });
    break;
```

### 5c. `Services/Console/ModeSelectionMenu.cs` — remove `UIMode` menu entry

Delete from `GetAvailableModesAsync()` (approximately line 178–184):
```csharp
// UI Mode - requires Storage + Gmail
(OperationalMode.UIMode,
 gmailHealthy ? "🖥️  Launch UI Mode" : "🖥️  Launch UI Mode [dim](Requires Gmail)[/]",
 gmailHealthy),
```

Verify:
```bash
dotnet build --configuration Release
```

---

## Phase F — Final Verification

```bash
# 1. Clean build
dotnet build --configuration Release

# 2. Full test suite
dotnet test --configuration Release

# 3. Grep audits (both must return zero lines)
grep -r "Avalonia" src/ --include="*.cs" --include="*.csproj"
grep -r "ObservableProperty\|RelayCommand\|ObservableObject\|CommunityToolkit" src/ --include="*.cs"

# 4. Format check
dotnet format --verify-no-changes

# 5. Run application (smoke test)
dotnet run --project src/TrashMailPanda

# 6. Startup timing measurement
time dotnet run --project src/TrashMailPanda -- --exit-after-startup 2>/dev/null
# Target: < 500 ms wall time to first interactive prompt

# 7. Binary size comparison (publish self-contained)
dotnet publish src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj \
  -c Release -r osx-x64 --self-contained -o /tmp/trashmail-console
du -sh /tmp/trashmail-console/
# Compare against stored baseline from EnableAvaloniaUI=true build
```

---

## Expected Outcomes

| Check | Expected Result |
|-------|----------------|
| `dotnet build --configuration Release` | ✅ 0 errors, 0 new warnings |
| `dotnet test --configuration Release` | ✅ All previously passing tests pass |
| `grep -r "Avalonia" src/` | ✅ Zero matches |
| `grep -r "ObservableProperty\|RelayCommand" src/` | ✅ Zero matches |
| Application runs | ✅ Spectre.Console TUI appears, provider status shown |
| Binary size | ✅ ≥ 50% smaller than pre-cleanup |
| Startup time | ✅ < 500 ms to first interactive prompt |

---

## Rollback

All changes on branch `063-remove-avalonia-ui`. To rollback:
```bash
git checkout main
```

The `main` branch retains the original Avalonia files until this branch is merged.
