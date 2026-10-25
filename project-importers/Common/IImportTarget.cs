using System.Text.Json.Nodes;
using Anythink.ProjectImporters.Models;

namespace Anythink.ProjectImporters;

public interface IImportTarget
{
    string BaseUrl { get; }
    string OrgId { get; }

    Task<List<Entity>> GetEntitiesAsync();
    Task<Entity> CreateEntityAsync(CreateEntityRequest req);
    Task<Field> AddFieldAsync(string entityName, CreateFieldRequest req);

    Task<List<Workflow>> GetWorkflowsAsync();
    Task<Workflow> CreateWorkflowAsync(CreateWorkflowRequest req);
    Task<WorkflowStep> AddWorkflowStepAsync(int workflowId, CreateWorkflowStepRequest req);
    Task<WorkflowStep?> UpdateWorkflowStepFullAsync(int workflowId, int stepId, object body);

    Task<PaginatedResult<JsonObject>> ListItemsAsync(
        string entityName, int page = 1, int pageSize = 20, string? filter = null, string? fields = null);
    Task<JsonObject> CreateItemAsync(string entityName, JsonObject data);

    Task<List<FileResponse>> GetAllFilesAsync();
    Task UpdateFileMetadataAsync(int id, UpdateFileMetadataRequest req);
    Task<FileResponse> UploadFileFromUrlAsync(
        string sourceUrl, string fileName, bool isPublic = false, string? sourceToken = null, long? maxBytes = null,
        IReadOnlyDictionary<string, string>? sourceHeaders = null, bool followRedirects = true);

    Task<List<RoleResponse>> GetRolesAsync();
    Task<RoleResponse> CreateRoleAsync(CreateRoleRequest req);
    Task<List<Permission>> GetPermissionsAsync();
    Task<Permission> CreatePermissionAsync(CreatePermissionRequest req);
    Task<RoleResponse?> UpdateRoleWithPermissionsAsync(int roleId, UpdateRolePermissionsRequest req);
}
