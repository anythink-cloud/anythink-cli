namespace Anythink.ProjectImporters.Firebase;

internal record FirebaseReference(string Path);

internal record FirebaseGeoPoint(double Latitude, double Longitude);

internal record FirebaseDocument(
    string Path,
    IReadOnlyDictionary<string, object?> Fields,
    IReadOnlyList<string> Subcollections);

internal record FirebaseUserCounts(int Total, int Disabled);

// Read-only by design: nothing here can change the Firebase project.
internal interface IFirebaseSource
{
    string ProjectId { get; }
    Task<IReadOnlyList<string>> ListCollectionIdsAsync();
    Task<IReadOnlyList<FirebaseDocument>> SampleDocumentsAsync(string collection, int limit);
    Task<IReadOnlyList<FirebaseDocument>> ReadDocumentsAsync(string collection, string? afterPath, int limit);
    Task<IReadOnlyList<string>> ListObjectNamesAsync();
    string SignDownloadUrl(string objectName);
    Task<FirebaseUserCounts> CountUsersAsync();
}
