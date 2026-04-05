using AnythinkCli.Tests.BulkOperations;

namespace AnythinkCli.Tests;

/// <summary>
/// Simple test runner for bulk operations
/// </summary>
public class TestRunner
{
    public static async Task Main(string[] args)
    {
        try
        {
            await BulkOperationsTests.RunAllTests();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
