using MehguViewer.Shared.Models;

namespace MehguViewer.Core.Backend.Services;

public interface IRepository
{
    void SeedDebugData();

    // Series
    void AddSeries(Series series);
    void UpdateSeries(Series series);
    Series? GetSeries(string id);
    IEnumerable<Series> ListSeries();
    IEnumerable<Series> SearchSeries(string? query, string? type, string[]? genres, string? status);
    void DeleteSeries(string id);

    // Units
    void AddUnit(Unit unit);
    void UpdateUnit(Unit unit);
    IEnumerable<Unit> ListUnits(string seriesId);
    Unit? GetUnit(string id);
    void DeleteUnit(string id);

    // Pages
    void AddPage(string unitId, Page page);
    IEnumerable<Page> GetPages(string unitId);

    // Progress
    void UpdateProgress(string userId, ReadingProgress progress);
    ReadingProgress? GetProgress(string userId, string seriesUrn);
    IEnumerable<ReadingProgress> GetLibrary(string userId);
    IEnumerable<ReadingProgress> GetHistory(string userId);

    // Comments
    void AddComment(Comment comment);
    IEnumerable<Comment> GetComments(string targetUrn);

    // Votes
    void AddVote(string userId, Vote vote);

    // Collections
    void AddCollection(string userId, Collection collection);
    IEnumerable<Collection> ListCollections(string userId);
    Collection? GetCollection(string id);
    void UpdateCollection(Collection collection);
    void DeleteCollection(string id);

    // Reports
    void AddReport(Report report);

    // System Config
    SystemConfig GetSystemConfig();
    void UpdateSystemConfig(SystemConfig config);

    // System Stats
    SystemStats GetSystemStats();

    // User Management
    void AddUser(User user);
    void UpdateUser(User user);
    User? GetUser(string id);
    User? GetUserByUsername(string username);
    IEnumerable<User> ListUsers();
    void DeleteUser(string id);
    void DeleteUserHistory(string userId);
    void AnonymizeUserContent(string userId);
    User? ValidateUser(string username, string password);
    bool IsAdminSet();

    // Passkey / WebAuthn
    void AddPasskey(Passkey passkey);
    void UpdatePasskey(Passkey passkey);
    IEnumerable<Passkey> GetPasskeysByUser(string userId);
    Passkey? GetPasskeyByCredentialId(string credentialId);
    Passkey? GetPasskey(string id);
    void DeletePasskey(string id);

    // Node Metadata
    NodeMetadata GetNodeMetadata();
    void UpdateNodeMetadata(NodeMetadata metadata);

    // Reset Operations
    void ResetAllData();
}
