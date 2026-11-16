using System.Net;
using System.Text.Json;
using Cinevo;

internal static class TmdbTests
{
    public static async Task Run()
    {
        using var handler = new Fixture();
        using var client = new TmdbClient("eyabcdefgh.abcdefghi.abcdefghijkl", handler);
        var people=await client.PeopleAsync("Pessoa Atriz",CancellationToken.None);
        var credits=await client.PersonCreditsAsync(7,CancellationToken.None);
        if(people.Single() is not {Id:7,Name:"Pessoa Atriz"}||credits.Length!=2||credits[0].Route!="movie/41"||credits[1].Route!="tv/42")throw new Exception("Busca de pessoas ou créditos TMDB incorretos");
        var movie = await client.LookupAsync("Filme de Teste", "2024", CancellationToken.None);
        if (movie is not { Overview: "Sinopse em português.", Score: 8.2, Votes: 31, TrailerId: "BRabcdefgh0" } ||
            movie.Cast.Single().Role != "Protagonista" || movie.Directors.Single() != "Pessoa Diretora" ||
            movie.Writers.Single() != "Pessoa Roteirista")
            throw new Exception("Ficha, créditos ou trailer TMDB incorretos.");
        if (await client.LookupAsync("Filme de Teste", "1999", CancellationToken.None) != null)
            throw new Exception("O TMDB associou um filme com ano incorreto.");
        if(movie.TrailerIds is not {Length:2}||movie.TrailerIds[0]!="BRabcdefgh0"||movie.TrailerIds[1]!="ENabcdefgh0")throw new Exception("Prévias alternativas do mesmo título devem ser preservadas e ordenadas.");
        handler.Numbered=true;
        if(await client.LookupAsync("Missão: Impossível 6 – Efeito Fallout","2018",CancellationToken.None) is not {TrailerId:"FRabcdefgh0"}||
            await client.LookupAsync("Missão: Impossível 6 – Efeito Fallout","2017",CancellationToken.None)!=null)
            throw new Exception("A variação numerada deve manter o subtítulo e conferir o ano.");
        handler.Numbered=false;
        using(var videos=JsonDocument.Parse("""{"videos":{"results":[{"site":"YouTube","type":"Clip","key":"CLabcdefgh0"},{"site":"YouTube","type":"Teaser","key":"PTabcdefgh0","iso_639_1":"pt","iso_3166_1":"BR"},{"site":"YouTube","type":"Trailer","key":"invalid"},{"site":"YouTube","type":"Trailer","key":"ENabcdefgh0"}]}}"""))
            if(TmdbClient.SelectTrailer(videos.RootElement)!="ENabcdefgh0")throw new Exception("Um trailer válido deve ter prioridade sobre teaser e clipes.");
        var linked = TmdbClient.RouteFromUrl("https://www.themoviedb.org/movie/41-filme-teste?language=pt-BR");
        if (linked != "movie/41" || TmdbClient.RouteFromUrl("https://example.org/movie/41") != null ||
            !TmdbClient.IsSiteUrl("https://www.themoviedb.org/?language=pt-BR") ||
            TmdbClient.RouteFromUrl("https://www.themoviedb.org/?language=pt-BR") != null ||
            await client.LookupLinkedAsync(linked, CancellationToken.None) is not
                { Votes: 31, Title: "Filme de Teste", Year: "2024", Poster: "/filme.jpg", Backdrop:"/panorama.jpg" } ||
            TmdbClient.RouteFromUrl("https://themoviedb.org/tv/42-serie/season/2") != "tv/42/season/2")
            throw new Exception("O vínculo direto do TMDB precisa aceitar a ficha certa e rejeitar outros domínios.");
        handler.Series = true;
        var season = await client.LookupAsync("Série de Teste 2ª Temporada", "2025", CancellationToken.None);
        if (season is not { Title: "Série de Teste Temporada 2", Year: "2025",
                Overview: "Sinopse da temporada.", TrailerId: "ENabcdefgh0" } ||
            season.Creators.Single() != "Pessoa Criadora" || !season.Url.Contains("/tv/42/season/2?"))
            throw new Exception("Temporada, criação ou trailer alternativo TMDB incorretos.");
        handler.ParentVideo=true;
        if(await client.LookupAsync("Série de Teste 2ª Temporada","2025",CancellationToken.None) is not {TrailerId:"PAabcdefgh0"})throw new Exception("Temporada sem prévia deve aproveitar a série identificada.");
        Console.WriteLine("PASS: TMDB pt-BR, créditos, título numerado com ano protegido, idiomas, teasers, prévias alternativas e trailer da série para temporada sem vídeo.");
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public bool Series { get; set; }
        public bool Numbered {get;set;}
        public bool ParentVideo {get;set;}

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "api.themoviedb.org" || request.Headers.Authorization?.Scheme != "Bearer")
                throw new Exception("Destino ou autenticação do TMDB incorretos.");
            var data = uri.AbsolutePath switch
            {
                "/3/search/person" => """{"results":[{"id":7,"name":"Pessoa Atriz","known_for":[{"title":"Filme de Teste"}]}]}""",
                "/3/person/7/combined_credits" => """{"cast":[{"id":41,"media_type":"movie","title":"Filme de Teste","release_date":"2024-01-01"},{"id":42,"media_type":"tv","name":"Série de Teste","first_air_date":"2025-01-01"},{"id":5,"media_type":"person","name":"Ignorar"}]}""",
                "/3/search/multi" when Series => """{"results":[{"id":42,"media_type":"tv","name":"Série de Teste","first_air_date":"2022-02-01"}]}""",
                "/3/search/multi" when Numbered => """{"results":[{"id":353081,"media_type":"movie","title":"Missão: Impossível - Efeito Fallout","release_date":"2018-07-25"}]}""",
                "/3/search/multi" => """{"results":[{"id":41,"media_type":"movie","title":"Filme de Teste","release_date":"2024-02-01"},{"id":90,"media_type":"movie","title":"Filme de Teste","release_date":"2012-02-01","vote_count":900}]}""",
                "/3/movie/41" => """
                    {"title":"Filme de Teste","release_date":"2024-02-01","poster_path":"/filme.jpg","backdrop_path":"/panorama.jpg",
                     "overview":"Sinopse em português.","vote_average":8.2,"vote_count":31,"runtime":106,
                     "credits":{"cast":[{"name":"Pessoa Atriz","character":"Protagonista"}],
                        "crew":[{"name":"Pessoa Diretora","job":"Director"},{"name":"Pessoa Roteirista","job":"Screenplay"}]},
                     "videos":{"results":[{"site":"Vimeo","type":"Trailer","key":"VIabcdefgh0"},
                        {"site":"YouTube","type":"Trailer","key":"ENabcdefgh0","official":true,"iso_639_1":"en"},
                        {"site":"YouTube","type":"Trailer","key":"BRabcdefgh0","official":true,"iso_639_1":"pt","iso_3166_1":"BR"}]}}
                    """,
                "/3/movie/353081" => """{"title":"Missão: Impossível - Efeito Fallout","release_date":"2018-07-25","original_language":"fr","videos":{"results":[{"site":"YouTube","type":"Teaser","key":"TEabcdefgh0"}]}}""",
                "/3/movie/353081/videos" when uri.Query.Contains("include_video_language=pt,en,null,fr") => """{"results":[{"site":"YouTube","type":"Trailer","key":"FRabcdefgh0","iso_639_1":"fr"}]}""",
                "/3/tv/42" when ParentVideo => """{"name":"Série de Teste","videos":{"results":[{"site":"YouTube","type":"Teaser","key":"PAabcdefgh0"}]}}""",
                "/3/tv/42" => """{"name":"Série de Teste","created_by":[{"name":"Pessoa Criadora"}],"overview":"Sinopse da série."}""",
                "/3/tv/42/season/2" => """{"air_date":"2025-04-03","overview":"Sinopse da temporada.","videos":{"results":[]}}""",
                "/3/tv/42/season/2/videos" when ParentVideo => """{"results":[]}""",
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
