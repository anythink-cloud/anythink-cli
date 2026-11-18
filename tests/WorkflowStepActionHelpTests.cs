using System.ComponentModel;
using System.Reflection;
using AnythinkCli.Commands;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class WorkflowStepActionHelpTests
{
    [Fact]
    public void StepAddActionHelp_ListsAdjustBalance()
    {
        var help = typeof(WorkflowStepAddSettings).GetProperty(nameof(WorkflowStepAddSettings.Action))!
            .GetCustomAttribute<DescriptionAttribute>()!.Description;

        help.Should().Contain("AdjustBalance");
    }
}
