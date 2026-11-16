using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Cinevo;

public partial class MainWindow
{
    private async Task<object> ImportTmdbAsync(string address)
    {
        var route = TmdbClient.RouteFromUrl(address)
            ?? throw new InvalidDataException("Abra um filme, série ou temporada no TMDB e cole o endereço dessa ficha. A página inicial do TMDB é um catálogo.");
        var token = lifetime.Token;
        try
        {
            var credential = TmdbClient.ReadCredential();
            if (credential.Length == 0) throw new InvalidDataException("Conecte sua conta em ⋯ → Configurar TMDB para importar uma ficha do TMDB.");
            using var client = new TmdbClient(credential);
            var details = await client.LookupLinkedAsync(route, token);
            if (string.IsNullOrWhiteSpace(details.Title)) throw new InvalidDataException("A ficha do TMDB não tem um título.");
            var movie = library.Movies.FirstOrDefault(item => item.IsTmdbImport && TmdbClient.RouteFromUrl(item.TmdbUrl) == route);
            if (movie != null)
                return new { count = 1, message = "Esta ficha já está em Filmes importados.", local = true };
            var cover = "";
            try { cover = await client.DownloadPosterAsync(details.Poster, route, token); }
            catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException) { }
            // The classic import dialog and the web interface can run independently.
            if (library.Movies.Any(item => item.IsTmdbImport && TmdbClient.RouteFromUrl(item.TmdbUrl) == route))
                return new { count = 1, message = "Esta ficha já está em Filmes importados.", local = true };
            movie = new Movie
            {
                Title = details.Title, Year = details.Year, ImportedOnly = true, IsTmdbImport = true,
                SourceUrl = details.Url, ImportedFrom = details.Url, TmdbUrl = details.Url,
                UserScore=details.Score, DurationMilliseconds=details.Minutes*60000L, Description = details.Overview, Genre = string.Join(" · ", details.Genres), Cover = cover,
                CoverSource = details.Url, MediaType = route.StartsWith("tv/", StringComparison.Ordinal) ? "series" : "movie"
            };
            library.Movies.Add(movie);
            try { library.Save(); }
            catch { library.Movies.Remove(movie); throw; }
            Refresh();
            return new { count = 1, message = "Ficha do TMDB adicionada a Filmes importados.", local = true };
        }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            throw new InvalidDataException("Não foi possível consultar a API do TMDB ou salvar a ficha neste PC.");
        }
    }
}
