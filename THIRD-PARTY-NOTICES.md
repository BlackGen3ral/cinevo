# Componentes de terceiros

## LibVLC / VideoLAN 3.0.24

O Cinevo usa LibVLC, desenvolvido pelo projeto VideoLAN, para reprodução de mídia. LibVLC é distribuído sob LGPL 2.1 ou posterior. Os módulos VLC incluídos podem ter licença GPL 2 ou posterior. Os arquivos binários oficiais são redistribuídos sem alterações e permanecem em arquivos separados, substituíveis na pasta `vlc`.

Copyright: VideoLAN e os autores listados em `vlc/AUTHORS.txt`. O texto da licença da distribuição está em `vlc/COPYING.txt`. A licença LGPL está no diretório `COPYING.LIB` do arquivo-fonte correspondente.

- Projeto: https://www.videolan.org/vlc/libvlc.html
- Binários originais: https://downloads.videolan.org/videolan/vlc/3.0.24/win64/vlc-3.0.24-win64.zip
- Código-fonte correspondente, fornecido também como asset junto ao instalador de cada release: `vlc-3.0.24-source.tar.xz`.
- Origem do código-fonte: https://downloads.videolan.org/videolan/vlc/3.0.24/vlc-3.0.24.tar.xz
- Fontes e instruções dos componentes externos: https://code.videolan.org/videolan/vlc/-/tree/3.0.24/contrib

O código do Cinevo continua disponível sob a licença MIT. As licenças dos componentes acima não são substituídas por essa licença. Você pode substituir as bibliotecas nativas e os módulos por versões compatíveis.

## Wikipédia / Wikimedia

A busca de pôsteres usa a API MediaWiki de Wikipédias em português e inglês. As imagens permanecem sujeitas às licenças ou direitos identificados na página de cada arquivo. Nenhuma imagem de filme é incluída no código-fonte ou no instalador. O aplicativo salva a página de origem em cada correspondência e oferece o botão **Ver origem da capa na Wikipédia** nos detalhes do filme. Consulte essa origem para créditos e condições de reutilização.

A busca envia somente título e ano. Vídeos, caminhos de arquivos e anotações não são enviados.
