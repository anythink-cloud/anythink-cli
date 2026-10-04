using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

[McpServerToolType]
public class EmailTools
{
    private readonly McpClientFactory _factory;
    public EmailTools(McpClientFactory factory) => _factory = factory;

    [McpServerTool(Name = "email_templates_list"),
     Description("List every email template defined for the active project.")]
    public async Task<string> TemplatesList()
    {
        var client = _factory.GetClient();
        var templates = await client.GetEmailTemplatesAsync();
        return JsonSerializer.Serialize(templates.Select(t => new
        {
            t.TemplateType, t.Subject, t.Locked, t.IsActive, t.DisplayName, t.Description,
        }));
    }

    [McpServerTool(Name = "email_templates_show"),
     Description("Fetch a single email template's subject + content (the raw source the author edits).")]
    public async Task<string> TemplatesShow(
        [Description("Template type identifier, e.g. confirmation, invite, recovery")] string templateType)
    {
        var client = _factory.GetClient();
        var t = await client.GetEmailTemplateAsync(templateType);
        return JsonSerializer.Serialize(new { t.TemplateType, t.Subject, t.Content, t.Locked });
    }

    [McpServerTool(Name = "email_templates_preview"),
     Description("Render the saved template using sample data, returning the rendered subject + HTML (CSS inlined, images substituted with signed URLs for inline images).")]
    public async Task<string> TemplatesPreview(
        [Description("Template type identifier")] string templateType)
    {
        var client = _factory.GetClient();
        var p = await client.PreviewEmailTemplateAsync(templateType);
        return JsonSerializer.Serialize(new { p.Subject, p.HtmlContent });
    }

    [McpServerTool(Name = "email_templates_update"),
     Description("Update a template's subject and/or content. Pass null to keep the existing value for either field.")]
    public async Task<string> TemplatesUpdate(
        [Description("Template type identifier")] string templateType,
        [Description("New subject (null = keep existing)")] string? subject,
        [Description("New HTML/text content (null = keep existing)")] string? content)
    {
        var client = _factory.GetClient();
        var current = await client.GetEmailTemplateAsync(templateType);
        var updated = await client.UpdateEmailTemplateAsync(templateType,
            new UpdateEmailTemplateRequest(subject ?? current.Subject, content ?? current.Content));
        return JsonSerializer.Serialize(new
        {
            updated!.TemplateType, updated.Subject, updated.Content,
            Message = "Template saved.",
        });
    }

    [McpServerTool(Name = "email_preview_raw"),
     Description("Render uncommitted subject + content (and optionally an unsaved shell wrapper) so a draft can be inspected without persisting it. Returns the rendered HTML.")]
    public async Task<string> PreviewRaw(
        [Description("Subject line")] string? subject,
        [Description("Template content (HTML)")] string? content,
        [Description("Optional shell wrapper HTML to render against")] string? wrapperHtml)
    {
        var client = _factory.GetClient();
        var preview = await client.PreviewRawEmailAsync(new PreviewRawEmailRequest(subject, content, wrapperHtml));
        return JsonSerializer.Serialize(new { preview.Subject, preview.HtmlContent });
    }

    [McpServerTool(Name = "email_shell_show"),
     Description("Return the per-project shell template (the HTML that wraps every email body). Indicates whether the project has customised it.")]
    public async Task<string> ShellShow()
    {
        var client = _factory.GetClient();
        var shell = await client.GetEmailShellAsync();
        return JsonSerializer.Serialize(new { shell.Html, shell.IsCustomised, shell.DefaultHtml });
    }

    [McpServerTool(Name = "email_shell_update"),
     Description("Save a new shell template. Pass null html to reset to the platform default.")]
    public async Task<string> ShellUpdate(
        [Description("New shell HTML, or null to reset to default")] string? html)
    {
        var client = _factory.GetClient();
        var result = await client.UpdateEmailShellAsync(new UpdateEmailShellRequest(html));
        return JsonSerializer.Serialize(new
        {
            result!.Html,
            Message = html == null ? "Shell reset to platform default." : "Shell saved.",
        });
    }
}
