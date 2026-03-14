# Avalonia MVVM Provider Dashboard Patterns

## Overview
This document provides comprehensive patterns for building provider status dashboards in TrashMail Panda using Avalonia UI 11 and CommunityToolkit.Mvvm.

## Core MVVM Architecture Patterns

### ViewModelBase Pattern
```csharp
// Based on existing TrashMail Panda pattern
public class ViewModelBase : ObservableObject
{
    // Uses CommunityToolkit.Mvvm's ObservableObject for INotifyPropertyChanged
}
```

### Observable Properties Pattern
```csharp
[ObservableProperty]
private bool _isLoading = false;

[ObservableProperty]
private string _status = "Checking...";

[ObservableProperty]
private ObservableCollection<ProviderStatusCardViewModel> _providers = new();
```

### Command Pattern
```csharp
[RelayCommand]
private async Task RefreshStatusAsync()
{
    IsLoading = true;
    try
    {
        await PerformRefreshAsync();
    }
    finally
    {
        IsLoading = false;
    }
}
```

## Provider Status Card ViewModel Pattern

### Core Properties
```csharp
public partial class ProviderStatusCardViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _providerName = string.Empty;

    [ObservableProperty]
    private bool _isHealthy = false;

    [ObservableProperty]
    private bool _requiresSetup = false;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading = false;

    // Computed properties for UI state
    public string StatusIcon => IsHealthy ? "✅" : RequiresSetup ? "⚙️" : "❌";
    public string StatusColor => IsHealthy ? "#4CAF50" : RequiresSetup ? "#FF9800" : "#F44336";
    public string ButtonText => RequiresSetup ? "Setup" : IsHealthy ? "Reconnect" : "Configure";
}
```

### Update Pattern
```csharp
public void UpdateFromProviderStatus(ProviderStatus status)
{
    ProviderName = status.Name;
    IsHealthy = status.IsHealthy;
    RequiresSetup = status.RequiresSetup;
    ErrorMessage = status.ErrorMessage;

    // Update computed properties
    OnPropertyChanged(nameof(StatusIcon));
    OnPropertyChanged(nameof(StatusColor));
    OnPropertyChanged(nameof(ButtonText));
}
```

## Card-Based XAML Layout Pattern

### Modern Card Design
```xml
<Border Background="White" 
        CornerRadius="12" 
        Padding="20" 
        Margin="8"
        BorderBrush="#E0E0E0" 
        BorderThickness="1">
    <Border.Styles>
        <Style Selector="Border:pointerover">
            <Setter Property="BorderBrush" Value="#BDBDBD"/>
            <Setter Property="BoxShadow" Value="0 2 8 rgba(0,0,0,0.1)"/>
        </Style>
    </Border.Styles>

    <Grid RowDefinitions="Auto,*,Auto">
        <!-- Header with Status Icon -->
        <StackPanel Orientation="Horizontal" Spacing="10">
            <Border Width="32" Height="32" 
                   Background="{Binding StatusColor}" 
                   CornerRadius="16">
                <TextBlock Text="{Binding StatusIcon}" 
                          FontSize="18" 
                          HorizontalAlignment="Center" 
                          VerticalAlignment="Center"/>
            </Border>
            
            <StackPanel>
                <TextBlock Text="{Binding DisplayName}" 
                          FontSize="16" 
                          FontWeight="SemiBold"/>
                <TextBlock Text="{Binding Status}" 
                          FontSize="12" 
                          Foreground="Gray"/>
            </StackPanel>
        </StackPanel>

        <!-- Status Details -->
        <StackPanel Grid.Row="1" Spacing="8" Margin="0,15,0,15">
            <!-- Error Message -->
            <Border IsVisible="{Binding !!ErrorMessage}"
                   Background="#FFEBEE" 
                   Padding="8" 
                   CornerRadius="4">
                <TextBlock Text="{Binding ErrorMessage}" 
                          FontSize="11" 
                          TextWrapping="Wrap"
                          Foreground="#C62828"/>
            </Border>
        </StackPanel>

        <!-- Action Buttons -->
        <StackPanel Grid.Row="2" Orientation="Horizontal" Spacing="8">
            <Button Content="{Binding ButtonText}"
                   Command="{Binding SetupProviderCommand}"
                   IsEnabled="{Binding !IsLoading}"/>
        </StackPanel>
    </Grid>
</Border>
```

## Progress Indicators and Loading States

### FluentAvalonia ProgressRing
```xml
<ui:ProgressRing Width="20" Height="20"
                IsActive="{Binding ShowProgressIndicator}"
                IsVisible="{Binding ShowProgressIndicator}"/>
```

### Loading Overlay Pattern
```xml
<Border Grid.RowSpan="2"
       Background="rgba(255,255,255,0.8)"
       IsVisible="{Binding IsRefreshing}">
    <StackPanel HorizontalAlignment="Center" 
               VerticalAlignment="Center"
               Spacing="10">
        <ui:ProgressRing Width="40" Height="40" IsActive="True"/>
        <TextBlock Text="Refreshing provider status..." 
                  HorizontalAlignment="Center"/>
    </StackPanel>
</Border>
```

## Real-time Data Binding Patterns

### Event Subscription Pattern
```csharp
public ProviderStatusDashboardViewModel(IProviderStatusService providerStatusService)
{
    _providerStatusService = providerStatusService;
    
    // Subscribe to status changes
    _providerStatusService.ProviderStatusChanged += OnProviderStatusChanged;
}

private async void OnProviderStatusChanged(object sender, ProviderStatusChangedEventArgs e)
{
    var provider = Providers.FirstOrDefault(p => p.ProviderName == e.ProviderName);
    if (provider != null)
    {
        provider.UpdateFromProviderStatus(e.Status);
    }
}
```

## Value Converters for Status Display

### Essential Converters
```csharp
public class BoolToHealthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? "Healthy" : "Unhealthy";
    }
}

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? "#4CAF50" : "#F44336";
    }
}
```

## Grid Layout Pattern

### Responsive Provider Grid
```xml
<ItemsControl ItemsSource="{Binding Providers}" Margin="20">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <UniformGrid Columns="2" />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    
    <ItemsControl.ItemTemplate>
        <DataTemplate DataType="vm:ProviderStatusCardViewModel">
            <controls:ProviderStatusCard/>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

## Button State Management

### Conditional Button Styling
```xml
<Button Content="{Binding ButtonText}"
       Command="{Binding SetupProviderCommand}"
       IsEnabled="{Binding !IsLoading}"
       Classes="accent">
    <Button.Styles>
        <Style Selector="Button.accent">
            <Setter Property="Background" Value="{Binding StatusColor}"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="CornerRadius" Value="6"/>
            <Setter Property="Padding" Value="16,8"/>
        </Style>
        <Style Selector="Button:disabled">
            <Setter Property="Opacity" Value="0.6"/>
        </Style>
    </Button.Styles>
</Button>
```

## Service Integration Pattern

### Constructor Injection
```csharp
public ProviderStatusDashboardViewModel(
    IProviderStatusService providerStatusService,
    IServiceProvider serviceProvider)
{
    _providerStatusService = providerStatusService;
    _serviceProvider = serviceProvider;
}
```

### Provider Card Factory Pattern
```csharp
private async Task InitializeProviders()
{
    var providerStatuses = await _providerStatusService.GetAllProviderStatusAsync();
    
    Providers.Clear();
    foreach (var (name, status) in providerStatuses)
    {
        var cardViewModel = _serviceProvider.GetService<ProviderStatusCardViewModel>();
        cardViewModel.UpdateFromProviderStatus(status);
        cardViewModel.DisplayName = GetDisplayName(name);
        Providers.Add(cardViewModel);
    }
}
```

## Critical Implementation Notes

1. **ObservableCollection**: Always use for dynamic provider lists to enable automatic UI updates
2. **Computed Properties**: Use OnPropertyChanged for derived properties that depend on observable properties
3. **Command Execution**: Always wrap async operations in try/finally blocks with IsLoading state management
4. **Memory Management**: Unsubscribe from events in ViewModel disposal
5. **Error Handling**: Use the existing Result<T> pattern for consistent error handling
6. **Thread Safety**: UI updates from service events must be dispatched to UI thread if needed

## Existing TrashMail Panda Patterns to Follow

- Use `ViewModelBase` as the base class for all ViewModels
- Follow the `IsLoading` boolean pattern for progress states
- Use `StatusMessage` string pattern for user feedback
- Implement cleanup in ViewModel disposal methods
- Follow existing Border/StackPanel card layout patterns from `WelcomeWizardWindow`