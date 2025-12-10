using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;

namespace MehguViewer.Core.Infrastructures;

/// <summary>
/// Repository interface for MehguViewer data persistence operations.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong> Provides abstraction layer over different storage backends,
/// enabling flexible data persistence strategies while maintaining consistent API contracts.</para>
/// 
/// <para><strong>Supported Implementations:</strong></para>
/// <list type="bullet">
///   <item><description><c>PostgresRepository</c>: Production-grade persistent storage using Npgsql with full ACID compliance</description></item>
///   <item><description><c>MemoryRepository</c>: In-memory storage for testing and development (non-persistent)</description></item>
///   <item><description><c>DynamicRepository</c>: Runtime-switchable wrapper enabling backend transitions without service restarts</description></item>
/// </list>
/// 
/// <para><strong>Core Capabilities:</strong></para>
/// <list type="bullet">
///   <item><description>Content Management: Series/Unit CRUD with hierarchical relationships</description></item>
///   <item><description>Access Control: URN-based permission management with owner/editor distinction</description></item>
///   <item><description>User Tracking: Reading progress, history, and library organization</description></item>
///   <item><description>Taxonomy: Configurable tags, authors, and scanlator registries</description></item>
///   <item><description>Authentication: WebAuthn passkey storage for passwordless auth</description></item>
///   <item><description>Social: Comments, ratings, and content reporting</description></item>
/// </list>
/// 
/// <para><strong>Design Principles:</strong></para>
/// <list type="bullet">
///   <item><description><strong>URN Format:</strong> All public IDs use urn:mvn:{type}:{uuid} format</description></item>
///   <item><description><strong>ID Flexibility:</strong> Methods accept both URN and raw UUID formats</description></item>
///   <item><description><strong>Logging:</strong> Implementations must log at appropriate levels (Debug/Info/Warning/Error)</description></item>
///   <item><description><strong>Return Semantics:</strong> Null = not found, Exception = operation failed</description></item>
///   <item><description><strong>Lazy Loading:</strong> IEnumerable results may be lazy; materialize when counting/indexing</description></item>
///   <item><description><strong>Security:</strong> Validate URN format, sanitize inputs, prevent injection attacks</description></item>
///   <item><description><strong>Atomicity:</strong> Write operations should be atomic where possible</description></item>
/// </list>
/// 
/// <para><strong>Implementation Guidelines:</strong></para>
/// <list type="bullet">
///   <item><description>Log Debug: Query execution details, cache hits/misses</description></item>
///   <item><description>Log Info: CRUD operations (created/updated/deleted entities)</description></item>
///   <item><description>Log Warning: Invalid URN format, not found, permission denied</description></item>
///   <item><description>Log Error: Database failures, constraint violations, unexpected exceptions</description></item>
///   <item><description>Validate all URNs using UrnHelper before processing</description></item>
///   <item><description>Use parameterized queries to prevent SQL injection</description></item>
///   <item><description>Implement proper transaction handling for multi-step operations</description></item>
/// </list>
/// </remarks>
public interface IRepository
{
    #region System Operations
    
    /// <summary>
    /// Seeds the repository with debug/sample data for development and testing.
    /// </summary>
    /// <remarks>
    /// <para><strong>Security:</strong> Should only be available in development/staging environments.</para>
    /// <para><strong>Logging:</strong> Log Info with count of entities created.</para>
    /// <para><strong>Side Effects:</strong> Creates sample users, series, units, and social data.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if called in production environment.</exception>
    void SeedDebugData();
    
    /// <summary>
    /// Resets all data in the repository (destructive operation).
    /// </summary>
    /// <remarks>
    /// <para><strong>Security:</strong> Requires admin privileges. Should only be available in dev/test environments.</para>
    /// <para><strong>Logging:</strong> Log Warning before deletion, log Info after completion.</para>
    /// <para><strong>Use Case:</strong> Test cleanup, development reset, NOT for production use.</para>
    /// <para><strong>Warning:</strong> Permanently deletes all series, users, collections, comments, progress, and permissions.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if called in production environment.</exception>
    void ResetAllData();
    
    #endregion

    #region Series Operations
    
    /// <summary>
    /// Adds a new series to the repository.
    /// </summary>
    /// <param name="series">Series to add with valid URN and required fields.</param>
    /// <remarks>
    /// <para><strong>Validation:</strong> Ensure series.urn follows urn:mvn:series:{uuid} format.</para>
    /// <para><strong>Logging:</strong> Log Info with series URN and title.</para>
    /// <para><strong>Security:</strong> Caller should have appropriate permissions (checked at endpoint level).</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if series is null.</exception>
    /// <exception cref="ArgumentException">Thrown if URN format is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown if series with same URN already exists.</exception>
    void AddSeries(Series series);
    
    /// <summary>
    /// Updates an existing series in the repository.
    /// </summary>
    /// <param name="series">Series with updated data. URN must match existing series.</param>
    /// <remarks>
    /// <para><strong>Validation:</strong> Verify series exists before updating.</para>
    /// <para><strong>Logging:</strong> Log Info with series URN and modified fields.</para>
    /// <para><strong>Security:</strong> Caller must have edit permission (checked at endpoint level).</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if series is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if series does not exist.</exception>
    void UpdateSeries(Series series);
    
    /// <summary>
    /// Retrieves a series by its URN or UUID.
    /// </summary>
    /// <param name="id">Series URN (urn:mvn:series:{uuid}) or raw UUID string.</param>
    /// <returns>Series if found, null otherwise.</returns>
    /// <remarks>
    /// <para><strong>Validation:</strong> Use UrnHelper to parse and validate ID.</para>
    /// <para><strong>Logging:</strong> Log Debug with query details, log Warning if not found.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown if ID format is invalid.</exception>
    Series? GetSeries(string id);
    
    /// <summary>
    /// Lists series with pagination support.
    /// </summary>
    /// <param name="offset">Number of series to skip (default: 0). Must be >= 0.</param>
    /// <param name="limit">Maximum number of series to return (default: 20). Range: 1-100.</param>
    /// <returns>Enumerable of series ordered by creation date (newest first).</returns>
    /// <remarks>
    /// <para><strong>Performance:</strong> Result may be lazy-loaded. Materialize for counting.</para>
    /// <para><strong>Logging:</strong> Log Debug with offset, limit, and result count.</para>
    /// <para><strong>Validation:</strong> Clamp limit to 1-100 range to prevent abuse.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if offset < 0.</exception>
    IEnumerable<Series> ListSeries(int offset = 0, int limit = 20);
    
    /// <summary>
    /// Searches series with multiple filters and pagination.
    /// </summary>
    /// <param name="query">Text search across title, description, and alt_titles. Null for no text filter.</param>
    /// <param name="type">Filter by media type (e.g., "manga", "novel", "comic"). Null for all types.</param>
    /// <param name="tags">Filter by tags (series must have at least one matching tag). Null/empty for no tag filter.</param>
    /// <param name="status">Filter by status (e.g., "ongoing", "completed", "hiatus"). Null for all statuses.</param>
    /// <param name="offset">Number of series to skip (default: 0). Must be >= 0.</param>
    /// <param name="limit">Maximum number to return (default: 20). Range: 1-100.</param>
    /// <returns>Filtered enumerable of series ordered by relevance (if query) or creation date.</returns>
    /// <remarks>
    /// <para><strong>Performance:</strong> Use indexes on title, type, status. Full-text search for query.</para>
    /// <para><strong>Logging:</strong> Log Debug with filter params and result count.</para>
    /// <para><strong>Security:</strong> Sanitize query to prevent injection (use parameterized queries).</para>
    /// <para><strong>Validation:</strong> Clamp limit to 1-100, validate status/type against allowed values.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if offset < 0.</exception>
    IEnumerable<Series> SearchSeries(string? query, string? type, string[]? tags, string? status, int offset = 0, int limit = 20);
    
    /// <summary>
    /// Deletes a series and all associated units, pages, and permissions (cascade delete).
    /// </summary>
    /// <param name="id">Series URN (urn:mvn:series:{uuid}) or raw UUID.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Caller must be series owner or admin (checked at endpoint level).</para>
    /// <para><strong>Logging:</strong> Log Warning with series URN before deletion, log Info after completion.</para>
    /// <para><strong>Side Effects:</strong> Deletes all units, pages, edit permissions, user progress for this series.</para>
    /// <para><strong>Atomicity:</strong> Should be transactional - all or nothing.</para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">Thrown if series does not exist.</exception>
    void DeleteSeries(string id);
    
    #endregion

    #region Unit Operations
    
    /// <summary>
    /// Adds a new unit (chapter/episode) to a series.
    /// </summary>
    /// <param name="unit">Unit to add with valid URN and series_urn reference.</param>
    /// <remarks>
    /// <para><strong>Validation:</strong> Verify series exists, unit.urn format is urn:mvn:unit:{uuid}.</para>
    /// <para><strong>Logging:</strong> Log Info with unit URN, series URN, and unit number.</para>
    /// <para><strong>Side Effects:</strong> Triggers AggregateSeriesMetadataFromUnits for parent series.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if unit is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if parent series does not exist.</exception>
    void AddUnit(Unit unit);
    
    /// <summary>
    /// Updates an existing unit.
    /// </summary>
    /// <param name="unit">Unit with updated data. URN must match existing unit.</param>
    /// <remarks>
    /// <para><strong>Validation:</strong> Verify unit exists, series_urn cannot be changed.</para>
    /// <para><strong>Logging:</strong> Log Info with unit URN and modified fields.</para>
    /// <para><strong>Security:</strong> Caller must have edit permission for unit or series.</para>
    /// <para><strong>Side Effects:</strong> May trigger AggregateSeriesMetadataFromUnits if metadata changed.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if unit is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if unit does not exist.</exception>
    void UpdateUnit(Unit unit);
    
    /// <summary>
    /// Lists all units for a specific series.
    /// </summary>
    /// <param name="seriesId">Series URN (urn:mvn:series:{uuid}) or raw UUID.</param>
    /// <returns>Enumerable of units ordered by unit number (ascending).</returns>
    /// <remarks>
    /// <para><strong>Performance:</strong> Result may be lazy-loaded. Use index on series_id.</para>
    /// <para><strong>Logging:</strong> Log Debug with series ID and result count.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown if seriesId format is invalid.</exception>
    IEnumerable<Unit> ListUnits(string seriesId);
    
    /// <summary>
    /// Retrieves a unit by its URN or UUID.
    /// </summary>
    /// <param name="id">Unit URN (urn:mvn:unit:{uuid}) or raw UUID.</param>
    /// <returns>Unit if found, null otherwise.</returns>
    /// <remarks>
    /// <para><strong>Validation:</strong> Use UrnHelper to parse and validate ID.</para>
    /// <para><strong>Logging:</strong> Log Debug with query details, log Warning if not found.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown if ID format is invalid.</exception>
    Unit? GetUnit(string id);
    
    /// <summary>
    /// Deletes a unit and all associated pages and permissions (cascade delete).
    /// </summary>
    /// <param name="id">Unit URN (urn:mvn:unit:{uuid}) or raw UUID.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Caller must have edit permission for unit or parent series.</para>
    /// <para><strong>Logging:</strong> Log Warning with unit URN before deletion, log Info after completion.</para>
    /// <para><strong>Side Effects:</strong> Deletes all pages, edit permissions, triggers metadata aggregation for parent series.</para>
    /// <para><strong>Atomicity:</strong> Should be transactional.</para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">Thrown if unit does not exist.</exception>
    void DeleteUnit(string id);
    
    #endregion
    
    #region Metadata Operations
    
    /// <summary>
    /// Aggregates metadata from all units of a series and updates the series record.
    /// </summary>
    /// <param name="seriesId">Series URN (urn:mvn:series:{uuid}) or raw UUID.</param>
    /// <remarks>
    /// <para><strong>Aggregated Fields:</strong> tags, scanlators, authors, content_warnings, languages.</para>
    /// <para><strong>Trigger Points:</strong> Called automatically after AddUnit, UpdateUnit, DeleteUnit.</para>
    /// <para><strong>Logic:</strong> Collects unique values from all units, sorts alphabetically, updates series.</para>
    /// <para><strong>Logging:</strong> Log Debug with seriesId and aggregated counts.</para>
    /// <para><strong>Performance:</strong> Use efficient set operations, consider caching for large series.</para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">Thrown if series does not exist.</exception>
    void AggregateSeriesMetadataFromUnits(string seriesId);
    
    #endregion

    #region Edit Permission Operations
    
    /// <summary>
    /// Grants edit permission to a user for a series or unit.
    /// </summary>
    /// <param name="targetUrn">URN of series (urn:mvn:series:{uuid}) or unit (urn:mvn:unit:{uuid}).</param>
    /// <param name="userUrn">URN of user (urn:mvn:user:{uuid}) receiving permission.</param>
    /// <param name="grantedBy">URN of user granting permission (must be owner or admin).</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Verify grantedBy has permission to grant (owner or admin).</para>
    /// <para><strong>Validation:</strong> All URNs must be valid, target and user must exist.</para>
    /// <para><strong>Logging:</strong> Log Info with targetUrn, userUrn, and grantedBy.</para>
    /// <para><strong>Idempotency:</strong> Granting existing permission should not fail.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown if URN format is invalid.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if target or user does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if grantedBy lacks permission.</exception>
    void GrantEditPermission(string targetUrn, string userUrn, string grantedBy);
    
    /// <summary>
    /// Revokes edit permission from a user for a series or unit.
    /// </summary>
    /// <param name="targetUrn">URN of series or unit.</param>
    /// <param name="userUrn">URN of user losing permission.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Cannot revoke owner's implicit permission.</para>
    /// <para><strong>Logging:</strong> Log Info with targetUrn and userUrn.</para>
    /// <para><strong>Idempotency:</strong> Revoking non-existent permission should not fail.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown if URN format is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown if attempting to revoke owner permission.</exception>
    void RevokeEditPermission(string targetUrn, string userUrn);
    
    /// <summary>
    /// Checks if a user has edit permission for a series or unit.
    /// </summary>
    /// <param name="targetUrn">URN of series or unit to check.</param>
    /// <param name="userUrn">URN of user to check.</param>
    /// <returns>True if user is owner or has explicit permission, false otherwise.</returns>
    /// <remarks>
    /// <para><strong>Priority:</strong> Owner always has permission regardless of explicit grants.</para>
    /// <para><strong>Inheritance:</strong> Series permission does NOT automatically grant unit permission.</para>
    /// <para><strong>Performance:</strong> Should be fast, consider caching for hot paths.</para>
    /// <para><strong>Logging:</strong> Log Debug with targetUrn, userUrn, and result.</para>
    /// </remarks>
    bool HasEditPermission(string targetUrn, string userUrn);
    
    /// <summary>
    /// Gets all user URNs who have edit permission for a series or unit.
    /// </summary>
    /// <param name="targetUrn">URN of series or unit.</param>
    /// <returns>Array of user URNs with edit permission (includes owner + explicit grants).</returns>
    /// <remarks>
    /// <para><strong>Includes:</strong> Owner URN + all explicitly granted user URNs.</para>
    /// <para><strong>Logging:</strong> Log Debug with targetUrn and result count.</para>
    /// </remarks>
    string[] GetEditPermissions(string targetUrn);

    /// <summary>
    /// Gets detailed edit permission records with metadata (granted_by, granted_at).
    /// </summary>
    /// <param name="targetUrn">URN of series or unit.</param>
    /// <returns>Array of edit permission records with grant metadata.</returns>
    /// <remarks>
    /// <para><strong>Use Case:</strong> Admin UI showing permission audit trail.</para>
    /// <para><strong>Logging:</strong> Log Debug with targetUrn and record count.</para>
    /// <para><strong>Excludes:</strong> Owner's implicit permission (not stored as record).</para>
    /// </remarks>
    EditPermission[] GetEditPermissionRecords(string targetUrn);

    /// <summary>
    /// Synchronizes edit permissions with file system state.
    /// Removes orphaned permissions for deleted series/units.
    /// </summary>
    /// <remarks>
    /// <para><strong>Purpose:</strong> Cleanup after file-based operations that bypass repository.</para>
    /// <para><strong>Trigger:</strong> Called after library initialization/scan.</para>
    /// <para><strong>Logic:</strong> Queries all permissions, verifies target exists, deletes orphans.</para>
    /// <para><strong>Logging:</strong> Log Info with count of orphaned permissions removed.</para>
    /// </remarks>
    void SyncEditPermissions();
    
    #endregion

    #region Page Operations
    
    /// <summary>
    /// Adds a page to a unit.
    /// </summary>
    /// <param name="unitId">Unit URN (urn:mvn:unit:{uuid}) or raw UUID.</param>
    /// <param name="page">Page to add with valid path and page number.</param>
    /// <remarks>
    /// <para><strong>Validation:</strong> Verify unit exists, page number is unique within unit.</para>
    /// <para><strong>Logging:</strong> Log Debug with unitId and page number.</para>
    /// <para><strong>Security:</strong> Verify file path is within allowed storage directory.</para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">Thrown if unit does not exist.</exception>
    /// <exception cref="ArgumentException">Thrown if page number already exists in unit.</exception>
    void AddPage(string unitId, Page page);
    
    /// <summary>
    /// Retrieves all pages for a unit.
    /// </summary>
    /// <param name="unitId">Unit URN (urn:mvn:unit:{uuid}) or raw UUID.</param>
    /// <returns>Enumerable of pages ordered by page number (ascending).</returns>
    /// <remarks>
    /// <para><strong>Performance:</strong> Use index on unit_id, may be lazy-loaded.</para>
    /// <para><strong>Logging:</strong> Log Debug with unitId and result count.</para>
    /// </remarks>
    IEnumerable<Page> GetPages(string unitId);
    
    #endregion

    #region Progress Tracking
    
    /// <summary>
    /// Updates reading progress for a user.
    /// </summary>
    /// <param name="userId">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <param name="progress">Reading progress data with series_urn, current_unit, page, timestamp.</param>
    /// <remarks>
    /// <para><strong>Upsert:</strong> Creates new or updates existing progress for user+series combination.</para>
    /// <para><strong>Logging:</strong> Log Debug with userId, series_urn, and progress details.</para>
    /// <para><strong>Privacy:</strong> Progress data is user-specific, enforce user isolation.</para>
    /// <para><strong>Validation:</strong> Verify series and unit exist.</para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">Thrown if user or series does not exist.</exception>
    void UpdateProgress(string userId, ReadingProgress progress);
    
    /// <summary>
    /// Retrieves reading progress for a user and specific series.
    /// </summary>
    /// <param name="userId">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <param name="seriesUrn">Series URN (urn:mvn:series:{uuid}).</param>
    /// <returns>Reading progress if exists, null if user hasn't read this series.</returns>
    /// <remarks>
    /// <para><strong>Privacy:</strong> Only return progress for requesting user.</para>
    /// <para><strong>Logging:</strong> Log Debug with userId and seriesUrn.</para>
    /// </remarks>
    ReadingProgress? GetProgress(string userId, string seriesUrn);
    
    /// <summary>
    /// Gets all reading progress for a user (library view).
    /// </summary>
    /// <param name="userId">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <returns>Enumerable of reading progress records for all series user has accessed.</returns>
    /// <remarks>
    /// <para><strong>Use Case:</strong> "My Library" page showing all series in progress.</para>
    /// <para><strong>Privacy:</strong> Only accessible by owning user or admin.</para>
    /// <para><strong>Logging:</strong> Log Debug with userId and result count.</para>
    /// <para><strong>Performance:</strong> May be large dataset, consider pagination.</para>
    /// </remarks>
    IEnumerable<ReadingProgress> GetLibrary(string userId);
    
    /// <summary>
    /// Gets reading history for a user (recently read series).
    /// </summary>
    /// <param name="userId">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <returns>Enumerable of reading progress records ordered by last_read_at (desc).</returns>
    /// <remarks>
    /// <para><strong>Use Case:</strong> "Continue Reading" section, recent history.</para>
    /// <para><strong>Privacy:</strong> Only accessible by owning user or admin.</para>
    /// <para><strong>Logging:</strong> Log Debug with userId and result count.</para>
    /// <para><strong>Ordering:</strong> Most recent first, consider limiting to top 50.</para>
    /// </remarks>
    IEnumerable<ReadingProgress> GetHistory(string userId);
    
    #endregion

    #region Social Features
    
    /// <summary>
    /// Adds a comment to a series or unit.
    /// </summary>
    /// <param name="comment">Comment to add with target_urn, user_urn, content.</param>
    /// <remarks>
    /// <para><strong>Validation:</strong> Verify user and target exist, content is not empty.</para>
    /// <para><strong>Security:</strong> Sanitize content to prevent XSS, check for spam patterns.</para>
    /// <para><strong>Logging:</strong> Log Info with user_urn, target_urn, and comment length.</para>
    /// <para><strong>Moderation:</strong> Consider rate limiting, profanity filtering.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if comment is null or content is empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if user or target does not exist.</exception>
    void AddComment(Comment comment);
    
    /// <summary>
    /// Retrieves all comments for a series or unit.
    /// </summary>
    /// <param name="targetUrn">URN of series or unit.</param>
    /// <returns>Enumerable of comments ordered by creation date (newest first).</returns>
    /// <remarks>
    /// <para><strong>Filtering:</strong> May exclude deleted/hidden comments based on user permissions.</para>
    /// <para><strong>Logging:</strong> Log Debug with targetUrn and result count.</para>
    /// <para><strong>Performance:</strong> Consider pagination for series with many comments.</para>
    /// </remarks>
    IEnumerable<Comment> GetComments(string targetUrn);

    /// <summary>
    /// Adds or updates a vote (rating) from a user.
    /// </summary>
    /// <param name="userId">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <param name="vote">Vote data with target_urn and value (e.g., 1-5 stars, thumbs up/down).</param>
    /// <remarks>
    /// <para><strong>Upsert:</strong> Updates existing vote or creates new one for user+target.</para>
    /// <para><strong>Validation:</strong> Verify user and target exist, vote value is within allowed range.</para>
    /// <para><strong>Logging:</strong> Log Info with userId, target_urn, and vote value.</para>
    /// <para><strong>Aggregation:</strong> May trigger recalculation of average rating for target.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if vote value is invalid.</exception>
    void AddVote(string userId, Vote vote);
    
    #endregion

    #region Collection Operations
    
    /// <summary>
    /// Adds a new collection for a user.
    /// </summary>
    /// <param name="userId">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <param name="collection">Collection to add with name, description, series_urns.</param>
    /// <remarks>
    /// <para><strong>Validation:</strong> Verify user exists, collection name is unique for user.</para>
    /// <para><strong>Logging:</strong> Log Info with userId, collection URN, and name.</para>
    /// <para><strong>Ownership:</strong> Collection.user_urn must match userId parameter.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown if collection name already exists for user.</exception>
    void AddCollection(string userId, Collection collection);
    
    /// <summary>
    /// Lists all collections for a user.
    /// </summary>
    /// <param name="userId">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <returns>Enumerable of collections ordered by creation date or name.</returns>
    /// <remarks>
    /// <para><strong>Privacy:</strong> Only return collections owned by userId.</para>
    /// <para><strong>Logging:</strong> Log Debug with userId and result count.</para>
    /// </remarks>
    IEnumerable<Collection> ListCollections(string userId);
    
    /// <summary>
    /// Retrieves a collection by its URN or UUID.
    /// </summary>
    /// <param name="id">Collection URN (urn:mvn:collection:{uuid}) or raw UUID.</param>
    /// <returns>Collection if found, null otherwise.</returns>
    /// <remarks>
    /// <para><strong>Privacy:</strong> Verify caller has permission to view (owner or admin).</para>
    /// <para><strong>Logging:</strong> Log Debug with collection ID.</para>
    /// </remarks>
    Collection? GetCollection(string id);
    
    /// <summary>
    /// Updates an existing collection.
    /// </summary>
    /// <param name="collection">Collection with updated data. URN must match existing collection.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Only owner can update collection.</para>
    /// <para><strong>Validation:</strong> Verify collection exists, user_urn cannot be changed.</para>
    /// <para><strong>Logging:</strong> Log Info with collection URN and modified fields.</para>
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">Thrown if caller is not owner.</exception>
    void UpdateCollection(Collection collection);
    
    /// <summary>
    /// Deletes a collection.
    /// </summary>
    /// <param name="id">Collection URN (urn:mvn:collection:{uuid}) or raw UUID.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Only owner or admin can delete.</para>
    /// <para><strong>Logging:</strong> Log Warning with collection URN before deletion.</para>
    /// <para><strong>Side Effects:</strong> Does NOT delete series within collection, only the collection itself.</para>
    /// </remarks>
    void DeleteCollection(string id);
    
    #endregion

    #region Reporting
    
    /// <summary>
    /// Adds a content report (spam, abuse, copyright violation, etc.).
    /// </summary>
    /// <param name="report">Report data with reporter_urn, target_urn, reason, description.</param>
    /// <remarks>
    /// <para><strong>Validation:</strong> Verify reporter and target exist, reason is valid type.</para>
    /// <para><strong>Security:</strong> Rate limit reports per user to prevent abuse.</para>
    /// <para><strong>Logging:</strong> Log Warning with reporter_urn, target_urn, and reason (for moderation queue).</para>
    /// <para><strong>Workflow:</strong> Creates report with status "pending", notifies moderators.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown if report reason is invalid.</exception>
    void AddReport(Report report);
    
    #endregion

    #region System Configuration
    
    /// <summary>
    /// Gets system-wide configuration settings.
    /// </summary>
    /// <returns>System configuration including allowed media types, max file sizes, features enabled.</returns>
    /// <remarks>
    /// <para><strong>Caching:</strong> Consider caching as this is frequently accessed.</para>
    /// <para><strong>Logging:</strong> Log Debug on access.</para>
    /// </remarks>
    SystemConfig GetSystemConfig();
    
    /// <summary>
    /// Updates system-wide configuration settings.
    /// </summary>
    /// <param name="config">New configuration. Validates settings before applying.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Requires admin privileges, validate all settings.</para>
    /// <para><strong>Logging:</strong> Log Warning with changed settings and admin who made change.</para>
    /// <para><strong>Validation:</strong> Ensure file size limits are reasonable, paths are valid.</para>
    /// <para><strong>Side Effects:</strong> May require service restart for some settings.</para>
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">Thrown if caller is not admin.</exception>
    /// <exception cref="ArgumentException">Thrown if config contains invalid values.</exception>
    void UpdateSystemConfig(SystemConfig config);

    /// <summary>
    /// Gets system-wide statistics (total series, users, storage used, etc.).
    /// </summary>
    /// <returns>System statistics for dashboard/monitoring.</returns>
    /// <remarks>
    /// <para><strong>Performance:</strong> May be expensive to calculate, consider caching with TTL.</para>
    /// <para><strong>Logging:</strong> Log Debug on access.</para>
    /// <para><strong>Use Case:</strong> Admin dashboard, system health monitoring.</para>
    /// </remarks>
    SystemStats GetSystemStats();
    
    #endregion

    #region User Management
    
    /// <summary>
    /// Adds a new user to the repository.
    /// </summary>
    /// <param name="user">User to add with valid URN, username, email.</param>
    /// <remarks>
    /// <para><strong>Validation:</strong> Username and email must be unique, URN format must be urn:mvn:user:{uuid}.</para>
    /// <para><strong>Security:</strong> Hash passwords before storage (if applicable), validate email format.</para>
    /// <para><strong>Logging:</strong> Log Info with user URN and username (NOT password/email).</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown if username or email already exists.</exception>
    void AddUser(User user);
    
    /// <summary>
    /// Updates an existing user.
    /// </summary>
    /// <param name="user">User with updated data. URN must match existing user.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Validate username/email uniqueness if changed.</para>
    /// <para><strong>Logging:</strong> Log Info with user URN and modified fields.</para>
    /// <para><strong>Privacy:</strong> User can only update own data (unless admin).</para>
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">Thrown if non-admin tries to update other user.</exception>
    void UpdateUser(User user);
    
    /// <summary>
    /// Retrieves a user by URN or UUID.
    /// </summary>
    /// <param name="id">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <returns>User if found, null otherwise.</returns>
    /// <remarks>
    /// <para><strong>Privacy:</strong> Exclude sensitive fields (password hash) from returned object.</para>
    /// <para><strong>Logging:</strong> Log Debug with query ID.</para>
    /// </remarks>
    User? GetUser(string id);
    
    /// <summary>
    /// Retrieves a user by username (for login).
    /// </summary>
    /// <param name="username">Username (case-insensitive, unique).</param>
    /// <returns>User if found, null otherwise.</returns>
    /// <remarks>
    /// <para><strong>Security:</strong> Use for authentication flow, return password hash for verification.</para>
    /// <para><strong>Logging:</strong> Log Debug with username (not password).</para>
    /// <para><strong>Performance:</strong> Should have unique index on username (lowercase).</para>
    /// </remarks>
    User? GetUserByUsername(string username);
    
    /// <summary>
    /// Lists all users in the system.
    /// </summary>
    /// <returns>Enumerable of users.</returns>
    /// <remarks>
    /// <para><strong>Security:</strong> Admin-only operation, exclude password hashes.</para>
    /// <para><strong>Logging:</strong> Log Info with caller URN and result count.</para>
    /// <para><strong>Performance:</strong> May be large dataset, consider pagination.</para>
    /// </remarks>
    IEnumerable<User> ListUsers();
    
    /// <summary>
    /// Deletes a user and all associated data (GDPR "right to be forgotten").
    /// </summary>
    /// <param name="id">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Requires admin privileges or user deleting own account.</para>
    /// <para><strong>Logging:</strong> Log Warning with user URN before deletion.</para>
    /// <para><strong>GDPR:</strong> Deletes user, passkeys, progress, collections, edit permissions.</para>
    /// <para><strong>Retention:</strong> Comments/votes may be anonymized instead of deleted.</para>
    /// <para><strong>Atomicity:</strong> Should be transactional.</para>
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">Thrown if caller lacks permission.</exception>
    void DeleteUser(string id);
    
    /// <summary>
    /// Deletes user's reading history and progress.
    /// </summary>
    /// <param name="userId">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <remarks>
    /// <para><strong>Privacy:</strong> Allows user to clear reading history.</para>
    /// <para><strong>Logging:</strong> Log Info with userId.</para>
    /// <para><strong>Side Effects:</strong> User account remains, only progress deleted.</para>
    /// </remarks>
    void DeleteUserHistory(string userId);
    
    /// <summary>
    /// Anonymizes user-generated content (comments, reports) for GDPR compliance.
    /// </summary>
    /// <param name="userId">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <remarks>
    /// <para><strong>GDPR:</strong> Replaces user references with "[deleted]" or similar.</para>
    /// <para><strong>Logging:</strong> Log Info with userId and content anonymized count.</para>
    /// <para><strong>Use Case:</strong> User deletion that preserves community content.</para>
    /// </remarks>
    void AnonymizeUserContent(string userId);
    
    /// <summary>
    /// Checks if at least one admin user exists in the system.
    /// </summary>
    /// <returns>True if admin exists, false if no admin users.</returns>
    /// <remarks>
    /// <para><strong>Use Case:</strong> Initial setup flow, prevent locking out system.</para>
    /// <para><strong>Logging:</strong> Log Debug with result.</para>
    /// </remarks>
    bool IsAdminSet();
    
    #endregion

    #region Passkey (WebAuthn) Operations
    
    /// <summary>
    /// Adds a new WebAuthn passkey for a user.
    /// </summary>
    /// <param name="passkey">Passkey to add with credential_id, public_key, counter, user_urn.</param>
    /// <remarks>
    /// <para><strong>Validation:</strong> Verify credential_id is unique, user exists.</para>
    /// <para><strong>Security:</strong> Store public key securely, validate attestation during registration.</para>
    /// <para><strong>Logging:</strong> Log Info with user_urn and credential_id (first 8 chars only).</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown if credential_id already exists.</exception>
    void AddPasskey(Passkey passkey);
    
    /// <summary>
    /// Updates an existing passkey (counter for replay attack prevention, last_used timestamp).
    /// </summary>
    /// <param name="passkey">Passkey with updated counter and last_used_at.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Counter must always increase (WebAuthn replay protection).</para>
    /// <para><strong>Logging:</strong> Log Debug with credential_id and new counter value.</para>
    /// <para><strong>Use Case:</strong> Called after each successful authentication.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if new counter <= old counter.</exception>
    void UpdatePasskey(Passkey passkey);
    
    /// <summary>
    /// Retrieves all passkeys for a user.
    /// </summary>
    /// <param name="userId">User URN (urn:mvn:user:{uuid}) or raw UUID.</param>
    /// <returns>Enumerable of passkeys ordered by creation date.</returns>
    /// <remarks>
    /// <para><strong>Use Case:</strong> User managing their registered devices/passkeys.</para>
    /// <para><strong>Logging:</strong> Log Debug with userId and result count.</para>
    /// </remarks>
    IEnumerable<Passkey> GetPasskeysByUser(string userId);
    
    /// <summary>
    /// Retrieves a passkey by credential ID (used during WebAuthn authentication).
    /// </summary>
    /// <param name="credentialId">Base64-encoded credential ID from authenticator.</param>
    /// <returns>Passkey if found, null otherwise.</returns>
    /// <remarks>
    /// <para><strong>Performance:</strong> Critical path during auth, must have unique index.</para>
    /// <para><strong>Logging:</strong> Log Debug with credential_id (first 8 chars).</para>
    /// <para><strong>Security:</strong> Return includes public_key for signature verification.</para>
    /// </remarks>
    Passkey? GetPasskeyByCredentialId(string credentialId);
    
    /// <summary>
    /// Retrieves a passkey by its URN or UUID.
    /// </summary>
    /// <param name="id">Passkey URN (urn:mvn:passkey:{uuid}) or raw UUID.</param>
    /// <returns>Passkey if found, null otherwise.</returns>
    /// <remarks>
    /// <para><strong>Use Case:</strong> Passkey management (rename, delete).</para>
    /// <para><strong>Logging:</strong> Log Debug with passkey URN.</para>
    /// </remarks>
    Passkey? GetPasskey(string id);
    
    /// <summary>
    /// Deletes a passkey (user removing a registered device).
    /// </summary>
    /// <param name="id">Passkey URN (urn:mvn:passkey:{uuid}) or raw UUID.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Only owner or admin can delete, verify user has other auth methods.</para>
    /// <para><strong>Logging:</strong> Log Warning with passkey URN and user_urn.</para>
    /// <para><strong>Safety:</strong> Consider preventing deletion of last passkey.</para>
    /// </remarks>
    void DeletePasskey(string id);
    
    #endregion

    #region Node Metadata
    
    /// <summary>
    /// Gets metadata about the current MehguViewer node (instance information).
    /// </summary>
    /// <returns>Node metadata including node_id, version, uptime, features.</returns>
    /// <remarks>
    /// <para><strong>Use Case:</strong> Federation, multi-node deployments, health checks.</para>
    /// <para><strong>Logging:</strong> Log Debug on access.</para>
    /// </remarks>
    NodeMetadata GetNodeMetadata();
    
    /// <summary>
    /// Updates node metadata.
    /// </summary>
    /// <param name="metadata">New metadata. Validates version and feature flags.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Admin-only operation.</para>
    /// <para><strong>Logging:</strong> Log Info with changed fields.</para>
    /// <para><strong>Validation:</strong> Ensure version follows semantic versioning.</para>
    /// </remarks>
    void UpdateNodeMetadata(NodeMetadata metadata);
    
    #endregion

    #region Taxonomy Configuration
    
    /// <summary>
    /// Gets taxonomy configuration (valid tags, authors, scanlators registry).
    /// </summary>
    /// <returns>Taxonomy configuration with allowed values and mappings.</returns>
    /// <remarks>
    /// <para><strong>Use Case:</strong> Autocomplete, validation, standardization.</para>
    /// <para><strong>Caching:</strong> Should be cached as frequently accessed.</para>
    /// <para><strong>Logging:</strong> Log Debug on access.</para>
    /// </remarks>
    TaxonomyConfig GetTaxonomyConfig();
    
    /// <summary>
    /// Updates taxonomy configuration (add/remove allowed tags, authors, scanlators).
    /// </summary>
    /// <param name="config">New taxonomy configuration.</param>
    /// <remarks>
    /// <para><strong>Security:</strong> Admin-only operation, validate all taxonomy entries.</para>
    /// <para><strong>Logging:</strong> Log Warning with changes made (added/removed items).</para>
    /// <para><strong>Validation:</strong> Ensure no orphaned references exist when removing taxonomy items.</para>
    /// <para><strong>Side Effects:</strong> May require revalidation of existing series/units.</para>
    /// </remarks>
    void UpdateTaxonomyConfig(TaxonomyConfig config);
    
    #endregion
}
