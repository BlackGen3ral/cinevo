using Cinevo;
if (args.Length == 3 && args[0] == "--live-covers") { await LiveCoverCheck.Run(args[1], args[2]); return; }
static void Assert(bool condition, string reason) { if (!condition) throw new Exception(reason); }
var parsed = Scanner.Parse("Duna.Parte.Dois.2024.1080p.BluRay.mkv");
Assert(parsed.Title == "Duna Parte Dois" && parsed.Year == "2024", "Título e ano com tags de release");
Assert(Scanner.Parse("Filme_sem_ano.720p.mp4").Title == "Filme sem ano", "Título sem ano");
Assert(Scanner.Parse("2001.A.Space.Odyssey.1968.mkv") == ("2001 A Space Odyssey", "1968"), "Título numérico preservado");
var root = Path.Combine(Path.GetTempPath(), "Cinevo-tests-" + Guid.NewGuid());
Directory.CreateDirectory(Path.Combine(root, "sub"));
try {
 File.WriteAllText(Path.Combine(root, "Teste.2023.MKV"), "movie");
 File.WriteAllText(Path.Combine(root, "sub", "Outro.mp4"), "movie");
 File.WriteAllText(Path.Combine(root, "ignorar.txt"), "text");
 var result = Scanner.Scan(new[] { root, Path.Combine(root, "sub") }, CancellationToken.None);
 Assert(result.Movies.Count == 2, "Pastas aninhadas não duplicam filmes; arquivos não-vídeo ignorados");
 Assert(result.Movies.All(m => m.Bytes == 5), "Tamanho real do arquivo");
 Assert(Scanner.Scan(new[] { Path.Combine(root, "missing") }, CancellationToken.None).Errors.Count == 1, "Pasta indisponível reportada");
 using var cancel = new CancellationTokenSource(); cancel.Cancel();
 try { Scanner.Scan(new[] { root }, cancel.Token); throw new Exception("Cancelamento não respeitado"); } catch (OperationCanceledException) { }
 var json = System.Text.Json.JsonSerializer.Serialize(new Library { Movies = [ new Movie { Title = "Teste", Favorite = true, Watched = true, Rating = 5, Notes = "Ótimo" } ] });
 var restored = System.Text.Json.JsonSerializer.Deserialize<Library>(json)!;
 Assert(restored.Movies[0].Favorite && restored.Movies[0].Watched && restored.Movies[0].Rating == 5 && restored.Movies[0].Notes == "Ótimo", "Metadados persistem");
 await UpdateTests.Run(root);
 await CoverTests.Run(root);
 Environment.SetEnvironmentVariable("CINEVO_TEST_DATA", Path.Combine(root, "profile"));
 var persisted = new Library { Movies = [new Movie { Title = "Persistência", ResumeMilliseconds = 45000, DurationMilliseconds = 90000, CoverSource = "https://pt.wikipedia.org/wiki/Teste" }] };
 persisted.Save(); persisted.Movies[0].Notes = "Segunda gravação"; persisted.Save();
 var loaded = Library.Load();
 Assert(loaded.Movies[0].ResumeMilliseconds == 45000 && loaded.Movies[0].Progress == 50 && File.Exists(Library.DataPath + ".bak"), "Progresso e backup preservados em gravação atômica");
 File.WriteAllText(Library.DataPath, "{invalid");
 try { Library.Load(); throw new Exception("Biblioteca corrompida aceita"); } catch (InvalidDataException) { }
 Assert(File.ReadAllText(Library.DataPath) == "{invalid", "Catálogo corrompido deve ser preservado");
 Console.WriteLine("PASS: títulos, indexação recursiva, deduplicação, formatos, tamanho, disco ausente, cancelamento e persistência.");
} finally { Environment.SetEnvironmentVariable("CINEVO_TEST_DATA", null); Directory.Delete(root, true); }
