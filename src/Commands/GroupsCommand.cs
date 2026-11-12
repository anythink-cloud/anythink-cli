using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;

namespace AnythinkCli.Commands;

public abstract class GroupsCommand<TSettings> : BaseCommand<TSettings> where TSettings : CommandSettings
{
    public sealed override async Task<int> ExecuteAsync(CommandContext context, TSettings settings)
    {
        try
        {
            return await RunAsync(GetClient(), settings);
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    protected abstract Task<int> RunAsync(AnythinkClient client, TSettings settings);

    protected static int Json(object value)
    {
        Renderer.PrintJson(JsonSerializer.Serialize(value, JsonOutput.PrettyRelaxed));
        return 0;
    }

    protected static bool Confirmed(bool yes, string question)
    {
        if (yes || AnsiConsole.Confirm($"[yellow]{Markup.Escape(question)}[/]", defaultValue: false)) return true;
        Renderer.Info("Cancelled.");
        return false;
    }

    protected static int Members(List<GroupMemberResponse> members)
    {
        Renderer.Header($"Members ({members.Count})");
        var table = Renderer.BuildTable("Member ID", "User ID", "Name", "Email", "Role", "Manages", "Reads data");
        foreach (var m in members)
            Renderer.AddRow(table, m.Id.ToString(), m.UserId.ToString(), m.User?.Name, m.User?.Email, m.Role,
                m.CanManageGroup ? "yes" : "no", m.CanReadGroupData ? "yes" : "no");
        AnsiConsole.Write(table);
        return 0;
    }

    protected static int Member(string message, GroupMemberResponse member)
    {
        Renderer.Success(Markup.Escape(message));
        Renderer.KeyValue("Member ID", member.Id.ToString());
        Renderer.KeyValue("Group ID", member.GroupId.ToString());
        Renderer.KeyValue("User ID", member.UserId.ToString());
        Renderer.KeyValue("Role", $"{member.Role} ({member.RoleId})");
        return 0;
    }

    protected static int Invites(List<GroupInviteResponse> invites)
    {
        Renderer.Header($"Invites, codes and requests ({invites.Count})");
        var table = Renderer.BuildTable("ID", "Group", "Kind", "Status", "Who", "Role", "Code", "Expires");
        foreach (var i in invites)
            Renderer.AddRow(table, i.Id.ToString(), i.GroupName, i.Kind, i.Status, i.Email ?? i.User?.Name, i.Role, i.Code,
                i.ExpiresAt?.ToString("yyyy-MM-dd HH:mm"));
        AnsiConsole.Write(table);
        return 0;
    }

    protected static int Invite(string message, GroupInviteResponse invite)
    {
        Renderer.Success(Markup.Escape(message));
        Renderer.KeyValue("ID", invite.Id.ToString());
        Renderer.KeyValue("Group", $"{invite.GroupName} ({invite.GroupId})");
        Renderer.KeyValue("Kind", invite.Kind);
        Renderer.KeyValue("Status", invite.Status);
        Renderer.KeyValue("Role", $"{invite.Role} ({invite.RoleId})");
        if (invite.Email is not null) Renderer.KeyValue("Email", invite.Email);
        if (invite.Code is not null) Renderer.KeyValue("Code", invite.Code);
        if (invite.ExpiresAt is { } expires) Renderer.KeyValue("Expires", expires.ToString("yyyy-MM-dd HH:mm"));
        return 0;
    }
}

public class GroupIdSettings : JsonSettings
{
    [CommandArgument(0, "<GROUP_ID>")]
    [Description("Group (team) ID")]
    public int GroupId { get; set; }
}

public class GroupRoleSettings : CommandSettings
{
    [CommandArgument(0, "<GROUP_ID>")]
    [Description("Group (team) ID")]
    public int GroupId { get; set; }

    [CommandOption("--role-id <ROLE_ID>")]
    [Description("Role the person gets in the group")]
    public int? RoleId { get; set; }

    public override ValidationResult Validate() =>
        RoleId is > 0 ? ValidationResult.Success() : ValidationResult.Error("Pass --role-id with the role to give in the group.");
}

public class GroupConfirmSettings : CommandSettings
{
    [CommandArgument(0, "<GROUP_ID>")]
    [Description("Group (team) ID")]
    public int GroupId { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

public class GroupInviteIdSettings : CommandSettings
{
    [CommandArgument(0, "<GROUP_ID>")]
    [Description("Group (team) ID")]
    public int GroupId { get; set; }

    [CommandArgument(1, "<INVITE_ID>")]
    [Description("Invite, code or request ID")]
    public int InviteId { get; set; }
}

public class MyInviteSettings : CommandSettings
{
    [CommandArgument(0, "<INVITE_ID>")]
    [Description("ID of your invite or request")]
    public int InviteId { get; set; }
}

// ── groups list / get / mine ─────────────────────────────────────────────────

[CommandGuide("Lists every group (team) in the project with its id, name and description. Needs project-wide anythink_groups:read; a team member who lacks it sees their own teams with `groups_mine`. Use a group's id with `groups_members_list` or `groups_invite`.")]
public class GroupsListCommand : GroupsCommand<JsonSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, JsonSettings settings)
    {
        var groups = await client.GetGroupsAsync();
        if (settings.Json) return Json(groups);

        Renderer.Header($"Groups ({groups.Count})");
        var table = Renderer.BuildTable("ID", "Name", "Description", "System");
        foreach (var g in groups)
            Renderer.AddRow(table, g.Id.ToString(), g.Name, g.Description, g.IsSystem ? "yes" : null);
        AnsiConsole.Write(table);
        return 0;
    }
}

[CommandGuide("Shows one group's (team's) name, description and owner. Needs project-wide anythink_groups:read; members see their teams with `groups_mine` and the people in one with `groups_members_list`.")]
public class GroupsGetCommand : GroupsCommand<GroupIdSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupIdSettings settings)
    {
        var group = await client.GetGroupAsync(settings.GroupId);
        if (group is null)
        {
            Renderer.Error($"Group {settings.GroupId} not found.");
            return 1;
        }
        if (settings.Json) return Json(group);

        Renderer.Header($"Group: {group.Name}");
        Renderer.KeyValue("ID", group.Id.ToString());
        Renderer.KeyValue("Description", group.Description);
        Renderer.KeyValue("Owner user ID", group.OwnerUserId?.ToString());
        return 0;
    }
}

[CommandGuide("Lists the teams you belong to, with your role in each and whether that role can read the team's data or manage the team. Works for any member, without project-wide groups access. Use it to find a group id for `groups_members_list`, or to check you can manage a team before `groups_invite`.")]
public class GroupsMineCommand : GroupsCommand<JsonSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, JsonSettings settings)
    {
        var mine = await client.GetMyGroupsAsync();
        if (settings.Json) return Json(mine);

        Renderer.Header($"My groups ({mine.Count})");
        var table = Renderer.BuildTable("Group ID", "Name", "Role", "Manages", "Reads data");
        foreach (var m in mine)
            Renderer.AddRow(table, m.GroupId.ToString(), m.GroupName, m.Role, m.CanManageGroup ? "yes" : "no", m.CanReadGroupData ? "yes" : "no");
        AnsiConsole.Write(table);
        return 0;
    }
}

// ── groups create / update / delete ──────────────────────────────────────────

public class GroupCreateSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Group (team) name, unique in the project")]
    public string Name { get; set; } = "";

    [CommandOption("--description <DESC>")]
    [Description("Optional description")]
    public string? Description { get; set; }
}

[CommandGuide("Creates a group (team) owned by you. Needs project-wide anythink_groups:create. Then bring people in with `groups_invite` or `groups_code`, or add existing users directly with `groups_members_add`.")]
public class GroupsCreateCommand : GroupsCommand<GroupCreateSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupCreateSettings settings)
    {
        var group = await client.CreateGroupAsync(new SaveGroupRequest(settings.Name, settings.Description));
        Renderer.Success($"Group [#F97316]{Markup.Escape(group.Name)}[/] created.");
        Renderer.KeyValue("ID", group.Id.ToString());
        return 0;
    }
}

public class GroupUpdateSettings : CommandSettings
{
    [CommandArgument(0, "<GROUP_ID>")]
    [Description("Group (team) ID")]
    public int GroupId { get; set; }

    [CommandOption("--name <NAME>")]
    [Description("New name")]
    public string? Name { get; set; }

    [CommandOption("--description <DESC>")]
    [Description("New description")]
    public string? Description { get; set; }
}

[CommandGuide("Renames a group (team) or changes its description; only the values you pass change. Needs project-wide anythink_groups:update, and built-in system groups can't be edited.")]
public class GroupsUpdateCommand : GroupsCommand<GroupUpdateSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupUpdateSettings settings)
    {
        await client.UpdateGroupAsync(settings.GroupId, new SaveGroupRequest(settings.Name, settings.Description));
        Renderer.Success($"Group [#F97316]{settings.GroupId}[/] updated.");
        return 0;
    }
}

[CommandGuide("Deletes a group (team) with its memberships, invites, join codes and requests. Records shared with the group are no longer shared with its members. Needs project-wide anythink_groups:delete; system groups can't be deleted.")]
public class GroupsDeleteCommand : GroupsCommand<GroupConfirmSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupConfirmSettings settings)
    {
        if (!Confirmed(settings.Yes, $"Delete group {settings.GroupId}?")) return 0;
        await client.DeleteGroupAsync(settings.GroupId);
        Renderer.Success($"Group [#F97316]{settings.GroupId}[/] deleted.");
        return 0;
    }
}

// ── groups members ───────────────────────────────────────────────────────────

[CommandGuide("Lists a team's members with member id, user id, name, email, role, and whether each can manage the team or read its data. Any member of the team can list it; others need project-wide anythink_groups:read. Use the member id with `groups_members_update` or `groups_members_remove`.")]
public class GroupsMembersListCommand : GroupsCommand<GroupIdSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupIdSettings settings)
    {
        var members = await client.GetGroupMembersAsync(settings.GroupId);
        return settings.Json ? Json(members) : Members(members);
    }
}

public class GroupMemberAddSettings : GroupRoleSettings
{
    [CommandArgument(1, "<USER_ID>")]
    [Description("User to add")]
    public int UserId { get; set; }
}

[CommandGuide("Adds an existing project user to a team at once, without asking them. Only project-wide anythink_groups:update may do this, because a team role can read members' data; team managers bring people in with `groups_invite` or `groups_code` instead.")]
public class GroupsMembersAddCommand : GroupsCommand<GroupMemberAddSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupMemberAddSettings settings) =>
        Member("Member added.", await client.AddGroupMemberAsync(settings.GroupId, new AddGroupMemberRequest(settings.UserId, settings.RoleId!.Value)));
}

public class GroupMemberUpdateSettings : GroupRoleSettings
{
    [CommandArgument(1, "<MEMBER_ID>")]
    [Description("Member ID from groups members list")]
    public int MemberId { get; set; }
}

[CommandGuide("Changes a member's role in the team, for example to make them a manager. Team managers can give any team role except a project administrator role; project-wide anythink_groups:update can give any role. Find the member id with `groups_members_list`.")]
public class GroupsMembersUpdateCommand : GroupsCommand<GroupMemberUpdateSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupMemberUpdateSettings settings)
    {
        var member = await client.UpdateGroupMemberAsync(settings.GroupId, settings.MemberId, new UpdateGroupMemberRequest(settings.RoleId!.Value));
        return member is null ? 0 : Member("Role changed.", member);
    }
}

public class GroupMemberRemoveSettings : GroupConfirmSettings
{
    [CommandArgument(1, "<MEMBER_ID>")]
    [Description("Member ID from groups members list")]
    public int MemberId { get; set; }
}

[CommandGuide("Removes someone from a team, so they lose the team's shared records and their team role. Team managers can remove anyone and any member can remove themselves. Find the member id with `groups_members_list`.")]
public class GroupsMembersRemoveCommand : GroupsCommand<GroupMemberRemoveSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupMemberRemoveSettings settings)
    {
        if (!Confirmed(settings.Yes, $"Remove member {settings.MemberId} from group {settings.GroupId}?")) return 0;
        await client.RemoveGroupMemberAsync(settings.GroupId, settings.MemberId);
        Renderer.Success($"Member [#F97316]{settings.MemberId}[/] removed.");
        return 0;
    }
}

// ── groups invite / code ─────────────────────────────────────────────────────

public class GroupInviteSettings : GroupRoleSettings
{
    [CommandArgument(1, "<EMAIL>")]
    [Description("Email address to invite")]
    public string Email { get; set; } = "";
}

[CommandGuide("Emails someone a link to join a team with the given role; it expires in 7 days and re-inviting the same address replaces the earlier link. They join when they accept it signed in, by the link or from `groups_invites_mine`. Needs a team role that can manage the team, or project-wide anythink_groups:update; a manager can't invite into a project administrator role.")]
public class GroupsInviteCommand : GroupsCommand<GroupInviteSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupInviteSettings settings) =>
        Invite("Invite sent.", await client.CreateGroupInviteAsync(settings.GroupId,
            new CreateGroupInviteRequest("invite", settings.RoleId!.Value, settings.Email)));
}

[CommandGuide("Makes a short join code for a team with the given role and revokes the team's previous code. People enter it with `groups_join`; unless the project setting group_codes_require_approval is off, that makes a request a manager approves with `groups_invites_approve`. Needs a team role that can manage the team, or project-wide anythink_groups:update.")]
public class GroupsCodeCommand : GroupsCommand<GroupRoleSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupRoleSettings settings) =>
        Invite("Join code created.", await client.CreateGroupInviteAsync(settings.GroupId,
            new CreateGroupInviteRequest("code", settings.RoleId!.Value)));
}

// ── groups invites (managers) ────────────────────────────────────────────────

public class GroupInvitesListSettings : GroupIdSettings
{
    [CommandOption("--status <STATUS>")]
    [Description("Only this status: pending (live, not expired), accepted, declined or revoked")]
    public string? Status { get; set; }
}

[CommandGuide("Lists a team's email invites, its join code and people's join requests, with id, kind, status, who, role and expiry. Pass status pending to see only what is still open. Needs a team role that can manage the team, or project-wide anythink_groups:read. Act on a request with `groups_invites_approve` or `groups_invites_decline`.")]
public class GroupsInvitesListCommand : GroupsCommand<GroupInvitesListSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupInvitesListSettings settings)
    {
        var invites = await client.GetGroupInvitesAsync(settings.GroupId, settings.Status);
        return settings.Json ? Json(invites) : Invites(invites);
    }
}

public class GroupInviteRevokeSettings : GroupInviteIdSettings
{
    [CommandOption("-y|--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

[CommandGuide("Cancels an open email invite, join code or join request, so it can no longer be used. Find its id with `groups_invites_list`. Needs a team role that can manage the team, or project-wide anythink_groups:update.")]
public class GroupsInvitesRevokeCommand : GroupsCommand<GroupInviteRevokeSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupInviteRevokeSettings settings)
    {
        if (!Confirmed(settings.Yes, $"Revoke invite {settings.InviteId}?")) return 0;
        await client.RevokeGroupInviteAsync(settings.GroupId, settings.InviteId);
        Renderer.Success($"Invite [#F97316]{settings.InviteId}[/] revoked.");
        return 0;
    }
}

public class GroupRequestApproveSettings : GroupInviteIdSettings
{
    [CommandOption("--role-id <ROLE_ID>")]
    [Description("Role to give instead of the join code's role")]
    public int? RoleId { get; set; }
}

[CommandGuide("Approves someone's request to join a team, adding them with the join code's role or the role you pass. A manager can't approve into a project administrator role. Find pending requests with `groups_invites_list`.")]
public class GroupsInvitesApproveCommand : GroupsCommand<GroupRequestApproveSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupRequestApproveSettings settings) =>
        Member("Request approved.", await client.ApproveGroupJoinRequestAsync(settings.GroupId, settings.InviteId, settings.RoleId));
}

[CommandGuide("Declines someone's request to join a team; they are not added and can ask again with a join code. Find pending requests with `groups_invites_list`.")]
public class GroupsInvitesDeclineCommand : GroupsCommand<GroupInviteIdSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupInviteIdSettings settings) =>
        Invite("Request declined.", await client.DeclineGroupJoinRequestAsync(settings.GroupId, settings.InviteId));
}

// ── groups join / accept and my invites ──────────────────────────────────────

public class GroupJoinSettings : CommandSettings
{
    [CommandArgument(0, "<CODE>")]
    [Description("Join code from a team manager")]
    public string Code { get; set; } = "";
}

[CommandGuide("Joins a team with a code a manager shared. When the project requires approval (the default) this makes a request a manager approves, shown in `groups_invites_mine`; otherwise you join at once with the code's role.")]
public class GroupsJoinCommand : GroupsCommand<GroupJoinSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupJoinSettings settings)
    {
        var joined = await client.JoinGroupAsync(settings.Code);
        return joined.Member is { } member
            ? Member("Joined the group.", member)
            : Invite("Request sent; a team manager will approve it.", joined.Request!);
    }
}

public class GroupAcceptSettings : CommandSettings
{
    [CommandArgument(0, "<TOKEN>")]
    [Description("Token from the invite email link (group_invite_token)")]
    public string Token { get; set; } = "";
}

[CommandGuide("Accepts an emailed team invite by the token in its link, adding you to the team with the invited role. If you are signed in with the invited email address, `groups_invites_accept` does the same without the token.")]
public class GroupsAcceptCommand : GroupsCommand<GroupAcceptSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, GroupAcceptSettings settings) =>
        Member("Invite accepted.", await client.AcceptGroupInviteTokenAsync(settings.Token));
}

[CommandGuide("Lists your open team invites (sent to your email address) and your pending join requests, with id, team, kind and role. Accept or reject an invite with `groups_invites_accept` or `groups_invites_reject`; withdraw a request with `groups_invites_withdraw`.")]
public class GroupsInvitesMineCommand : GroupsCommand<JsonSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, JsonSettings settings)
    {
        var invites = await client.GetMyGroupInvitesAsync();
        return settings.Json ? Json(invites) : Invites(invites);
    }
}

[CommandGuide("Accepts one of your open team invites without its emailed token, matched on the email address you are signed in with. Find the id with `groups_invites_mine`.")]
public class GroupsInvitesAcceptCommand : GroupsCommand<MyInviteSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, MyInviteSettings settings) =>
        Member("Invite accepted.", await client.AcceptMyGroupInviteAsync(settings.InviteId));
}

[CommandGuide("Turns down one of your open team invites so it can't be used. Find the id with `groups_invites_mine`.")]
public class GroupsInvitesRejectCommand : GroupsCommand<MyInviteSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, MyInviteSettings settings) =>
        Invite("Invite declined.", await client.DeclineMyGroupInviteAsync(settings.InviteId));
}

[CommandGuide("Withdraws your pending request to join a team, before a manager decides it. Find the id with `groups_invites_mine`.")]
public class GroupsInvitesWithdrawCommand : GroupsCommand<MyInviteSettings>
{
    protected override async Task<int> RunAsync(AnythinkClient client, MyInviteSettings settings)
    {
        await client.WithdrawMyGroupRequestAsync(settings.InviteId);
        Renderer.Success($"Request [#F97316]{settings.InviteId}[/] withdrawn.");
        return 0;
    }
}
