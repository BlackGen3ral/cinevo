# Cinevo

App desktop Windows WPF em português. Preserve o catálogo e os vídeos do usuário. Nunca envie vídeos, caminhos pessoais ou library.json ao GitHub.

O proprietário autorizou publicar este projeto em https://github.com/BlackGen3ral/cinevo e publicar cada melhoria solicitada como nova versão para atualização automática.

Em cada melhoria: implemente, execute scripts/build.ps1, verifique o app, incremente a versão semântica em Cinevo.csproj, scripts/build.ps1 e installer/Cinevo.iss, faça commit e push em main e publique uma tag vX.Y.Z. Acompanhe o workflow até a release conter Cinevo-Setup.exe e SHA256SUMS.txt. Não reutilize tags/releases de versões publicadas.

Runtime .NET 10. Sem dependências NuGet externas. Dados em %LOCALAPPDATA%\Cinevo, com gravação atômica e backup. Use virtualização na lista e indexação fora da thread de interface. Atualizador só aceita assets da release oficial e verifica SHA-256. Não habilite atualização em builds de desenvolvimento.

O SDK local pode estar em .tools/dotnet/dotnet.exe. O compilador local Inno Setup pode estar em .tools/inno/ISCC.exe. Ambos são ignorados pelo Git.
