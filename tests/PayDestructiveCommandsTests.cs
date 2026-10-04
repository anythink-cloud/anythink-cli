using AnythinkCli.Commands;
using FluentAssertions;
using Spectre.Console.Cli;
using System.Reflection;

namespace AnythinkCli.Tests;

// Rule: every pay command that deletes, expires, relinks or cancels must confirm by default and accept -y|--yes.
public class PayDestructiveCommandsTests
{
    [Fact]
    public void PlanDelete_RequiresConfirmationByDefault()
        => new PayPlansDeleteSettings().SkipConfirm.Should().BeFalse();

    [Fact]
    public void SubscriptionDeleteForceExpireAndCancel_RequireConfirmationByDefault()
        => new PaySubscriptionsConfirmSettings().SkipConfirm.Should().BeFalse();

    [Fact]
    public void SubscriptionRelink_RequiresConfirmationByDefault()
        => new PaySubscriptionsRelinkSettings().SkipConfirm.Should().BeFalse();

    [Theory]
    [InlineData(typeof(PayPlansDeleteSettings), nameof(PayPlansDeleteSettings.SkipConfirm))]
    [InlineData(typeof(PaySubscriptionsConfirmSettings), nameof(PaySubscriptionsConfirmSettings.SkipConfirm))]
    [InlineData(typeof(PaySubscriptionsRelinkSettings), nameof(PaySubscriptionsRelinkSettings.SkipConfirm))]
    public void ConfirmationFlag_IsBoundToDashYAndDashDashYes(Type settings, string property)
    {
        var option = settings.GetProperty(property)!.GetCustomAttribute<CommandOptionAttribute>()!;

        option.ShortNames.Should().Contain("y");
        option.LongNames.Should().Contain("yes");
    }

    [Fact]
    public void ConfirmOrExit_WithYes_ProceedsWithoutPrompting()
        => PayHelpers.ConfirmOrExit(skipConfirm: true, "Delete?", interactive: false).Should().BeNull();

    [Fact]
    public void ConfirmOrExit_NonInteractiveWithoutYes_RefusesWithNonZeroExit()
        => PayHelpers.ConfirmOrExit(skipConfirm: false, "Delete?", interactive: false).Should().Be(1);
}
