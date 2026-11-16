# Cinevo 0.1.48 · Beta

**Seu cinema, em casa.** Gerenciador de filmes locais para Windows, com interface em português, instalador simples e atualizações pelo GitHub.

[Baixar o instalador Beta](https://github.com/BlackGen3ral/cinevo/releases/download/v0.1.48-beta.49/Cinevo-Setup.exe) · [Portal online](https://cinevo-portal.arthurgabrielfera.workers.dev) · [Versões](https://github.com/BlackGen3ral/cinevo/releases)

O Cinevo está em **Beta**: esta é a versão inicial de desenvolvimento, ainda sujeita a ajustes e melhorias.

<img src="Cinevo/Assets/cinevo.png" width="96" alt="Ícone Cinevo: letra C violeta com símbolo de reprodução" />

## Começar

1. Baixe e execute **Cinevo-Setup.exe**.
2. Conclua a instalação. Não precisa instalar .NET nem ter acesso de administrador.
3. Abra o Cinevo e clique em **Adicionar pasta**. Escolha as pastas dos seus filmes, inclusive em discos externos.
4. As capas são buscadas automaticamente pelo título e ano. Use **Buscar capas** para tentar novamente, ou **••• → Buscar / corrigir capa pelo nome** para escolher outro resultado.
5. Use **Assistir** para reproduzir dentro do Cinevo. Em **•••**, edite título, ano, gênero, capa, notas, avaliação e os estados de favorito/assistido.

Requisitos: Windows 10/11 x64. O motor LibVLC e seus módulos já vêm no instalador: não é necessário instalar VLC ou outro player. Arquivos incompletos, protegidos por DRM ou com formatos incomuns ainda podem falhar na reprodução.

## Recursos

A interface usa painéis de vidro, bordas suaves e iluminação ambiente em violeta e rosa. O fundo é feito com gradientes locais e anima apenas transformações; não baixa novas imagens nem grava cache para esse efeito. O movimento pausa quando o app perde o foco, fica oculto ou mostra o player da TV. A intensidade acompanha o brilho do perfil, e a preferência do sistema por movimento reduzido desativa a animação. Navegadores sem suporte ao desfoque recebem painéis sólidos.


- Indexação recursiva em segundo plano, cancelável e sem duplicatas por caminho.
- Reconhecimento de títulos e anos a partir do nome do arquivo.
- Busca por título, ano, gênero e caminho; filtros para favoritos e assistidos.
- Ordenação por título, data de adição, ano e avaliação.
- Aparência por perfil: tamanho das capas, intensidade do brilho no modo controle, volume dos cliques e densidade. Ajuste em Configurações → Aparência e sons.
- Downloads com capa quando identificado no catálogo, progresso, velocidade real e previsão de término quando disponível no qBittorrent.
- Duração, resolução e disponibilidade ao focar ou apontar uma capa. Dados ausentes são indicados.
- Busca em categorias de filmes, séries, episódios e elenco identificado; pessoas no TMDB conectado sob demanda.
- Controles do player recolhem após três segundos sem interação durante a reprodução e reaparecem com mouse, teclado ou controle. Pausa e final mantêm os botões visíveis.
- Interface com grade de pôsteres responsiva, destaque para a próxima sessão e indicação do progresso.
- Capas automáticas da Wikipédia com cache local, correção por busca e imagens escolhidas do computador.
- Player integrado: pausa, volume, busca na linha do tempo, saltos de 10 segundos, tela cheia, faixas de áudio, legendas internas e arquivo de legenda externo.
- Continuação do ponto salvo e marcação de assistido ao atingir o final.
- Grade com virtualização por linhas e decodificação das capas em resolução limitada para reduzir uso de memória.
- Anotações e avaliações de 1 a 5 estrelas.
- Discos ausentes mantêm seus registros e preferências no catálogo.
- Backup exportável e cópia automática da última gravação do catálogo.
- Instalador por usuário, atalhos e desinstalador.

O Cinevo não move, apaga ou envia seus vídeos. A busca de capas envia somente o título e ano à Wikipédia; pode ser desativada em **Configurações**. Seus caminhos e anotações ficam no computador. As capas e seus direitos pertencem aos respectivos titulares; a origem fica acessível nos detalhes. Correspondências são aproximadas e nem todos os filmes terão um pôster disponível. Capas manuais são preservadas. Filmes divididos em arquivos separados aparecem como registros separados.

No player: **Espaço** pausa/continua; **← / →** recuam/avançam 10 segundos; **F** alterna tela cheia; **Esc** sai da tela cheia ou volta à biblioteca; **M** ativa/desativa o som. O progresso é salvo a cada dez segundos e ao fechar o player. A continuação começa após 30 segundos de progresso salvo. No final, o filme é marcado como assistido automaticamente; você pode alterar isso nos detalhes.

## Central TV, downloads e prévias

Na TV pareada, abra **Filmes importados**, escolha um título ou episódio e selecione **Baixar no PC**. O computador consulta novamente a origem e usa sua pasta padrão. Torrents exigem o qBittorrent conectado no PC; para arquivos diretos, sem pasta definida, o destino é Vídeos/Cinevo. Os pedidos são acompanhados em **Downloads no PC** na TV durante a sessão. Não há garantia de disponibilidade de pares ou de arquivos externos. Páginas sem arquivo direto reconhecido não são baixadas automaticamente.

Mantenha o Cinevo aberto no computador para concluir arquivos diretos e disponibilizar os vídeos à TV. Arquivos diretos cancelados removem o parcial; pausar um torrent preserva os dados dele. O app espera a remoção dos parciais ao encerrar normalmente. As solicitações da TV ficam nesta execução do PC; os vídeos concluídos e a biblioteca são preservados em disco.

A linha do tempo e os botões de salto mostram prévias de vídeos locais a cada dez segundos, geradas sob demanda por um decodificador silencioso separado. O cache é global, limitado a **96 imagens / 8 MiB de memória**, sem gravação de miniaturas no disco. Vídeos incompatíveis ou indisponíveis podem ficar sem prévia. A função usa os callbacks de vídeo do [LibVLC 3](https://videolan.videolan.me/vlc-3.0/group__libvlc__media__player.html).

Abra **Diagnóstico da central** no PC ou **Diagnóstico** na TV, inclusive nos controles do player. O PC informa o contato das TVs nos últimos 45 segundos. A TV testa se o PC responde pela rede e informa erros recebidos pelo navegador. Sem resposta, pode haver falha de rede, suspensão, Cinevo fechado ou central desativada. O diagnóstico não presume que o PC está desligado e não garante compatibilidade de codecs com qualquer modelo de TV.

## Atualizações

Em **Filmes importados**, filtre pelo site ou escolha apenas filmes ou séries. As séries ficam reunidas pelo nome dentro de cada site, com temporadas e episódios em ordem quando identificados. Reimportar um catálogo preserva sinopses, capas e links encontrados em importações anteriores quando a listagem fornece menos informações.

O Cinevo instalado acompanha os endereços importados ao iniciar e verifica novidades a cada 30 minutos enquanto permanece aberto e conectado à internet. Os novos títulos chegam a **Filmes importados**. Catálogos grandes são percorridos em lotes, com continuação salva entre sessões e nova tentativa após falhas temporárias. Catálogos já presentes na biblioteca são reconhecidos pelo endereço de origem.

Para repetir uma busca manualmente, abra **Filmes importados → Buscar novamente**, escolha um dos **Endereços adicionados** e clique em **Buscar novamente**. O app mostra o progresso e permite pausar e continuar a importação.

O Cinevo carrega as capas dos títulos importados conforme aparecem na interface. O cache dessas imagens tem limite de 64 MB e descarta as capas menos recentes quando precisa de espaço.

Em sites WordPress que disponibilizam uma listagem pública, importar a página inicial usa essa listagem para percorrer todos os registros, incluindo os títulos antigos que o tema pode omitir nas páginas visíveis. O progresso mostra o total informado pela origem. Os identificadores existentes são preservados ao trocar para essa leitura.

Com o TMDB conectado, **Importar endereço** também aceita o link da ficha de um filme, série ou temporada no TMDB. A ficha é salva no catálogo deste PC e abre a página do TMDB como origem. Para títulos vindos de outros sites, o Cinevo usa o nome sem os rótulos de qualidade e idioma para buscar a ficha.

Para usar o **The Movie Database**, abra **⋯ → Configurar TMDB** na interface web do Cinevo. Use o token de leitura ou a chave API v3 da [sua conta do TMDB](https://www.themoviedb.org/settings/api). A credencial é protegida no Windows. Nos detalhes do título, a integração exibe a sinopse disponível em português do Brasil, a nota e a quantidade de avaliações da comunidade, o elenco, a direção, o roteiro ou a criação e um trailer do YouTube quando cadastrado. A busca envia ao TMDB o título e usa o ano da biblioteca para conferir a correspondência. A nota do TMDB fica separada da sua avaliação pessoal.

This product uses the TMDB API but is not endorsed or certified by TMDB.

Se o TMDB ainda estiver desconectado, a ficha do filme mostra **Conectar TMDB**. Se a busca automática não localizar o título, procure a ficha no TMDB e cole seu endereço em **Editar detalhes → Link da ficha no TMDB**.

Clique em um pôster ou em **Mais informações** para ver a capa, a sinopse e o trailer no painel do filme. Com a busca automática ativada, o Cinevo consulta a Wikipédia e procura trailers identificados no Wikidata. Em **Editar detalhes**, você pode preencher a própria sinopse ou vincular um vídeo do YouTube. O player do trailer é carregado quando você clica em **Ver trailer**. Se a reprodução incorporada estiver bloqueada, use o link do YouTube. O app também oferece busca manual quando não encontra um trailer.

Em **Atualizar Cinevo**, um clique inicia a verificação, mostra o download do instalador e reabre o Cinevo após instalar. O instalador oficial é verificado por SHA-256. Se o player estiver aberto ao terminar, feche-o para instalar.

Em **Mais informações → Remover da biblioteca**, arquivos locais continuam no disco e títulos online voltam para **Filmes importados**. Os importados são agrupados pelo site de origem. Em **Downloads**, **Remover** retira a transferência da fila do qBittorrent e preserva os arquivos.

### Portal online e downloads

Para usar só pelo navegador, abra **Ver detalhes** no portal. Ali você pode reproduzir vídeos compatíveis com o navegador e abrir as opções de download publicadas pela origem. A conexão com o app é opcional.

No Cinevo, clique em **Importar endereço** na biblioteca e cole o link HTTPS de um catálogo ou filme. No [portal Cinevo](https://cinevo-portal.arthurgabrielfera.workers.dev), cole o mesmo tipo de endereço e clique em **Importar**. Nos dois casos, a importação segue as próximas páginas que encontra e pode ser pausada e retomada. A origem pode limitar a leitura se exigir login ou carregar os títulos apenas por scripts.

Ao importar um endereço pela primeira vez no app, o Cinevo usa a coleção conectada ou cria uma nova coleção privada. Se você já usa uma coleção no navegador, use **Conectar ao app** no portal para baixar o arquivo de conexão. No Cinevo, abra **Portal online → Conectar portal** e escolha esse arquivo. O botão **Abrir meu portal** abre a coleção conectada ao Cinevo no navegador.

Títulos novos entram primeiro na aba **Filmes importados**. Use **Adicionar à biblioteca** no pôster ou nos detalhes para incluí-los na coleção principal. Os títulos que já estavam na biblioteca continuam lá. As capas são copiadas para o cache local. Ao remover um título no portal, a cópia já sincronizada no PC é preservada. Os vídeos e as notas locais ficam no PC.

O Cinevo abre a interface web que vem no instalador em uma janela própria, usando o Chrome ou o Microsoft Edge disponível no PC. Ela funciona sem internet para acessar a biblioteca, ver capas em cache e reproduzir arquivos locais pelo LibVLC. Se a janela web falhar na abertura, a interface clássica continua disponível. O portal online continua disponível no navegador para a coleção conectada.

### Downloads com qBittorrent

No qBittorrent instalado no PC, abra **Ferramentas → Opções → Interface Web**. Ative a Interface Web, defina o endereço IP como `127.0.0.1` e deixe o redirecionamento UPnP/NAT-PMP desmarcado. Para usar sem senha, marque **Bypass authentication for clients on localhost** e aplique. No Cinevo, vá a **Downloads → Conectar qBittorrent**, informe a mesma porta e marque **Conectar sem senha neste PC**. Se preferir usar autenticação, desmarque essa opção no Cinevo e informe seu usuário e senha. O Cinevo protege a conexão salva no Windows.

Na aba **Downloads**, cole um link magnet ou escolha uma opção de torrent nos detalhes de um filme importado. A aba mostra as transferências do qBittorrent, incluindo as adicionadas por ele, com progresso, velocidade e estimativa de tempo quando disponível. Você pode pausar, retomar e escolher um vídeo concluído para assistir no player do Cinevo. As transferências continuam no qBittorrent se você fechar o Cinevo.

Quando existe um vídeo direto, **Assistir** reproduz a origem no player integrado. Sem vídeo direto, o botão **Abrir origem** abre a página no navegador. Em **Detalhes → Baixar este conteúdo**, o app consulta a página de cada filme e mostra as opções com os rótulos da origem. Arquivos diretos baixam no Cinevo. Links magnet abrem no cliente torrent configurado no Windows e outros links abrem no navegador. O download vai diretamente da origem ao PC, com seleção de pasta, progresso e cancelamento; arquivos parciais são removidos. Após concluir, o vídeo pode ser assistido offline. Playlists de streaming, DRM, login e arquivos indisponíveis não são convertidos em downloads. Ter um link acessível não transfere direitos de uso do conteúdo.

O arquivo de conexão é pessoal e contém o identificador privado da coleção. No PC, ele é armazenado separadamente em `portal.dat`, protegido pelo usuário do Windows via DPAPI; não integra os backups JSON do catálogo. Nunca publique esse arquivo ou o arquivo de conexão no GitHub.

A versão instalada verifica a release mais recente do GitHub ao abrir. Se houver uma versão superior, baixa o instalador e valida seu SHA-256. Clique em **Atualizar** para instalar em segundo plano e reiniciar o Cinevo. Fechar o app encerra a sessão. É possível desativar o download automático em **Configurações**.

Ao abrir o Cinevo, o atualizador limpa os instaladores de versões já instaladas. Quando prepara uma atualização, guarda somente o instalador pendente.

O instalador aguarda o processo do Cinevo encerrar antes de substituir os arquivos. Durante a instalação, novas aberturas do app são encerradas automaticamente, e o próprio instalador reabre o Cinevo ao terminar a atualização.

Sem conexão, o catálogo continua funcionando. A primeira versão não tem certificado de assinatura de código; o Windows pode exibir uma mensagem de editor desconhecido. A verificação SHA-256 detecta corrupção do download, mas não substitui assinatura de código.

## Seus dados

Catálogo: `%LOCALAPPDATA%\Cinevo\library.json`.

Cópia da gravação anterior: `library.json.bak`. Capas copiadas para a subpasta `covers`. O desinstalador preserva esse diretório para permitir reinstalação sem perder sua coleção. Para restaurar uma cópia, feche o app e substitua `library.json` pelo JSON exportado. Não publique o catálogo: ele contém caminhos locais e suas anotações.

## Desenvolvimento e publicação

WPF, C# e .NET 10. Sem dependências NuGet de terceiros. O runtime .NET e as bibliotecas nativas oficiais LibVLC 3.0.24 são incluídos no instalador. Downloads nativos e código-fonte são fixados e verificados por SHA-256.

```powershell
dotnet run --project tests/Cinevo.Tests.csproj -c Release
./scripts/build.ps1 -Version 0.1.48-beta.49
```

Pré-requisitos de compilação: .NET SDK 10 e Inno Setup 6 ou 7. Para um compilador fora do local padrão, passe `-InnoCompiler C:\caminho\ISCC.exe`. `scripts/icon.ps1` regenera o ícone ICO em seis tamanhos e a imagem PNG.

Cada melhoria deve incrementar o núcleo da versão e receber uma nova tag, por exemplo `v0.1.2-beta.3`. O workflow `.github/workflows/release.yml` compila no Windows, executa testes do catálogo, capas, atualização e reprodução real de vídeo/áudio com LibVLC, e publica `Cinevo-Setup.exe`, `SHA256SUMS.txt` e o código-fonte correspondente do VLC. O app instalado recebe essa release na próxima verificação. Veja `AGENTS.md` para a autorização permanente e as regras de publicação deste projeto.

Para testes de interface sem tocar no catálogo real, defina `CINEVO_TEST_DATA` com uma pasta temporária antes de executar a cópia de desenvolvimento. O processo usa um catálogo e uma instância separados. Nunca use dados pessoais em capturas ou fixtures publicadas.

## Licença

Código do Cinevo sob MIT. LibVLC e módulos VLC têm licenças LGPL/GPL; as licenças, os autores e a origem do código-fonte são preservados. Veja [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). O runtime [.NET](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) e o [Inno Setup](https://jrsoftware.org/isinfo.php) também têm suas próprias licenças.

### Biblioteca inteligente e downloads

Na interface web, use **Filtros e duplicados** para combinar gênero, duração, nota (TMDB ou informada, de 0 a 10), resolução, idioma e coleção. Valores desconhecidos não atendem filtros que exigem esses dados. Duplicados são sugestões por título, ano e episódio; nenhum arquivo é apagado.

Na ficha de um título, **Corrigir identificação / coleções** permite editar metadados ou vincular a ficha/temporada correta do TMDB, incluindo a capa. A vinculação exige a credencial TMDB configurada.

Em Downloads, **Fila e velocidade** altera os limites globais do qBittorrent. **Organizar / episódios** oferece prioridade, início agendado e seleção de arquivos. Para selecionar antes de baixar, marque essa opção ao adicionar o magnet: requer qBittorrent 5+, consulta os metadados e aguarda a escolha dos arquivos. O agendamento de início é persistido no Cinevo e executado a cada 30 segundos enquanto Cinevo e qBittorrent estiverem abertos.

**Armazenamento** contabiliza os vídeos da biblioteca sem contar o mesmo caminho duas vezes, capas e pacotes temporários. A limpeza é restrita a capas sem referência e instaladores antigos na pasta de dados do Cinevo. Vídeos, legendas, capas referenciadas, downloads parciais e a atualização pendente são preservados.

### Contas e TV (Beta)

Abra **Conta e TV** no menu lateral. Criar ou conectar uma conta é opcional. A conta usa nome de usuário e senha de pelo menos 12 caracteres; nesta Beta não há recuperação por e-mail. São sincronizados os títulos adicionados à biblioteca, favoritos e progresso. Arquivos de vídeo, caminhos de pastas, anotações pessoais e credenciais de outros serviços não são enviados. Alterações feitas offline aguardam conexão. A sincronização ocorre a cada 30 segundos.

Para usar a TV, ative a central e abra o endereço mostrado no navegador da TV, na mesma rede privada. Digite o código de 6 dígitos (válido por 5 minutos). O PC deve permanecer ligado com o Cinevo aberto. A central usa a porta 39876 e lembra a ativação; o botão Desativar central revoga as sessões dos aparelhos. No primeiro acesso, o Windows pode pedir permissão para a rede privada.

O modo TV tem botões grandes, foco visível, navegação por setas/OK e controle remoto. No PC, o link **Abrir modo TV neste PC** permite usar controles padrão reconhecidos pelo navegador: direcional ou analógico esquerdo, A para selecionar, B para voltar e Start para pausar/continuar. Sons suaves podem ser desligados e não tocam automaticamente antes de interação. Não há downloads de pacotes de sons.

Os vídeos locais são enviados diretamente pelo PC à TV com suporte a HTTP Range; vídeos online diretos continuam na origem. O player salva o progresso a cada 5 segundos e ao pausar/sair. Esta versão não converte formatos; codecs, faixas de áudio e legendas dependem do navegador da TV. A validação automatizada usa navegador no PC e controle simulado; não substitui o teste na TV física.

### Perfis, séries e central de mídia

**Perfis** no topo permite criar até oito pessoas e trocar o perfil do PC. Assistidos, favoritos e ponto de reprodução são individuais; os arquivos são compartilhados. O perfil Principal preserva os dados anteriores. A TV escolhe o próprio perfil no cabeçalho, sem alterar o PC. Os perfis também sincronizam pela conta. Perfis não são controles de acesso nem têm senha individual.

**Mídia no PC** gerencia a inclusão automática de vídeos concluídos do qBittorrent, verifica novos arquivos e escolhe a pasta padrão dos torrents. A verificação ocorre a cada 30 segundos, em lotes, fora da thread da interface para indexação dos arquivos. Vídeos ainda incompletos, arquivos fora da pasta do torrent e caminhos removidos da biblioteca não são incluídos.

Clicar em uma série abre sua central: uma ficha com temporadas em ordem, episódios disponíveis e continuação para o perfil escolhido. Capas de temporadas são consultadas sob demanda no TMDB quando configurado; na ausência delas, usa-se a capa disponível. O modo controle destaca a seleção com brilho animado, respeitando a preferência de movimento reduzido. Os controles adicionais ficam em **Ferramentas**, e os títulos grandes das páginas foram removidos.

### Experiência de reprodução

A tela inicial usa o perfil selecionado para as fileiras Continue assistindo e Próximo episódio. Vídeos concluídos pelo Cinevo aparecem em Recém-baixados. A ficha mostra a arte panorâmica do TMDB quando disponível, com alternativa visual pela capa existente. Artes panorâmicas são carregadas sob demanda e mantidas somente em memória: até quatro imagens, com limite de 6 MB por imagem.

O botão Recolher menu libera espaço para as capas, mantém rótulos e ícones acessíveis e salva a preferência no PC. A expansão e o retorno das capas usam View Transitions quando disponíveis; movimento reduzido e navegadores sem suporte usam a abertura simples.

No player Windows e no modo TV, o próximo episódio disponível é iniciado após uma contagem de 10 segundos ao terminar. Cancelar mantém a sessão no episódio atual. Na TV, sair do player, mudar de perfil ou ocultar a página interrompe a contagem. No Windows, volume e tela cheia são mantidos ao avançar. A reprodução na TV continua sujeita aos formatos aceitos pelo navegador.
