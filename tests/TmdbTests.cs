using System.Net;
using Cinevo;

internal static class TmdbTests
{
    public static async Task Run()
    {
        using var handler = new Fixture();
        using var client = new TmdbClient("eyabcdefgh.abcdefghi.abcdefghijkl", handler);
        var movie = await client.LookupAsync("Filme de Teste", "2024", CancellationToken.None);
        if (movie is not { Overview: "Sinopse em português.", Score: 8.2, Votes: 31, TrailerId: "BRabcdefgh0" } ||
            movie.Cast.Single().Role != "Protagonista" || movie.Directors.Single() != "Pessoa Diretora" ||
            movie.Writers.Single() != "Pessoa Roteirista")
            throw new Exception("Ficha, créditos ou trailer TMDB incorretos.");
        if (await client.LookupAsync("Filme de Teste", "1999", CancellationToken.None) != null)
            throw new Exception("O TMDB associou um filme com ano incorreto.");
        var linked = TmdbClient.RouteFromUrl("https://www.themoviedb.org/movie/41-filme-teste?language=pt-BR");
        if (linked != "movie/41" || TmdbClient.RouteFromUrl("https://example.org/movie/41") != null ||
            (await client.LookupLinkedAsync(linked, CancellationToken.None)).Votes != 31 ||
            TmdbClient.RouteFromUrl("https://themoviedb.org/tv/42-serie/season/2") != "tv/42/season/2")
            throw new Exception("O vínculo direto do TMDB precisa aceitar a ficha certa e rejeitar outros domínios.");
        handler.Series = true;
        var season = await client.LookupAsync("Série de Teste 2ª Temporada", "2025", CancellationToken.None);
        if (season is not { Overview: "Sinopse da temporada.", TrailerId: "ENabcdefgh0" } ||
            season.Creators.Single() != "Pessoa Criadora" || !season.Url.Contains("/tv/42/season/2?"))
            throw new Exception("Temporada, criação ou trailer alternativo TMDB incorretos.");
        Console.WriteLine("PASS: TMDB pt-BR, nota, créditos, ano, temporada e trailers do YouTube.");
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public bool Series { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "api.themoviedb.org" || request.Headers.Authorization?.Scheme != "Bearer")
                throw new Exception("Destino ou autenticação do TMDB incorretos.");
            var data = uri.AbsolutePath switch
            {
                "/3/search/multi" when Series => """{"results":[{"id":42,"media_type":"tv","name":"Série de Teste","first_air_date":"2022-02-01"}]}""",
                "/3/search/multi" => """{"results":[{"id":41,"media_type":"movie","title":"Filme de Teste","release_date":"2024-02-01"},{"id":90,"media_type":"movie","title":"Filme de Teste","release_date":"2012-02-01","vote_count":900}]}""",
                "/3/movie/41" => """
                    {"overview":"Sinopse em português.","vote_average":8.2,"vote_count":31,"runtime":106,
                     "credits":{"cast":[{"name":"Pessoa Atriz","character":"Protagonista"}],
                        "crew":[{"name":"Pessoa Diretora","job":"Director"},{"name":"Pessoa Roteirista","job":"Screenplay"}]},
                     "videos":{"results":[{"site":"Vimeo","type":"Trailer","key":"VIabcdefgh0"},
                        {"site":"YouTube","type":"Trailer","key":"ENabcdefgh0","official":true,"iso_639_1":"en"},
                        {"site":"YouTube","type":"Trailer","key":"BRabcdefgh0","official":true,"iso_639_1":"pt","iso_3166_1":"BR"}]}}
                    """,
                "/3/tv/42" => """{"created_by":[{"name":"Pessoa Criadora"}],"overview":"Sinopse da série."}""",
                "/3/tv/42/season/2" => """{"overview":"Sinopse da temporada.","videos":{"results":[]}}""",
                "/3/tv/42/season/2/videos" when uri.Query.Contains("language=en-US") =>
                    """{"results":[{"site":"YouTube","type":"Trailer","key":"ENabcdefgh0"}]}""",
                _ => throw new Exception("Consulta TMDB inesperada: " + uri.AbsolutePath)
            };
            if (!uri.AbsolutePath.EndsWith("/videos") && !uri.Query.Contains("language=pt-BR"))
                throw new Exception("A consulta deve solicitar português do Brasil.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(data) });
        }
    }
}
