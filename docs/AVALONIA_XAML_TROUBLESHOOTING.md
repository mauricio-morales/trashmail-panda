# Avalonia XAML Compilation Troubleshooting Guide

## Common XAML Compilation Errors and Solutions

### Error: "No precompiled XAML found for [AppName].App"

This error typically indicates one of several issues with XAML resource inclusion or compilation.

#### Root Causes & Solutions

1. **Missing or Invalid x:Class Attribute**
   ```xml
   <!-- CORRECT App.axaml -->
   <Application xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="YourApp.App">
       <!-- content -->
   </Application>
   ```

2. **Incorrect .csproj XAML Resource Configuration**
   ```xml
   <!-- CORRECT .csproj settings -->
   <PropertyGroup>
       <EnableDefaultAvaloniaItems>true</EnableDefaultAvaloniaItems>
       <AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>
   </PropertyGroup>
   
   <!-- Manual inclusion if needed -->
   <ItemGroup>
       <AvaloniaResource Include="**/*.axaml" />
   </ItemGroup>
   ```

3. **XAML Syntax Errors Blocking Compilation**

### Grid vs Border Properties in Avalonia

**Critical:** Grid elements do NOT support visual styling properties.

#### ❌ INCORRECT - Grid with styling properties
```xml
<Grid Padding="16,8"           <!-- Grid doesn't have Padding -->
      CornerRadius="4"         <!-- Grid doesn't have CornerRadius -->  
      Background="#F5F5F5">    <!-- This works but limited -->
    <!-- content -->
</Grid>
```

#### ✅ CORRECT - Use Border for styling
```xml
<Border Padding="16,8"
        CornerRadius="4"
        Background="#F5F5F5">
    <Grid ColumnDefinitions="*,Auto">
        <!-- content -->
    </Grid>
</Border>
```

### Avalonia Element Capabilities Matrix

| Property | Grid | Border | StackPanel | Button | TextBlock |
|----------|------|--------|------------|--------|-----------|
| Padding | ❌ | ✅ | ✅ | ✅ | ✅ |
| CornerRadius | ❌ | ✅ | ❌ | ✅ | ❌ |
| Background | ⚠️ Limited | ✅ | ✅ | ✅ | ✅ |
| Margin | ✅ | ✅ | ✅ | ✅ | ✅ |

### CSS Selector Syntax in Avalonia Styles

#### ❌ INCORRECT - Invalid operators
```xml
<Style Selector="UserControl[Width^='600']">  <!-- ^ is invalid -->
<Style Selector="Button[Content~='OK']">      <!-- ~ is invalid -->
```

#### ✅ CORRECT - Valid Avalonia selectors
```xml
<Style Selector="UserControl">
<Style Selector="Button.accent">
<Style Selector="TextBlock:pointerover">
<Style Selector="Grid > Button">
```

### Valid Avalonia CSS Selector Operators
- `=` - Exact match
- `:` - Pseudo-classes (:hover, :pressed, :selected)
- `.` - Style classes
- `>` - Direct child
- ` ` - Descendant
- `#` - Name selector

### MultiBinding and Converters

#### Complex MultiBinding Issues
```xml
<!-- PROBLEMATIC - Complex MultiBinding -->
<Button.Command>
    <MultiBinding Converter="{x:Static BoolConverters.Or}">
        <Binding Path="Command1"/>
        <Binding Path="Command2"/>
    </MultiBinding>
</Button.Command>
```

#### Simpler Solutions
```xml
<!-- BETTER - Single command with conditional logic -->
<Button Command="{Binding ConditionalCommand}" />

<!-- OR - Custom converter -->
<Button Command="{Binding Path=SomeProperty, 
                          Converter={x:Static local:MyConverter.Instance}}" />
```

### ScrollViewer Content Rules

#### ❌ INCORRECT - Multiple direct children
```xml
<ScrollViewer>
    <StackPanel />
    <Grid />        <!-- Second child causes error -->
</ScrollViewer>
```

#### ✅ CORRECT - Single child container
```xml
<ScrollViewer>
    <StackPanel>
        <StackPanel />  <!-- Nested content OK -->
        <Grid />        <!-- Nested content OK -->
    </StackPanel>
</ScrollViewer>
```

## Build Troubleshooting Steps

1. **Clean and Rebuild**
   ```bash
   dotnet clean
   dotnet restore
   dotnet build --verbosity detailed
   ```

2. **Check XAML Compilation**
   - Look for `XamlParseException` in build output
   - Check line numbers for syntax errors
   - Verify all referenced converters exist

3. **Verify Resource Inclusion**
   - Ensure `.axaml` files are included as `AvaloniaResource`
   - Check `EnableDefaultAvaloniaItems` is true

4. **Validate XAML Syntax**
   - Use proper element hierarchy (Border > Grid, not Grid with styling)
   - Verify CSS selector syntax
   - Check converter references

## .NET 9 + Avalonia 11.3.4 Compatibility

**Status:** Generally compatible with considerations:
- Ensure latest NuGet packages
- Some MSBuild changes in .NET 9 may affect XAML compilation
- Use explicit package references if issues persist

**Recommended Package Versions:**
```xml
<PackageReference Include="Avalonia" Version="11.3.4" />
<PackageReference Include="Avalonia.Desktop" Version="11.3.4" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.4" />
```

## Quick Fix Checklist

- [ ] App.axaml exists with correct x:Class
- [ ] No Grid elements with Padding/CornerRadius
- [ ] CSS selectors use valid operators
- [ ] ScrollViewer has single child
- [ ] All converters are defined and imported
- [ ] .csproj has EnableDefaultAvaloniaItems=true
- [ ] Clean build with detailed verbosity