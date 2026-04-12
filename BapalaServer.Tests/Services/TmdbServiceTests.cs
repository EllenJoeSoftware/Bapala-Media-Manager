using System.Net;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using BapalaServer.Models;
using BapalaServer.Services;
using Xunit;

namespace BapalaServer.Tests.Services;

public class TmdbServiceTests
{
    private static ITmdbService CreateService(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bapala:TmdbApiKey"] = "test_key" })
            .Build();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Returns(new HttpClient(handler));
        return new TmdbService(config, factory.Object);
    }

    [Fact]
    public async Task FetchMetadataAsync_ReturnsNullWhenNoApiKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bapala:TmdbApiKey"] = "" })
            .Build();
        var factory = new Mock<IHttpClientFactory>();
        var svc = new TmdbService(config, factory.Object);

        var result = await svc.FetchMetadataAsync("Inception", 2010, MediaType.Movie);
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchMetadataAsync_ParsesMovieResponse()
    {
        var searchJson = """
            {"results":[{"id":27205,"title":"Inception","overview":"A thief...","vote_average":8.3,
             "release_date":"2010-07-16","genre_ids":[28,878],"poster_path":"/poster.jpg","backdrop_path":"/back.jpg"}]}
            """;
        var detailJson = """{"id":27205,"genres":[{"name":"Action"},{"name":"Science Fiction"}]}""";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .SetupSequence<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(searchJson, System.Text.Encoding.UTF8, "application/json") })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(detailJson, System.Text.Encoding.UTF8, "application/json") });

        var svc = CreateService(handlerMock.Object);
        var meta = await svc.FetchMetadataAsync("Inception", 2010, MediaType.Movie);

        Assert.NotNull(meta);
        Assert.Equal(27205, meta!.TmdbId);
        Assert.Contains("Action", meta.Genres);
        Assert.Equal(8.3, meta.Rating!.Value, 1);
    }
}
