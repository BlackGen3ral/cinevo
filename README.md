# Cinevo

**Seu cinema, em casa.** Gerenciador de filmes locais para Windows, com interface em português, instalador simples e atualizações pelo GitHub.

[Baixar o instalador](https://github.com/BlackGen3ral/cinevo/releases/latest/download/Cinevo-Setup.exe) · [Versões](https://github.com/BlackGen3ral/cinevo/releases)

<img src="Cinevo/Assets/cinevo.png" width="96" alt="Ícone Cinevo: letra C violeta com símbolo de reprodução" />

## Começar

1. Baixe e execute **Cinevo-Setup.exe**.
2. Conclua a instalação. Não precisa instalar .NET nem ter acesso de administrador.
3. Abra o Cinevo e clique em **Adicionar pasta**. Escolha as pastas dos seus filmes, inclusive em discos externos.
4. Use **Assistir** para abrir um vídeo no player padrão do Windows. Em **Detalhes**, edite título, ano, gênero, capa, notas, avaliação e os estados de favorito/assistido.

Requisitos: Windows 10/11 x64. A reprodução depende de um player instalado que suporte o formato escolhido. O aplicativo não inclui codecs ou player próprio.

## Recursos

- Indexação recursiva em segundo plano, cancelável e sem duplicatas por caminho.
- Reconhecimento de títulos e anos a partir do nome do arquivo.
- Busca por título, ano, gênero e caminho; filtros para favoritos e assistidos.
- Ordenação por título, data de adição, ano e avaliação.
- Capas escolhidas do computador, anotações e avaliações de 1 a 5 estrelas.
- Lista virtualizada para evitar criar todos os elementos visuais de grandes coleções de uma só vez.
- Discos ausentes mantêm seus registros e preferências no catálogo.
- Backup exportável e cópia automática da última gravação do catálogo.
- Instalador por usuário, atalhos e desinstalador.

O Cinevo não move, apaga ou envia seus vídeos. Não busca cartazes ou sinopses na internet. Filmes com arquivos separados aparecem como registros separados. O status de assistido é marcado manualmente, pois a reprodução ocorre em um player externo.

## Atualizações

A versão instalada verifica a release mais recente do GitHub ao abrir. Se houver uma versão superior, baixa o instalador, valida seu SHA-256 e instala ao fechar o app. Você também pode usar **Instalar atualização**. É possível desativar esse comportamento em **Configurações**.

Sem conexão, o catálogo continua funcionando. A primeira versão não tem certificado de assinatura de código; o Windows pode exibir uma mensagem de editor desconhecido. A verificação SHA-256 detecta corrupção do download, mas não substitui assinatura de código.

## Seus dados

Catálogo: `%LOCALAPPDATA%\Cinevo\library.json`.

Cópia da gravação anterior: `library.json.bak`. Capas copiadas para a subpasta `covers`. O desinstalador preserva esse diretório para permitir reinstalação sem perder sua coleção. Para restaurar uma cópia, feche o app e substitua `library.json` pelo JSON exportado. Não publique o catálogo: ele contém caminhos locais e suas anotações.

## Desenvolvimento e publicação

WPF, C# e .NET 10. Sem dependências NuGet de terceiros. O runtime é incluído no instalador.

```powershell
dotnet run --project tests/Cinevo.Tests.csproj -c Release
./scripts/build.ps1 -Version 1.0.0
```

Pré-requisitos de compilação: .NET SDK 10 e Inno Setup 6 ou 7. Para um compilador fora do local padrão, passe `-InnoCompiler C:\caminho\ISCC.exe`. `scripts/icon.ps1` regenera o ícone ICO em seis tamanhos e a imagem PNG.

Cada melhoria deve incrementar a versão, passar pelos testes e receber uma nova tag `vX.Y.Z`. O workflow `.github/workflows/release.yml` compila no Windows e publica `Cinevo-Setup.exe` e `SHA256SUMS.txt`. O app instalado recebe essa release na próxima verificação. Veja `AGENTS.md` para a autorização permanente e as regras de publicação deste projeto.

## Licença

Código do Cinevo sob MIT. O runtime .NET e o Inno Setup têm suas próprias licenças, disponíveis nos projetos oficiais: [dotnet](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) e [Inno Setup](https://jrsoftware.org/isinfo.php).
