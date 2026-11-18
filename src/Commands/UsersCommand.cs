using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── users list ────────────────────────────────────────────────────────────────

[CommandGuide("Shows each person who can sign in to this project with their id, name, email, role and whether they have confirmed their invitation. Use it to find a user's id, or to see who holds a role before changing it with `roles_permissions_add` or deleting it with `roles_delete`.")]
public class UsersListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            List<UserResponse> users = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching users...", async _ =>
                {
                    users = await client.GetUsersAsync();
                });

            if (users.Count == 0)
            {
                Renderer.Info("No users found.");
                return 0;
            }

            Renderer.Header($"Users ({users.Count})");

            var table = Renderer.BuildTable("ID", "Name", "Email", "Role", "Confirmed");
            foreach (var u in users.OrderBy(x => x.Id))
            {
                Renderer.AddRow(table,
                    u.Id.ToString(),
                    $"{u.FirstName} {u.LastName}",
                    u.Email,
                    u.RoleName ?? (u.RoleId.HasValue ? u.RoleId.ToString() : "—"),
                    u.IsConfirmed ? "yes" : "no"
                );
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── users me ──────────────────────────────────────────────────────────────────

[CommandGuide("Shows the user you are signed in as in this project: id, name, email, role, whether confirmed and when created. Your role decides what every other tool may do, so check it when a call is refused; use `users_get` to look up someone else.")]
public class UsersMeCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            UserResponse? user = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching current user...", async _ =>
                {
                    user = await client.GetMeAsync();
                });

            if (user == null)
            {
                Renderer.Error("Could not retrieve current user.");
                return 1;
            }

            Renderer.Header("Current User");
            Renderer.KeyValue("ID", user.Id.ToString());
            Renderer.KeyValue("Name", $"{user.FirstName} {user.LastName}");
            Renderer.KeyValue("Email", user.Email);
            Renderer.KeyValue("Role", user.RoleName ?? (user.RoleId.HasValue ? user.RoleId.ToString() : null));
            Renderer.KeyValue("Confirmed", user.IsConfirmed ? "yes" : "no");
            Renderer.KeyValue("Created", user.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── users get ─────────────────────────────────────────────────────────────────

public class UserGetSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("User ID")]
    public int Id { get; set; }
}

[CommandGuide("Shows one user's name, email, role, whether they have confirmed their invitation and when they were created. Use it to check one person's role; use `users_list` to scan everyone, and `roles_permissions_list` to see what their role allows.")]
public class UsersGetCommand : BaseCommand<UserGetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UserGetSettings settings)
    {
        try
        {
            var client = GetClient();
            UserResponse? user = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Fetching user {settings.Id}..."), async _ =>
                {
                    user = await client.GetUserAsync(settings.Id);
                });

            if (user == null)
            {
                Renderer.Error($"User {settings.Id} not found.");
                return 1;
            }

            Renderer.Header($"User: {user.FirstName} {user.LastName}");
            Renderer.KeyValue("ID", user.Id.ToString());
            Renderer.KeyValue("Name", $"{user.FirstName} {user.LastName}");
            Renderer.KeyValue("Email", user.Email);
            Renderer.KeyValue("Role", user.RoleName ?? (user.RoleId.HasValue ? user.RoleId.ToString() : null));
            Renderer.KeyValue("Confirmed", user.IsConfirmed ? "yes" : "no");
            Renderer.KeyValue("Created", user.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── users invite ──────────────────────────────────────────────────────────────

public class UserInviteSettings : CommandSettings
{
    [CommandArgument(0, "<EMAIL>")]
    [Description("User email address")]
    public string Email { get; set; } = "";

    [CommandArgument(1, "<FIRST_NAME>")]
    [Description("First name")]
    public string FirstName { get; set; } = "";

    [CommandArgument(2, "<LAST_NAME>")]
    [Description("Last name")]
    public string LastName { get; set; } = "";

    [CommandOption("--role-id <ID>")]
    [Description("Role ID to assign")]
    public int? RoleId { get; set; }
}

[CommandGuide("Adds the person to this project with the role given by role_id, then emails them an invitation to confirm their account. Always pass role_id from `roles_list`: the API rejects an invite without an existing role, and administrator roles can't be given this way. Check `users_list` first so nobody is invited twice.")]
public class UsersInviteCommand : BaseCommand<UserInviteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UserInviteSettings settings)
    {
        try
        {
            var client = GetClient();
            UserResponse? user = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Creating user and sending invite to {settings.Email}..."), async _ =>
                {
                    user = await client.CreateUserAsync(new CreateUserRequest(
                        settings.FirstName,
                        settings.LastName,
                        settings.Email,
                        settings.RoleId
                    ));
                });

            Renderer.Success($"User [#F97316]{Markup.Escape(settings.Email)}[/] created and invitation sent.");
            if (user is not null)
            {
                Renderer.KeyValue("ID", user.Id.ToString());
                Renderer.KeyValue("Name", $"{user.FirstName} {user.LastName}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── users resend-invite ───────────────────────────────────────────────────────

public class UserResendInviteSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("User ID to send a fresh invitation to")]
    public int Id { get; set; }
}

[CommandGuide("Emails an existing user a new invitation, which replaces the link in any earlier invitation email. `users_invite` already sends one, so use this only when the person says it never arrived or the link expired.")]
public class UsersResendInviteCommand : BaseCommand<UserResendInviteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UserResendInviteSettings settings)
    {
        try
        {
            await GetClient().SendInvitationAsync(settings.Id);
            Renderer.Success($"Invitation sent to user [#F97316]{settings.Id}[/].");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── users delete ──────────────────────────────────────────────────────────────

public class UserDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("User ID to delete")]
    public int Id { get; set; }

    [CommandOption("--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

[CommandGuide("Removes the user from this project, so they can no longer sign in to it or use their role's access. The role and its permissions stay in place for everyone else. Make sure you have the right person with `users_get` first.")]
public class UsersDeleteCommand : BaseCommand<UserDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UserDeleteSettings settings)
    {
        if (!settings.Yes)
        {
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Delete user[/] [bold red]{settings.Id}[/][yellow]?[/]",
                defaultValue: false);
            if (!confirm)
            {
                Renderer.Info("Cancelled.");
                return 0;
            }
        }

        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Deleting user {settings.Id}..."), async _ =>
                {
                    await client.DeleteUserAsync(settings.Id);
                });

            Renderer.Success($"User [#F97316]{settings.Id}[/] deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
