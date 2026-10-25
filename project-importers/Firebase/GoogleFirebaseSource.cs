using System.Net;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Storage.V1;

namespace Anythink.ProjectImporters.Firebase;

internal sealed class GoogleFirebaseSource(FirebaseServiceAccountKey key, string? bucket) : IFirebaseSource
{
    internal static readonly string[] ReadOnlyStorageScopes = ["https://www.googleapis.com/auth/devstorage.read_only"];

    private static readonly TimeSpan SignedUrlLifetime = TimeSpan.FromMinutes(15);

    private readonly Lazy<ServiceAccountCredential> _account = new(() =>
        CredentialFactory.FromJson<ServiceAccountCredential>(key.Json));

    private FirestoreDb Db => _db ??= new FirestoreDbBuilder { ProjectId = key.ProjectId, GoogleCredential = _account.Value.ToGoogleCredential() }.Build();

    private FirestoreDb? _db;
    private string? _resolvedBucket = bucket;

    public string ProjectId => key.ProjectId;

    public async Task<IReadOnlyList<string>> ListCollectionIdsAsync()
    {
        var ids = new List<string>();
        await foreach (var collection in Db.ListRootCollectionsAsync())
            ids.Add(collection.Id);
        return ids;
    }

    public async Task<IReadOnlyList<FirebaseDocument>> SampleDocumentsAsync(string collection, int limit)
    {
        var snapshot = await Db.Collection(collection).Limit(limit).GetSnapshotAsync();
        var documents = new List<FirebaseDocument>();
        foreach (var doc in snapshot.Documents)
        {
            var subcollections = new List<string>();
            await foreach (var sub in doc.Reference.ListCollectionsAsync())
                subcollections.Add(sub.Id);
            documents.Add(ToDocument(doc, subcollections));
        }
        return documents;
    }

    public async Task<IReadOnlyList<FirebaseDocument>> ReadDocumentsAsync(string collection, string? afterPath, int limit)
    {
        var query = Db.Collection(collection).OrderBy(FieldPath.DocumentId).Limit(limit);
        if (afterPath is not null) query = query.StartAfter(Db.Document(afterPath));
        return (await query.GetSnapshotAsync()).Documents.Select(d => ToDocument(d, [])).ToList();
    }

    public async Task<IReadOnlyList<string>> ListObjectNamesAsync()
    {
        var storage = StorageClient.Create(_account.Value.ToGoogleCredential().CreateScoped(ReadOnlyStorageScopes));
        var candidates = bucket is not null
            ? new[] { bucket }
            : new[] { $"{key.ProjectId}.firebasestorage.app", $"{key.ProjectId}.appspot.com" };

        foreach (var candidate in candidates)
        {
            try
            {
                var objects = new List<string>();
                await foreach (var o in storage.ListObjectsAsync(candidate))
                    objects.Add(o.Name);
                _resolvedBucket = candidate;
                return objects;
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound && candidate != candidates[^1]) { }
        }
        throw new ImportException("No Cloud Storage bucket was found; pass the bucket name explicitly.");
    }

    public string SignDownloadUrl(string objectName)
    {
        var bucketName = _resolvedBucket ?? throw new ImportException("Files must be listed before they can be downloaded.");
        return UrlSigner.FromCredential(_account.Value).Sign(bucketName, objectName, SignedUrlLifetime, HttpMethod.Get);
    }

    public async Task<FirebaseUserCounts> CountUsersAsync()
    {
        var app = FirebaseApp.Create(
            new AppOptions { Credential = _account.Value.ToGoogleCredential(), ProjectId = key.ProjectId },
            $"anythink-import-{Guid.NewGuid():N}");
        try
        {
            int total = 0, disabled = 0;
            await foreach (var u in FirebaseAuth.GetAuth(app).ListUsersAsync(null))
            {
                total++;
                if (u.Disabled) disabled++;
            }
            return new FirebaseUserCounts(total, disabled);
        }
        finally { app.Delete(); }
    }

    internal static string RelativePath(string fullPath)
    {
        const string marker = "/documents/";
        var at = fullPath.IndexOf(marker, StringComparison.Ordinal);
        return at < 0 ? fullPath : fullPath[(at + marker.Length)..];
    }

    private static FirebaseDocument ToDocument(DocumentSnapshot doc, IReadOnlyList<string> subcollections) =>
        new(RelativePath(doc.Reference.Path), doc.ToDictionary().ToDictionary(kv => kv.Key, kv => Normalise(kv.Value)), subcollections);

    internal static object? Normalise(object? value) => value switch
    {
        Timestamp t => t.ToDateTimeOffset(),
        GeoPoint g => new FirebaseGeoPoint(g.Latitude, g.Longitude),
        DocumentReference r => new FirebaseReference(RelativePath(r.Path)),
        Blob b => b.ByteString.ToByteArray(),
        IDictionary<string, object> map => map.ToDictionary(kv => kv.Key, kv => Normalise(kv.Value)),
        string s => s,
        System.Collections.IEnumerable list when value is not byte[] => list.Cast<object?>().Select(Normalise).ToList(),
        _ => value
    };
}
