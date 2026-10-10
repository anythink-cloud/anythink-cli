using System.Net;
using AnythinkCli.Client;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class FileUploadTests
{
    private const string UploadUrl = "https://api.example.com/org/99999/files";
    private const string FileJson = """{"id":9,"original_file_name":"upload.bin","file_name":"stored.bin","file_type":"application/octet-stream","file_size":6,"is_public":false,"created_at":"2024-01-01T00:00:00Z"}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_SendsTheOriginalBytesAndMultipartMetadata(bool isPublic)
    {
        using var file = new TempUploadFile();
        using var http = new HttpClient(new UploadHandler(async (request, cancellation) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.ToString().Should().Be($"{UploadUrl}?isPublic={isPublic.ToString().ToLowerInvariant()}");
            var form = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject;
            form.Headers.ContentLength.Should().BeGreaterThan(file.Bytes.Length);
            var part = form.Should().ContainSingle().Subject;
            part.Headers.ContentDisposition!.Name!.Trim('"').Should().Be("file");
            part.Headers.ContentDisposition.FileName!.Trim('"').Should().Be(Path.GetFileName(file.Path));
            part.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
            part.Headers.ContentLength.Should().Be(file.Bytes.Length);
            (await part.ReadAsByteArrayAsync(cancellation)).Should().Equal(file.Bytes);
            return Response(HttpStatusCode.OK, FileJson);
        }));

        var uploaded = await Client(http).UploadFileAsync(file.Path, isPublic);

        uploaded.Id.Should().Be(9);
        file.ShouldBeReleased();
    }

    [Fact]
    public async Task Upload_RejectedByTheServer_ClosesTheFileAndKeepsTheApiError()
    {
        using var file = new TempUploadFile();
        using var http = new HttpClient(new UploadHandler((_, _) =>
            Task.FromResult(Response(HttpStatusCode.RequestEntityTooLarge, "File too large"))));

        var act = () => Client(http).UploadFileAsync(file.Path);

        await act.Should().ThrowAsync<AnythinkException>()
            .Where(error => error.StatusCode == 413 && error.Message == "File too large");
        file.ShouldBeReleased();
    }

    [Fact]
    public async Task Upload_TransportFailure_ClosesTheFile()
    {
        using var file = new TempUploadFile();
        using var http = new HttpClient(new UploadHandler((_, _) => throw new HttpRequestException("Connection reset")));

        var act = () => Client(http).UploadFileAsync(file.Path);

        await act.Should().ThrowAsync<HttpRequestException>();
        file.ShouldBeReleased();
    }

    [Fact]
    public async Task Upload_CancellationReachesHttpAndClosesTheFile()
    {
        using var file = new TempUploadFile();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var receivedCancelledToken = false;
        using var http = new HttpClient(new UploadHandler((_, token) =>
        {
            receivedCancelledToken = token.IsCancellationRequested;
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Response(HttpStatusCode.OK, FileJson));
        }));
        var previous = ClientContext.Cancellation;
        ClientContext.Cancellation = cancellation.Token;
        try
        {
            var act = () => Client(http).UploadFileAsync(file.Path);

            await act.Should().ThrowAsync<OperationCanceledException>();
            receivedCancelledToken.Should().BeTrue();
            file.ShouldBeReleased();
        }
        finally
        {
            ClientContext.Cancellation = previous;
        }
    }

    private static AnythinkClient Client(HttpClient http) => new("99999", "https://api.example.com", http);

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private sealed class UploadHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class TempUploadFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetTempFileName();
        public byte[] Bytes { get; } = [0, 1, 13, 10, 128, 255];

        public TempUploadFile() => File.WriteAllBytes(Path, Bytes);

        public void ShouldBeReleased()
        {
            using var reopened = File.Open(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            reopened.Length.Should().Be(Bytes.Length);
        }

        public void Dispose() => File.Delete(Path);
    }
}
