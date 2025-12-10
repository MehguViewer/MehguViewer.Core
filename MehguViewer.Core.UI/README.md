# MehguViewer.Core.UI

> **The Blazor WebAssembly Admin Interface for MehguViewer.Core**

This project provides the integrated web-based administration dashboard for MehguViewer.Core. Built with Blazor WebAssembly and MudBlazor, it offers a modern, responsive interface for managing content, users, and system settings.

---

## 🚀 Overview

MehguViewer.Core.UI is a single-page application (SPA) that runs entirely in the browser, communicating with the Core API through a unified service layer. It provides comprehensive management capabilities while maintaining the security and separation of concerns defined in the MehguViewer architecture.

### Key Technologies

- **Framework**: Blazor WebAssembly (.NET 9)
- **UI Components**: MudBlazor 7.x
- **Authentication**: JWT-based with custom AuthStateProvider
- **HTTP Client**: Scoped HttpClient with ApiService wrapper
- **State Management**: Component-level state with service injection

---

## 📁 Project Structure

```
MehguViewer.Core.UI/
├── Pages/                      # Routable pages (main UI views)
│   ├── Home.razor             # Dashboard home page
│   ├── Login.razor            # Authentication page
│   ├── SetupWizard.razor      # Initial setup flow
│   ├── SeriesPage.razor       # Series management
│   ├── SeriesDetailsPage.razor # Series details and unit management
│   ├── IngestPage.razor       # Content ingestion
│   ├── AuthorsPage.razor      # Author taxonomy management
│   ├── ScanlatorsPage.razor   # Scanlator taxonomy management
│   ├── GroupsPage.razor       # Group taxonomy management
│   ├── TagsPage.razor         # Tag taxonomy management
│   ├── UsersPage.razor        # User management (Admin only)
│   ├── JobsPage.razor         # Background job monitoring
│   ├── LogsPage.razor         # System logs viewer
│   ├── SettingsPage.razor     # System settings
│   └── ProfilePage.razor      # User profile and preferences
│
├── Components/                 # Reusable UI components
│   ├── CreateSeriesDialog.razor        # Series creation modal
│   ├── UnitDialog.razor                # Unit creation/edit modal
│   ├── CreateUserDialog.razor          # User creation modal
│   ├── EditUserDialog.razor            # User editing modal
│   ├── ResetPasswordDialog.razor       # Password reset modal
│   ├── TransferOwnershipDialog.razor   # Ownership transfer modal
│   ├── ReportDialog.razor              # Content reporting
│   ├── CommentSection.razor            # Comment thread display
│   ├── PasswordStrengthValidator.razor # Password validation UI
│   ├── DashboardSettingsPanel.razor    # Dashboard customization
│   ├── GeneralSettings.razor           # General settings panel
│   ├── SecuritySettings.razor          # Security configuration
│   ├── StorageSettings.razor           # Storage settings
│   ├── FederationSettings.razor        # Federation configuration
│   ├── TaxonomySettings.razor          # Taxonomy management
│   ├── AdvancedSettings.razor          # Advanced options
│   └── RedirectToLogin.razor           # Auth redirect component
│
├── Layout/                     # Application layout components
│   ├── MainLayout.razor       # Primary application layout
│   └── EmptyLayout.razor      # Minimal layout for login/setup
│
├── Services/                   # Client-side services
│   ├── ApiService.cs          # Unified API client wrapper
│   ├── AuthStateProvider.cs   # JWT authentication state management
│   ├── DashboardSettingsService.cs # Dashboard preferences
│   └── CryptoHelper.cs        # Client-side cryptographic utilities
│
├── wwwroot/                    # Static assets
│   ├── css/                   # Stylesheets
│   ├── js/                    # JavaScript interop files
│   └── index.html             # Application entry point
│
├── App.razor                   # Root component with routing
├── _Imports.razor             # Global using directives
└── Program.cs                 # Application startup and DI configuration
```

---

## 🔑 Key Features

### Authentication & Authorization
- **JWT-based authentication** with automatic token refresh
- **Role-based access control** (Admin, Uploader, User)
- **Custom AuthStateProvider** for seamless authentication state
- **Automatic redirect** to login for unauthorized access

### Content Management
- **Series CRUD operations** with rich metadata editing
- **Unit management** with file upload and metadata
- **Taxonomy management** (Authors, Scanlators, Groups, Tags)
- **Content ingestion** from local and remote sources
- **Image processing** with automatic variant generation

### User Management (Admin Only)
- **User creation and editing** with role assignment
- **Password management** with strength validation
- **Ownership transfer** for series and content
- **User deactivation and deletion**

### System Administration
- **Dashboard customization** with persistent preferences
- **System settings** for node configuration
- **Background job monitoring** with status tracking
- **System logs** with filtering and search
- **Federation settings** for multi-node deployments

### User Experience
- **Responsive design** for mobile, tablet, and desktop
- **Dark/light theme** support via MudBlazor
- **Fast loading** with Blazor WebAssembly optimizations
- **Real-time feedback** with snackbar notifications
- **Intuitive navigation** with breadcrumbs and side menu

---

## 🛠️ Development

### Running the UI in Development

The UI is automatically hosted by the main MehguViewer.Core application:

```bash
cd MehguViewer.Core
dotnet watch run
```

The UI will be available at `http://localhost:6230`

### Standalone Development (Optional)

For UI-only development with hot reload:

```bash
cd MehguViewer.Core.UI
dotnet watch run
```

This requires the Core API to be running separately.

### Building for Production

The UI is compiled and included in the Core application:

```bash
cd MehguViewer.Core
dotnet publish -c Release
```

---

## 🧩 Architecture & Integration

### Service Layer

The UI communicates with the Core API exclusively through the `ApiService`:

```csharp
@inject ApiService Api
@inject ISnackbar Snackbar

private async Task LoadSeriesAsync()
{
    try
    {
        var response = await Api.GetAsync<SeriesListResponse>("/api/v1/series");
        seriesList = response.series;
    }
    catch (ApiException ex)
    {
        Snackbar.Add(ex.Message, Severity.Error);
    }
}
```

### Authentication Flow

1. User submits credentials via `Login.razor`
2. `ApiService` calls `/api/v1/auth/login`
3. JWT token stored in `localStorage`
4. `JwtAuthStateProvider` notifies components of auth state change
5. Subsequent requests include `Authorization: Bearer {token}` header

### Component Patterns

All UI components follow consistent patterns:

- **Scoped services** via `@inject`
- **Loading states** with `isLoading` flag
- **Error handling** with try/catch and snackbar notifications
- **Form validation** using MudBlazor form components
- **Dialog management** via MudBlazor DialogService

---

## 🎨 UI/UX Guidelines

### Design Principles
- **Straightforward**: Minimal clicks to accomplish tasks
- **Intuitive**: Clear visual hierarchy and action paths
- **Responsive**: Mobile-first design with tablet/desktop optimizations
- **Consistent**: Reuse components and maintain visual language
- **Accessible**: Keyboard navigation, ARIA labels, screen reader support

### Component Structure
```razor
@inject ApiService Api
@inject ISnackbar Snackbar

<MudContainer MaxWidth="MaxWidth.Large">
    @if (isLoading)
    {
        <MudProgressCircular Indeterminate="true" />
    }
    else
    {
        <!-- Content -->
    }
</MudContainer>

@code {
    private bool isLoading = true;

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            isLoading = true;
            // API calls
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
        }
    }
}
```

### Styling
- **CSS Isolation**: Each component has its own `.razor.css` file
- **CSS Variables**: Defined in `app.css` for consistency
- **MudBlazor Theming**: Leverage built-in theme system
- **Responsive Breakpoints**: Mobile (<768px), Tablet (768-1024px), Desktop (>1024px)

---

## 🔧 Configuration

### Service Registration (Program.cs)

```csharp
// MudBlazor with custom snackbar settings
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.VisibleStateDuration = 2000;
});

// Authentication
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<JwtAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => 
    sp.GetRequiredService<JwtAuthStateProvider>());

// Application Services
builder.Services.AddScoped<ApiService>();
builder.Services.AddScoped<DashboardSettingsService>();
```

### API Service Configuration

The `ApiService` automatically:
- Adds JWT token to requests
- Handles HTTP errors with RFC 7807 Problem Details
- Provides typed response deserialization
- Manages URN-based routing

---

## 📊 State Management

### Authentication State
- Managed by `JwtAuthStateProvider`
- Token stored in browser `localStorage`
- Automatic expiration handling
- Components subscribe via `AuthorizeView` or `CascadingAuthenticationState`

### Dashboard Preferences
- Managed by `DashboardSettingsService`
- Persisted to `localStorage`
- Panel visibility toggles
- Custom dashboard layouts

### Component State
- Local state in `@code` blocks
- No global state management (intentionally simple)
- Parent-child communication via `EventCallback`

---

## 🧪 Testing

UI tests are integrated with the main test suite:

```bash
cd MehguViewer.Core
dotnet test --filter "Category=UI"
```

### Test Categories
- **Component Rendering**: Verify component output
- **User Interactions**: Button clicks, form submissions
- **API Integration**: Mock API responses
- **Authentication Flow**: Login/logout scenarios

---

## 🚀 Performance Optimizations

- **Lazy Loading**: Routes loaded on-demand
- **Component Virtualization**: Large lists use `MudVirtualize`
- **Minimal Re-renders**: Use `ShouldRender()` where appropriate
- **Image Optimization**: Leverage Core API's image variants
- **Caching**: Dashboard settings cached locally

---

## 📦 Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.AspNetCore.Components.WebAssembly` | 9.x | Blazor WebAssembly runtime |
| `MudBlazor` | 7.x | Material Design components |
| `System.Net.Http.Json` | 9.x | JSON serialization for HttpClient |

See `MehguViewer.Core.UI.csproj` for complete dependency list.

---

## 🤝 Contributing

When adding new UI features:

1. **Follow existing patterns** in similar pages/components
2. **Use MudBlazor components** for consistency
3. **Add CSS isolation** (`.razor.css` files)
4. **Implement error handling** with snackbar notifications
5. **Test responsiveness** on mobile/tablet/desktop
6. **Update this README** with new pages/components

---

## 📄 License

This project is part of MehguViewer.Core and shares the same license. See the root [LICENSE](../LICENSE) file.

---

<div align="center">
  <sub>Built with Blazor WebAssembly and MudBlazor</sub>
  <br>
  <sub>MehguViewer.Core.UI &copy; 2025</sub>
</div>
