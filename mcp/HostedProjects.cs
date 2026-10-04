using AnythinkCli.Client;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AnythinkMcp;

public sealed class HostedProjectException(string message) : Exception(message);

public sealed class HostedProjects(
    HostedCredentials credentials, ITokenExchanger exchanger, McpClientFactory factory, HostedMode.Options options)
{
    public const string ParameterName = "project";

    public const string ParameterDescription =
        "Project id from 'projects_list'. Required when the connection covers all your projects.";

    public async Task<AnythinkClient> ClientAsync(string? project, CancellationToken cancellationToken)
    {
        if (!credentials.AllProjects)
        {
            if (!string.IsNullOrEmpty(project) && !string.Equals(project, credentials.ProjectId, StringComparison.OrdinalIgnoreCase))
                throw new HostedProjectException("This connection covers one project only. Leave 'project' out.");
            return factory.GetClient(credentials);
        }

        if (!Guid.TryParse(project, out var projectId))
            throw new HostedProjectException("This connection covers all your projects. Pass 'project' with an id from 'projects_list'.");

        string token;
        try
        {
            token = await exchanger.ExchangeAsync(credentials.InboundToken!, projectId.ToString(), cancellationToken);
        }
        catch (TokenExchangeException ex)
        {
            throw new HostedProjectException(ex.Error == "invalid_target"
                ? "You don't have access to that project, or it doesn't exist."
                : "Couldn't get access to that project. Try again.");
        }

        var claims = new JsonWebToken(token);
        var orgId = claims.TryGetClaim("tid", out var tid) ? tid.Value : null;
        var instanceUrl = claims.TryGetClaim("instance_url", out var url) ? url.Value : null;
        if (orgId is null || !orgId.All(char.IsAsciiDigit) || instanceUrl is null
            || !HostedAuth.IsAllowedInstanceUrl(instanceUrl, options.AllowLoopbackInstance, options.AllowedInstanceHostSuffixes))
            throw new HostedProjectException("Couldn't get access to that project. Try again.");

        return factory.GetClient(new HostedCredentials { OrgId = orgId, InstanceUrl = instanceUrl.TrimEnd('/'), Token = token });
    }

    public async Task<string> ListAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await exchanger.ListProjectsAsync(credentials.InboundToken!, cancellationToken);
        }
        catch (TokenExchangeException)
        {
            throw new HostedProjectException("Couldn't list your projects. Try again.");
        }
    }
}
