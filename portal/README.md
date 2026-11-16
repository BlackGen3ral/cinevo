# Cinevo Portal

Portal em português publicado na conta Cloudflare do proprietário, com Worker ES Modules, HTMLRewriter e D1. Acesso direto sem tela de login; cada sessão recebe um identificador aleatório de 256 bits em cookie HttpOnly/Secure/SameSite=Strict. Consultas e remoções são limitadas ao hash da sessão. O arquivo de conexão exportado transfere essa mesma coleção ao app Windows e deve ser tratado como informação privada.

## Fonte e publicação

Arquivos: `worker.mjs`, `accounts.mjs`, `importer.mjs` e `index.html` (módulo de texto). A configuração pública dos recursos está em `deployment.json`. O esquema está em `schema.sql`. Recursos já existentes: Worker `cinevo-portal`, banco D1 `cinevo-portal`. Não recrie o banco ao atualizar o código.

Para publicar pela integração Cloudflare, envie os quatro módulos em multipart no endpoint `PUT /accounts/{account_id}/workers/scripts/cinevo-portal` com `main_module: worker.mjs`, a data de compatibilidade de `deployment.json` e os bindings D1 `DB` e Durable Object `ACCOUNTS` indicados em `deployment.json`. Em atualizações, preserve o namespace de contas já existente. `index.html` usa Content-Type `text/plain`; os outros módulos usam `application/javascript+module`. Preserve o banco e os dados. Observabilidade e traces devem permanecer habilitados. Habilite o subdomínio workers.dev apenas na criação. Nenhum segredo Cloudflare integra esse projeto.

Tabelas: `catalog_items` é o catálogo atual com isolamento por `owner_hash`. Uma tabela legada `items`, caso existente, não deve ser apagada sem revisar a migração. Identificadores de conexão nunca são salvos em texto no banco.

## Verificação

```powershell
cd portal
npm ci --ignore-scripts
npm test
```

Os testes usam Miniflare e D1 locais: extração, listas estruturadas, vídeos, downloads explícitos, rejeição de IPs privados e redirecionamentos, sessão automática, isolamento entre coleções e persistência. Não usam o catálogo de produção. As dependências npm são exclusivamente de teste e não são enviadas ao Worker.

Importação limitada a HTTPS público, cinco saltos, 2 MB de HTML, quatro páginas por lote e imagens de até 5 MB. Todos os saltos são validados e os endereços DNS privados/reservados são rejeitados. Scripts da origem não são executados. Não há proxy de vídeo ou transporte de arquivos de filmes pela Cloudflare.

Ao importar a raiz de um site WordPress com descoberta `api.w.org`, o importador consulta a listagem pública `wp/v2/posts` em páginas de 50 registros, com título, sinopse e capa. A continuação segue `X-WP-TotalPages`, e `total` informa `X-WP-Total`. A leitura HTML segue disponível quando essa API opcional está indisponível. A migração preserva os identificadores dos registros anteriores por endereço, com seleção e gravação na mesma instrução SQL para impedir duplicatas em importações concorrentes.

`GET /health` é público. A página inicial cria a sessão automaticamente. A API aceita cookie de sessão ou Bearer do app. `GET /api/items` lista a coleção, `POST /api/import` importa `{url}`, `GET /api/connection` exporta a conexão, `POST /api/session` restaura uma conexão, e `DELETE /api/items/{id}` remove da coleção correspondente. `GET /api/items/{id}/cover` serve somente uma capa pertencente à sessão, com resposta limitada. O endpoint GET /api/items/{id}/downloads consulta os links publicados na página original. `POST /api/downloads` consulta `{url}` diretamente na origem pública com a sessão do portal, sem acessar a coleção. Cabeçalhos de autenticação não são enviados aos sites importados.

## Contas opcionais

O portal de importação continua com abertura direta e sessões próprias. O módulo `accounts.mjs` adiciona `/api/account/register`, `/login`, `/me`, `/logout` e `/titles`, usados pelo app desktop. As contas não substituem os identificadores de coleção do portal. As contas usam a classe SQLite Durable Object `AccountStore`, uma instância por nome de usuário, no binding `ACCOUNTS`. Assim, a sincronização não consome as leituras do D1 usado para importação. Ao publicar, envie também `accounts.mjs` e preserve o binding `ACCOUNTS`; somente na criação aplique a migração `accounts-v1` com `new_sqlite_classes: ["AccountStore"]`. Não apague ou recrie o D1 existente.

Senhas usam PBKDF2-SHA256 com salt aleatório e 100.000 iterações. Tokens de sessão aleatórios de 256 bits expiram em 90 dias; o banco guarda apenas seu hash. Há limitação de tentativas de autenticação por origem de rede e minuto. Metadados sincronizados usam uma lista explícita de campos, isolamento por conta e controle de versão otimista. Não há transporte de vídeos, caminhos de arquivos, nem `library.json`. As contas são opcionais e não têm recuperação por e-mail nesta Beta.

### Perfis de uma conta

`GET/POST /api/account/profiles` lista e cria até oito perfis. `GET/POST /api/account/titles?profile=ID` separa os estados de cada pessoa. O perfil `principal` mantém a tabela `titles` original e continua sendo o padrão para versões antigas. A tabela aditiva `profile_titles` guarda os demais perfis no mesmo objeto da conta, sem recriar o namespace ou o D1.
