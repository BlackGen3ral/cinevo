# Cinevo Portal

Portal em português publicado na conta Cloudflare do proprietário, com Worker ES Modules, HTMLRewriter e D1. Acesso direto sem tela de login; cada sessão recebe um identificador aleatório de 256 bits em cookie HttpOnly/Secure/SameSite=Strict. Consultas e remoções são limitadas ao hash da sessão. O arquivo de conexão exportado transfere essa mesma coleção ao app Windows e deve ser tratado como informação privada.

## Fonte e publicação

Arquivos: `worker.mjs`, `importer.mjs` e `index.html` (módulo de texto). A configuração pública dos recursos está em `deployment.json`. O esquema está em `schema.sql`. Recursos já existentes: Worker `cinevo-portal`, banco D1 `cinevo-portal`. Não recrie o banco ao atualizar o código.

Para publicar pela integração Cloudflare, envie os três módulos em multipart no endpoint `PUT /accounts/{account_id}/workers/scripts/cinevo-portal` com `main_module: worker.mjs`, a data de compatibilidade de `deployment.json` e o binding D1 `DB` com o database_id indicado. `index.html` usa Content-Type `text/plain`; os outros módulos usam `application/javascript+module`. Preserve o banco e os dados. Observabilidade e traces devem permanecer habilitados. Habilite o subdomínio workers.dev apenas na criação. Nenhum segredo Cloudflare integra esse projeto.

Tabelas: `catalog_items` é o catálogo atual com isolamento por `owner_hash`. Uma tabela legada `items`, caso existente, não deve ser apagada sem revisar a migração. Identificadores de conexão nunca são salvos em texto no banco.

## Verificação

```powershell
cd portal
npm ci --ignore-scripts
npm test
```

Os testes usam Miniflare e D1 locais: extração, listas estruturadas, vídeos, downloads explícitos, rejeição de IPs privados e redirecionamentos, sessão automática, isolamento entre coleções e persistência. Não usam o catálogo de produção. As dependências npm são exclusivamente de teste e não são enviadas ao Worker.

Importação limitada a HTTPS público, cinco saltos, 2 MB de HTML, 40 títulos por página e imagens de até 5 MB. Todos os saltos são validados e os endereços DNS privados/reservados são rejeitados. Scripts da origem não são executados. Não há proxy de vídeo ou transporte de arquivos de filmes pela Cloudflare.

`GET /health` é público. A página inicial cria a sessão automaticamente. A API aceita cookie de sessão ou Bearer do app. `GET /api/items` lista a coleção, `POST /api/import` importa `{url}`, `GET /api/connection` exporta a conexão, `POST /api/session` restaura uma conexão, e `DELETE /api/items/{id}` remove da coleção correspondente. `GET /api/items/{id}/cover` serve somente uma capa pertencente à sessão, com resposta limitada. Cabeçalhos de autenticação não são enviados aos sites importados.
