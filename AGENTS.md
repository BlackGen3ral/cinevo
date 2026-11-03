# Cinevo

App desktop Windows WPF em português. Preserve o catálogo e os vídeos do usuário. Nunca envie vídeos, caminhos pessoais ou library.json ao GitHub.

O proprietário autorizou publicar este projeto em https://github.com/BlackGen3ral/cinevo e publicar cada melhoria solicitada como nova versão para atualização automática.

Em cada melhoria: implemente, execute scripts/build.ps1, verifique o app, incremente a versão semântica em Cinevo.csproj, scripts/build.ps1 e installer/Cinevo.iss, faça commit e push em main e publique uma tag vX.Y.Z. Acompanhe o workflow até a release conter Cinevo-Setup.exe e SHA256SUMS.txt. Não reutilize tags/releases de versões publicadas.

Runtime .NET 10. Sem dependências NuGet externas. Dados em %LOCALAPPDATA%\Cinevo, com gravação atômica e backup. Use virtualização na lista e indexação fora da thread de interface. Atualizador só aceita assets da release oficial e verifica SHA-256. Não habilite atualização em builds de desenvolvimento.

O app está em Beta por pedido do proprietário. Exiba o número e a indicação Beta juntos (por exemplo Cinevo 0.1.0 · Beta). Tags usam v0.1.0-beta.1; incremente o componente numérico do núcleo em cada melhoria (0.1.1-beta.2 etc.), pois o atualizador compara esse núcleo. Releases devem ser prerelease. Não promover para 1.0.0 sem pedido. Atualize também o link de download no README.

O SDK local pode estar em .tools/dotnet/dotnet.exe. O compilador local Inno Setup pode estar em .tools/inno/ISCC.exe. Ambos são ignorados pelo Git.

O player usa LibVLC 3.0.24 via interoperabilidade nativa, sem NuGet. scripts/prepare-vlc.ps1 baixa os binários oficiais e os fontes correspondentes com hashes fixos. Preserve as licenças e publique o asset vlc-3.0.24-source.tar.xz junto ao instalador. scripts/build.ps1 inclui teste real de vídeo, áudio e legenda no LibVLC.

Capas usam a API MediaWiki/Wikipédia por título e ano, com cache, fonte acessível e opção de corrigir correspondência. Não inclua capas ou vídeos nos arquivos versionados. Para QA visual, use CINEVO_TEST_DATA apontando para .tools/qa-profile; esse perfil e a instância ficam separados do catálogo instalado. Nunca sobrescreva o catálogo real para preparar demonstrações.
