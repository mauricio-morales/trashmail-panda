# TrashMail Panda Theme Specifications

> **Status**: ✅ Implemented (PRP #47)  
> **Reference**: [ProfessionalColors.cs](../src/TrashMailPanda/TrashMailPanda/Theming/ProfessionalColors.cs)

## Design Philosophy

Professional blue-gray theme delivering calm, trustworthy user experience for email security tools.

## Color Palette

### Primary Colors

| Color | Hex | Usage |
|-------|-----|-------|
| **AccentBlue** | `#3A7BD5` | Primary buttons, highlights, active states |
| **BackgroundPrimary** | `#F7F8FA` | Main app background (soft warm gray) |
| **CardBackground** | `#FFFFFF` | Cards, dialogs, elevated surfaces |

### Text Hierarchy

| Color | Hex | Usage |
|-------|-----|-------|
| **TextPrimary** | `#2C3E50` | Headlines, primary content |
| **TextSecondary** | `#546E7A` | Secondary content, descriptions |
| **TextTertiary** | `#90A4AE` | Hints, disabled text |

### Status Colors

| Color | Hex | Usage |
|-------|-----|-------|
| **StatusSuccess** | `#4CAF50` | Connected, healthy providers |
| **StatusWarning** | `#FFB300` | Attention required |
| **StatusError** | `#E57373` | Errors, failed states |
| **StatusInfo** | `#42A5F5` | Information, neutral states |
| **StatusNeutral** | `#78909C` | Inactive, pending states |

## Typography

- **Font Family**: System default sans-serif (Inter via Avalonia.Fonts.Inter)
- **Hierarchy**: Clear size and weight differentiation
- **Contrast**: WCAG AA compliant against backgrounds

## UI Components

### Cards

- **Background**: White (`#FFFFFF`)
- **Border Radius**: 8px
- **Shadow**: Soft shadow for depth
- **Padding**: Consistent internal spacing

### Buttons

- **Primary**: AccentBlue background, white text
- **Secondary**: Transparent background, AccentBlue border
- **Link**: AccentBlue text, no background

### Layout

- **Grid**: 3-column layout for provider cards
- **Spacing**: Consistent margins and padding
- **Responsive**: Adapts to window size

## Implementation

All colors MUST use `ProfessionalColors` class semantic helpers:

```csharp
// ✅ CORRECT
var color = ProfessionalColors.GetStatusColor("Connected");
var accent = ProfessionalColors.AccentBlue;

// ❌ FORBIDDEN - Never hardcode RGB values
var color = Color.Parse("#E57373");
```

## Design Rationale

- **Blue-Gray Palette**: Conveys trust, security, professionalism
- **Soft Backgrounds**: Reduces eye strain, calm user experience
- **Status Colors**: Intuitive, universally understood (green=good, red=error)
- **Card Design**: Modern, organized information hierarchy
- **Semantic Naming**: Enables theming flexibility without code changes

## Mockup Reference

Original design specifications captured in [PRP #47](../PRPs/47-improve-app-color-theme-and-styling.md).
