using System.Text.Json.Serialization;
using MehguViewer.Core.Extensions;
using MehguViewer.Core.Shared;

var builder = WebApplication.CreateBuilder(args);

// Configure default URLs if not specified
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:6230");
}

// Add Services
builder.Services.AddMehguServices(builder.Configuration);
builder.Services.AddMehguSecurity();
builder.Services.AddMehguInfrastructure();

var app = builder.Build();

// Configure Middleware
app.UseMehguMiddleware(app.Environment);

// Map Endpoints
app.MapMehguEndpoints();

app.Run();

[JsonSerializable(typeof(NodeManifest))]
[JsonSerializable(typeof(NodeMetadata))]
[JsonSerializable(typeof(TaxonomyData))]
[JsonSerializable(typeof(TaxonomyConfig))]
[JsonSerializable(typeof(TaxonomyConfigUpdate))]
[JsonSerializable(typeof(SearchResults))]
[JsonSerializable(typeof(Series))]
[JsonSerializable(typeof(SeriesCreate))]
[JsonSerializable(typeof(SeriesListResponse))]
[JsonSerializable(typeof(Unit))]
[JsonSerializable(typeof(UnitCreate))]
[JsonSerializable(typeof(UnitListResponse))]
[JsonSerializable(typeof(Page))]
[JsonSerializable(typeof(IEnumerable<Page>))]
[JsonSerializable(typeof(List<Page>))]
[JsonSerializable(typeof(JobResponse))]
[JsonSerializable(typeof(JobListResponse))]
[JsonSerializable(typeof(Job))]
[JsonSerializable(typeof(Job[]))]
[JsonSerializable(typeof(ProgressUpdate))]
[JsonSerializable(typeof(ReadingProgress))]
[JsonSerializable(typeof(HistoryListResponse))]
[JsonSerializable(typeof(HistoryBatchImport))]
[JsonSerializable(typeof(ReadingProgress[]))]
[JsonSerializable(typeof(CommentListResponse))]
[JsonSerializable(typeof(CommentCreate))]
[JsonSerializable(typeof(Comment))]
[JsonSerializable(typeof(Vote))]
[JsonSerializable(typeof(Collection[]))]
[JsonSerializable(typeof(Collection))]
[JsonSerializable(typeof(CollectionCreate))]
[JsonSerializable(typeof(CollectionUpdate))]
[JsonSerializable(typeof(CollectionItemAdd))]
[JsonSerializable(typeof(SystemConfig))]
[JsonSerializable(typeof(SystemConfigUpdate))]
[JsonSerializable(typeof(SystemStats))]
[JsonSerializable(typeof(StorageStatsResponse))]
[JsonSerializable(typeof(StorageSettingsUpdate))]
[JsonSerializable(typeof(Report))]
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(User[]))]
[JsonSerializable(typeof(UserCreate))]
[JsonSerializable(typeof(UserUpdate))]
[JsonSerializable(typeof(SeriesUpdate))]
[JsonSerializable(typeof(UnitUpdate))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(Problem))]
[JsonSerializable(typeof(AdminPasswordRequest))]
[JsonSerializable(typeof(DatabaseConfig))]
[JsonSerializable(typeof(DatabaseSetupRequest))]
[JsonSerializable(typeof(DatabaseTestResponse))]
[JsonSerializable(typeof(EmbeddedDatabaseStatus))]
[JsonSerializable(typeof(UseEmbeddedDatabaseRequest))]
[JsonSerializable(typeof(UseEmbeddedDatabaseResponse))]
[JsonSerializable(typeof(SetupStatusResponse))]
[JsonSerializable(typeof(DebugResponse))]
[JsonSerializable(typeof(ResetRequest))]
[JsonSerializable(typeof(ResetResponse))]
[JsonSerializable(typeof(ClearCacheResponse))]
[JsonSerializable(typeof(IEnumerable<User>))]
[JsonSerializable(typeof(Group))]
[JsonSerializable(typeof(Group[]))]
[JsonSerializable(typeof(LocalizedMetadata))]
[JsonSerializable(typeof(Dictionary<string, LocalizedMetadata>))]
[JsonSerializable(typeof(AuthConfig))]
[JsonSerializable(typeof(CloudflareConfig))]
[JsonSerializable(typeof(AuthConfigUpdate))]
[JsonSerializable(typeof(CloudflareConfigUpdate))]
[JsonSerializable(typeof(LoginRequestWithCf))]
[JsonSerializable(typeof(RegisterRequestWithCf))]
[JsonSerializable(typeof(AuthConfigPublic))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(UserProfileResponse))]
[JsonSerializable(typeof(Passkey))]
[JsonSerializable(typeof(Passkey[]))]
[JsonSerializable(typeof(PasskeyInfo))]
[JsonSerializable(typeof(PasskeyInfo[]))]
[JsonSerializable(typeof(PasskeyRegistrationOptionsRequest))]
[JsonSerializable(typeof(PasskeyRegistrationOptions))]
[JsonSerializable(typeof(PasskeyRpEntity))]
[JsonSerializable(typeof(PasskeyUserEntity))]
[JsonSerializable(typeof(PasskeyPubKeyCredParam))]
[JsonSerializable(typeof(PasskeyPubKeyCredParam[]))]
[JsonSerializable(typeof(PasskeyRegistrationRequest))]
[JsonSerializable(typeof(PasskeyAuthenticatorAttestationResponse))]
[JsonSerializable(typeof(PasskeyAuthenticationOptionsRequest))]
[JsonSerializable(typeof(PasskeyAuthenticationOptions))]
[JsonSerializable(typeof(PasskeyAllowCredential))]
[JsonSerializable(typeof(PasskeyAllowCredential[]))]
[JsonSerializable(typeof(PasskeyAuthenticationRequest))]
[JsonSerializable(typeof(PasskeyAuthenticatorAssertionResponse))]
[JsonSerializable(typeof(PasskeyRenameRequest))]
[JsonSerializable(typeof(TogglePasswordLoginRequest))]
[JsonSerializable(typeof(TogglePasswordLoginResponse))]
[JsonSerializable(typeof(ResetRequest))]
[JsonSerializable(typeof(PasskeyVerificationData))]
[JsonSerializable(typeof(PasskeyAssertionResponseData))]
[JsonSerializable(typeof(LogEntry))]
[JsonSerializable(typeof(LogEntry[]))]
[JsonSerializable(typeof(LogsResponse))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}

public partial class Program { }
