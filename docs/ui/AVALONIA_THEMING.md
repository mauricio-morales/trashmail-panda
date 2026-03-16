# Avalonia Professional Theming Implementation Guide

This guide provides complete context for implementing professional theme systems in Avalonia UI applications, specifically for transforming the TrashMail Panda Provider Setup Dashboard.

## Professional Blue-Gray Design System

### Color Palette Specifications

Based on the TrashMail Panda mockup and professional design standards:

```xml
<!-- Primary Professional Palette -->
<ResourceDictionary>
    <!-- Background Colors -->
    <SolidColorBrush x:Key="BackgroundPrimary">#F7F8FA</SolidColorBrush>     <!-- Soft warm gray -->
    <SolidColorBrush x:Key="BackgroundSecondary">#FFFFFF</SolidColorBrush>   <!-- Pure white for cards -->
    <SolidColorBrush x:Key="BackgroundTertiary">#E2E8F0</SolidColorBrush>    <!-- Light border gray -->
    
    <!-- Primary Blue-Gray Scale -->
    <SolidColorBrush x:Key="AccentPrimary">#3A7BD5</SolidColorBrush>         <!-- Calm professional blue -->
    <SolidColorBrush x:Key="TextPrimary">#2D3748</SolidColorBrush>           <!-- Dark blue-gray text -->
    <SolidColorBrush x:Key="TextSecondary">#4A5568</SolidColorBrush>         <!-- Medium blue-gray -->
    <SolidColorBrush x:Key="TextTertiary">#718096</SolidColorBrush>          <!-- Light blue-gray -->
    
    <!-- Status Colors -->
    <SolidColorBrush x:Key="StatusSuccess">#4CAF50</SolidColorBrush>         <!-- Professional green -->
    <SolidColorBrush x:Key="StatusWarning">#FFB300</SolidColorBrush>         <!-- Amber warning -->
    <SolidColorBrush x:Key="StatusError">#E57373</SolidColorBrush>           <!-- Desaturated red -->
    <SolidColorBrush x:Key="StatusInfo">#3A7BD5</SolidColorBrush>            <!-- Information blue -->
    
    <!-- Border and Surface Colors -->
    <SolidColorBrush x:Key="BorderLight">#E2E8F0</SolidColorBrush>           <!-- Light borders -->
    <SolidColorBrush x:Key="BorderMedium">#CBD5E1</SolidColorBrush>          <!-- Medium borders -->
    <SolidColorBrush x:Key="ShadowColor">#00000015</SolidColorBrush>         <!-- Subtle shadow -->
</ResourceDictionary>
```

### Color Psychology and Usage

**Professional Blue (#3A7BD5)**:
- Conveys trust, security, and reliability
- Use for: Primary action buttons, links, loading states
- Avoid: Overuse that creates cold feeling

**Soft Gray Background (#F7F8FA)**:
- Creates calm, professional atmosphere  
- Reduces eye strain during extended use
- Perfect for dashboard backgrounds and large surfaces

**White Cards**:
- Provide clear content separation
- Create visual hierarchy through elevation
- Essential for grouping related information

## FluentTheme Customization

### Complete FluentTheme Palette Override

```xml
<Application.Styles>
    <FluentTheme>
        <FluentTheme.Palettes>
            <!-- Professional Light Theme -->
            <ColorPaletteResources x:Key="Light" 
                                 Accent="#3A7BD5"                    <!-- Primary accent color -->
                                 RegionColor="#F7F8FA"               <!-- Main background -->
                                 AltHighColor="#FFFFFF"              <!-- Card/surface backgrounds -->
                                 AltMediumHighColor="#E2E8F0"        <!-- Light borders -->
                                 AltMediumColor="#CBD5E1"            <!-- Medium borders -->
                                 AltLowColor="#94A3B8"               <!-- Disabled elements -->
                                 ChromeAltLowColor="#4A5568"         <!-- Secondary text -->
                                 ChromeMediumColor="#2D3748"         <!-- Primary text -->
                                 ChromeHighColor="#1A202C"           <!-- High contrast text -->
                                 ListLowColor="#F1F5F9"              <!-- List item backgrounds -->
                                 SystemAccentColor="#3A7BD5"         <!-- System accent override -->
                                 SystemAccentColorLight1="#60A5FA"   <!-- Light accent variant -->
                                 SystemAccentColorLight2="#93C5FD"   <!-- Lighter accent variant -->
                                 SystemAccentColorDark1="#2563EB"    <!-- Dark accent variant --> 
                                 SystemAccentColorDark2="#1D4ED8" /> <!-- Darker accent variant -->
                                 
            <!-- Professional Dark Theme (future) -->
            <ColorPaletteResources x:Key="Dark" 
                                 Accent="#60A5FA"
                                 RegionColor="#1A202C"
                                 AltHighColor="#2D3748"
                                 AltMediumHighColor="#374151"
                                 AltMediumColor="#4B5563"
                                 AltLowColor="#6B7280"
                                 ChromeAltLowColor="#9CA3AF"
                                 ChromeMediumColor="#D1D5DB"
                                 ChromeHighColor="#F9FAFB"
                                 ListLowColor="#374151"
                                 SystemAccentColor="#60A5FA"
                                 SystemAccentColorLight1="#93C5FD"
                                 SystemAccentColorLight2="#BFDBFE"
                                 SystemAccentColorDark1="#3B82F6"
                                 SystemAccentColorDark2="#2563EB" />
        </FluentTheme.Palettes>
    </FluentTheme>
</Application.Styles>
```

### Critical FluentTheme Properties

Each ColorPaletteResources property serves specific purposes:

- **Accent**: Primary brand color for buttons, links, highlights
- **RegionColor**: Main application background
- **AltHighColor**: Card backgrounds, elevated surfaces
- **ChromeMediumColor**: Primary text color
- **ChromeAltLowColor**: Secondary text, subtitles
- **SystemAccentColor**: Overrides system accent for consistency

## Professional Component Styling

### Card System with Elevation

```xml
<ResourceDictionary>
    <!-- Base Card Style -->
    <Style x:Key="ProfessionalCard" Selector="Border">
        <Setter Property="Background" Value="{DynamicResource AltHighColor}" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="Padding" Value="24" />
        <Setter Property="BorderBrush" Value="{DynamicResource AltMediumHighColor}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="BoxShadow" Value="0 4 12 0 #00000015" />
    </Style>
    
    <!-- Provider Status Card -->
    <Style x:Key="ProviderCard" Selector="Border">
        <Setter Property="Background" Value="{DynamicResource AltHighColor}" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="Padding" Value="20" />
        <Setter Property="BorderBrush" Value="{DynamicResource AltMediumHighColor}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="BoxShadow" Value="0 2 8 0 #00000010" />
        <Setter Property="MinHeight" Value="180" />
        <Setter Property="Margin" Value="8" />
    </Style>
    
    <!-- Elevated Card (hover states) -->
    <Style x:Key="ElevatedCard" Selector="Border">
        <Setter Property="Background" Value="{DynamicResource AltHighColor}" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="Padding" Value="24" />
        <Setter Property="BorderBrush" Value="{DynamicResource AltMediumHighColor}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="BoxShadow" Value="0 8 16 0 #0000001A" />
    </Style>
</ResourceDictionary>
```

### Professional Button System

```xml
<ResourceDictionary>
    <!-- Primary Action Button -->
    <Style x:Key="PrimaryButton" Selector="Button">
        <Setter Property="Background" Value="{DynamicResource SystemAccentColor}" />
        <Setter Property="Foreground" Value="White" />
        <Setter Property="BorderBrush" Value="{DynamicResource SystemAccentColor}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="6" />
        <Setter Property="Padding" Value="16,8" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="MinHeight" Value="36" />
    </Style>
    
    <Style Selector="Button.PrimaryButton:pointerover /template/ ContentPresenter#PART_ContentPresenter">
        <Setter Property="Background" Value="{DynamicResource SystemAccentColorDark1}" />
        <Setter Property="BorderBrush" Value="{DynamicResource SystemAccentColorDark1}" />
    </Style>
    
    <!-- Secondary Button -->
    <Style x:Key="SecondaryButton" Selector="Button">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Foreground" Value="{DynamicResource SystemAccentColor}" />
        <Setter Property="BorderBrush" Value="{DynamicResource SystemAccentColor}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="6" />
        <Setter Property="Padding" Value="16,8" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="MinHeight" Value="36" />
    </Style>
    
    <!-- Danger Button -->
    <Style x:Key="DangerButton" Selector="Button">
        <Setter Property="Background" Value="#E57373" />
        <Setter Property="Foreground" Value="White" />
        <Setter Property="BorderBrush" Value="#E57373" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="6" />
        <Setter Property="Padding" Value="16,8" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="MinHeight" Value="36" />
    </Style>
</ResourceDictionary>
```

### Professional Typography System

```xml
<ResourceDictionary>
    <!-- Display Typography -->
    <Style x:Key="DisplayLarge" Selector="TextBlock">
        <Setter Property="FontFamily" Value="Inter, SF Pro Display, Segoe UI, sans-serif" />
        <Setter Property="FontSize" Value="32" />
        <Setter Property="FontWeight" Value="Bold" />
        <Setter Property="LineHeight" Value="40" />
        <Setter Property="Foreground" Value="{DynamicResource ChromeHighColor}" />
    </Style>
    
    <!-- Headline Typography -->
    <Style x:Key="HeadlineLarge" Selector="TextBlock">
        <Setter Property="FontFamily" Value="Inter, SF Pro Display, Segoe UI, sans-serif" />
        <Setter Property="FontSize" Value="24" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="LineHeight" Value="32" />
        <Setter Property="Foreground" Value="{DynamicResource ChromeMediumColor}" />
        <Setter Property="Margin" Value="0,0,0,16" />
    </Style>
    
    <Style x:Key="HeadlineMedium" Selector="TextBlock">
        <Setter Property="FontFamily" Value="Inter, SF Pro Display, Segoe UI, sans-serif" />
        <Setter Property="FontSize" Value="20" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="LineHeight" Value="28" />
        <Setter Property="Foreground" Value="{DynamicResource ChromeMediumColor}" />
        <Setter Property="Margin" Value="0,0,0,12" />
    </Style>
    
    <!-- Title Typography -->
    <Style x:Key="TitleLarge" Selector="TextBlock">
        <Setter Property="FontFamily" Value="Inter, SF Pro Text, Segoe UI, sans-serif" />
        <Setter Property="FontSize" Value="18" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="LineHeight" Value="24" />
        <Setter Property="Foreground" Value="{DynamicResource ChromeMediumColor}" />
    </Style>
    
    <Style x:Key="TitleMedium" Selector="TextBlock">
        <Setter Property="FontFamily" Value="Inter, SF Pro Text, Segoe UI, sans-serif" />
        <Setter Property="FontSize" Value="16" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="LineHeight" Value="22" />
        <Setter Property="Foreground" Value="{DynamicResource ChromeMediumColor}" />
    </Style>
    
    <!-- Body Typography -->
    <Style x:Key="BodyLarge" Selector="TextBlock">
        <Setter Property="FontFamily" Value="Inter, SF Pro Text, Segoe UI, sans-serif" />
        <Setter Property="FontSize" Value="16" />
        <Setter Property="FontWeight" Value="Regular" />
        <Setter Property="LineHeight" Value="24" />
        <Setter Property="Foreground" Value="{DynamicResource ChromeMediumColor}" />
    </Style>
    
    <Style x:Key="BodyMedium" Selector="TextBlock">
        <Setter Property="FontFamily" Value="Inter, SF Pro Text, Segoe UI, sans-serif" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="FontWeight" Value="Regular" />
        <Setter Property="LineHeight" Value="20" />
        <Setter Property="Foreground" Value="{DynamicResource ChromeMediumColor}" />
    </Style>
    
    <!-- Label Typography -->
    <Style x:Key="LabelLarge" Selector="TextBlock">
        <Setter Property="FontFamily" Value="Inter, SF Pro Text, Segoe UI, sans-serif" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="LineHeight" Value="20" />
        <Setter Property="Foreground" Value="{DynamicResource ChromeAltLowColor}" />
    </Style>
    
    <Style x:Key="LabelMedium" Selector="TextBlock">
        <Setter Property="FontFamily" Value="Inter, SF Pro Text, Segoe UI, sans-serif" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="LineHeight" Value="16" />
        <Setter Property="Foreground" Value="{DynamicResource ChromeAltLowColor}" />
        <Setter Property="TextTransform" Value="Uppercase" />
        <Setter Property="LetterSpacing" Value="0.5" />
    </Style>
</ResourceDictionary>
```

## BoxShadow Implementation Guide

### Critical BoxShadow Syntax

Avalonia BoxShadows use specific syntax: `offsetX offsetY blur spread color`

**Professional Shadow Levels**:

```xml
<ResourceDictionary>
    <!-- Subtle Card Shadow -->
    <BoxShadows x:Key="ShadowSmall">0 2 4 0 #0000000D</BoxShadows>
    
    <!-- Standard Card Shadow -->
    <BoxShadows x:Key="ShadowMedium">0 4 8 0 #00000015</BoxShadows>
    
    <!-- Elevated Card Shadow -->
    <BoxShadows x:Key="ShadowLarge">0 8 16 0 #0000001A</BoxShadows>
    
    <!-- Modal/Dialog Shadow -->
    <BoxShadows x:Key="ShadowXLarge">0 16 32 0 #0000001F</BoxShadows>
    
    <!-- Provider Dashboard Card Shadow (from mockup) -->
    <BoxShadows x:Key="DashboardCardShadow">0 4 12 0 #00000015</BoxShadows>
</ResourceDictionary>
```

### Shadow Usage Patterns

```xml
<!-- Standard Usage -->
<Border BoxShadow="{StaticResource ShadowMedium}">
    <!-- Card content -->
</Border>

<!-- Inline Usage -->
<Border BoxShadow="0 4 12 0 #00000015">
    <!-- Card content -->
</Border>

<!-- Hover State Enhancement -->
<Style Selector="Border.interactive:pointerover">
    <Setter Property="BoxShadow" Value="{StaticResource ShadowLarge}" />
</Style>
```

## Status Indication System

### Professional Status Colors

Based on the TrashMail Panda provider status requirements:

```xml
<ResourceDictionary>
    <!-- Status Color Palette -->
    <SolidColorBrush x:Key="StatusHealthy">#4CAF50</SolidColorBrush>          <!-- Professional green -->
    <SolidColorBrush x:Key="StatusSetupRequired">#FFB300</SolidColorBrush>    <!-- Amber warning -->
    <SolidColorBrush x:Key="StatusError">#E57373</SolidColorBrush>            <!-- Soft red -->
    <SolidColorBrush x:Key="StatusLoading">#3A7BD5</SolidColorBrush>          <!-- Professional blue -->
    <SolidColorBrush x:Key="StatusUnknown">#94A3B8</SolidColorBrush>          <!-- Neutral gray -->
    
    <!-- Status Indicator Styles -->
    <Style x:Key="StatusIndicator" Selector="Ellipse">
        <Setter Property="Width" Value="12" />
        <Setter Property="Height" Value="12" />
        <Setter Property="Fill" Value="{DynamicResource StatusUnknown}" />
    </Style>
    
    <Style Selector="Ellipse.status-healthy">
        <Setter Property="Fill" Value="{DynamicResource StatusHealthy}" />
    </Style>
    
    <Style Selector="Ellipse.status-warning">
        <Setter Property="Fill" Value="{DynamicResource StatusSetupRequired}" />
    </Style>
    
    <Style Selector="Ellipse.status-error">
        <Setter Property="Fill" Value="{DynamicResource StatusError}" />
    </Style>
    
    <Style Selector="Ellipse.status-loading">
        <Setter Property="Fill" Value="{DynamicResource StatusLoading}" />
    </Style>
</ResourceDictionary>
```

### Status Badge Components

```xml
<ResourceDictionary>
    <!-- Status Badge Base -->
    <Style x:Key="StatusBadge" Selector="Border">
        <Setter Property="CornerRadius" Value="12" />
        <Setter Property="Padding" Value="8,4" />
        <Setter Property="HorizontalAlignment" Value="Left" />
        <Setter Property="VerticalAlignment" Value="Center" />
    </Style>
    
    <!-- Success Badge -->
    <Style x:Key="SuccessBadge" Selector="Border">
        <Setter Property="Background" Value="#F0FDF4" />
        <Setter Property="BorderBrush" Value="{DynamicResource StatusHealthy}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="12" />
        <Setter Property="Padding" Value="8,4" />
    </Style>
    
    <!-- Warning Badge -->
    <Style x:Key="WarningBadge" Selector="Border">
        <Setter Property="Background" Value="#FFFBEB" />
        <Setter Property="BorderBrush" Value="{DynamicResource StatusSetupRequired}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="12" />
        <Setter Property="Padding" Value="8,4" />
    </Style>
    
    <!-- Error Badge -->
    <Style x:Key="ErrorBadge" Selector="Border">
        <Setter Property="Background" Value="#FEF2F2" />
        <Setter Property="BorderBrush" Value="{DynamicResource StatusError}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="12" />
        <Setter Property="Padding" Value="8,4" />
    </Style>
</ResourceDictionary>
```

## Implementation Best Practices

### Resource Organization

1. **Separate Concerns**: Create focused resource files
   - `Colors.axaml` - Color definitions only
   - `Typography.axaml` - Font and text styles
   - `Components.axaml` - UI component styles
   - `Theme.axaml` - FluentTheme palette overrides

2. **Use Semantic Naming**: 
   - `BackgroundPrimary` instead of `Gray50`
   - `AccentBlue` instead of `Blue500`
   - `StatusSuccess` instead of `Green400`

3. **Dynamic Resource Binding**: Use `DynamicResource` for theme-aware properties

### Theme Integration Workflow

1. **Create Color Palette**: Define all colors centrally
2. **Override FluentTheme**: Customize base theme palette  
3. **Build Component Library**: Create reusable styled components
4. **Apply Systematically**: Update all UI elements consistently
5. **Test Thoroughly**: Verify visual appearance and functionality

### Performance Considerations

- Use `StaticResource` for colors that never change
- Use `DynamicResource` only for theme-aware properties
- Minimize resource lookup chains
- Cache complex styles for frequently used components

This guide provides complete context for implementing professional themes in Avalonia UI applications with specific focus on the TrashMail Panda Provider Setup Dashboard transformation.