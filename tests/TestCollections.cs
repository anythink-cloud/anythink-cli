namespace AnythinkCli.Tests;

/// <summary>
/// All test classes that touch ConfigService.ConfigDirOverride (a static property)
/// must belong to this collection so xUnit runs them sequentially, preventing
/// race conditions when tests execute in parallel.
/// </summary>
[CollectionDefinition("SequentialConfig")]
public class SequentialConfigCollection { }
