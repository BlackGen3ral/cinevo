import {test,after} from 'node:test';import assert from 'node:assert/strict';import {readFile} from 'node:fs/promises';import {createHash} from 'node:crypto';import {Miniflare} from 'miniflare';
const key='b'.repeat(64);const html=await readFile(new URL('../index.html',import.meta.url),'utf8');
const fixture=`
export async function fixtureFetch(address) {
 const url=new URL(address);
 if(url.pathname==='/licensed-film') return new Response('<a download href="/example.mp4">Baixar vídeo</a>',{headers:{'content-type':'text/html'}});
 if(url.pathname==='/') return new Response('<link rel="https://api.w.org/" href="/wp-json/"><title>Catalog</title>',{headers:{'content-type':'text/html'}});
 const page=Number(url.searchParams.get('page')||1);
 return Response.json([0,1].map(index=>({id:page*10+index,link:'https://wp.example.org/film-'+(page*10+index),
  title:{rendered:'Filme &#8211; '+(page*10+index)+' (2024)'},excerpt:{rendered:'<p>Sinopse pública.</p>'},
  _embedded:{'wp:featuredmedia':[{source_url:'https://wp.example.org/capa.jpg'}]}})),{headers:{'X-WP-TotalPages':'5','X-WP-Total':'10'}});
}
`;
const entry=(await readFile(new URL('../worker.mjs',import.meta.url),'utf8'))
 .replace("import html from './index.html';",'const html='+JSON.stringify(html)+';')
 .replace('await importCatalog(data.url,{catalogPage:data.continuation===true})','await importCatalog(data.url,{catalogPage:data.continuation===true,validate:async()=>{},fetcher:fixtureFetch})')
 .replace('await loadDownloads(data.url)','await loadDownloads(data.url,{validate:async()=>{},fetcher:fixtureFetch})')+fixture;
const harness=`export {AccountStore} from './worker.mjs';import worker,{fixtureFetch} from './worker.mjs';import {parsePage,importAddress,publicUrl,publicIp,fetchPublic,importCatalog,downloadOptions} from './importer.mjs';
const card=(n)=>'<a href="/filmes/'+n+'"><img src="/'+n+'.jpg" alt="Filme '+n+'"></a>';
const catalog=async address=>{const page=Number(new URL(address).searchParams.get('page')||1);const html='<title>Catálogo</title>'+card(page)+(page<6?'<a rel="next" href="?page='+(page+1)+'">Próximo</a>':'');return new Response(html,{headers:{'content-type':'text/html'}});};
const base='https://catalog.example.org/filmes';
`;
const handler=`
export default{async fetch(request,env){const u=new URL(request.url);if(u.pathname==='/__parse'){const p=await request.json();return Response.json(await parsePage(p.html,p.url));}if(u.pathname==='/__direct'){return Response.json(await importAddress('https://media.w3.org/test.mp4',{validate:async()=>{},fetcher:async()=>new Response('video',{headers:{'Content-Type':'video/mp4'}})}));}if(u.pathname==='/__blocked'){try{await fetchPublic('https://example.org/start',{validate:async()=>{},fetcher:async()=>new Response(null,{status:302,headers:{location:'https://127.0.0.1/admin'}})});return new Response('FAIL');}catch{return new Response('BLOCKED');}}return worker.fetch(request,env);}};`;
const checks=`
if(u.pathname==='/__wp')return Response.json(await importCatalog(u.searchParams.has('more')?'https://wp.example.org/wp-json/wp/v2/posts?page=5':'https://wp.example.org/',{validate:async()=>{},fetcher:fixtureFetch}));
if(u.pathname==='/__catalog')return Response.json(await importCatalog(u.searchParams.has('more')?base+'?page=5':base,{catalogPage:u.searchParams.has('more'),validate:async()=>{},fetcher:catalog}));
if(u.pathname==='/__numbered')return Response.json(await importCatalog(base,{validate:async()=>{},fetcher:async address=>{
 const page=Number(new URL(address).searchParams.get('page')||1);
 if(page===3)return new Response(null,{status:403});
 const metadata=page===1?'<script type="application/ld+json">'+JSON.stringify({'@type':'Movie',name:'Filme 1',url:'/filmes/1',description:'Sinopse'})+'</script>':'';
 return new Response('<title>Catálogo</title>'+metadata+card(page)+card(page+10)+'<nav class="wp-pagenavi"><a href="?page=4">4</a><a href="?page='+(page+1)+'">'+(page+1)+'</a></nav>',{headers:{'content-type':'text/html'}});
}}));
if(u.pathname==='/__quality'){
 const a='magnet:?xt=urn:btih:'+'b'.repeat(40)+'&amp;dn=Example%2E1080p%2EHEVC&amp;tr=udp%3A%2F%2Ftracker.example.org';
 const b='magnet:?xt=urn:btih:'+'c'.repeat(40)+'&amp;dn=Example';
 const c='magnet:?xt=urn:btih:'+'d'.repeat(40)+'&amp;dn=Example.720p';
 return Response.json(await downloadOptions('<a href="'+a+'">Torrent</a><a href="'+b+'"></a><a href="'+c+'" title="4K">Baixar</a><a href="/categoria/1080p/">1080p</a><a href="/files/test.mp4" download>480p</a>',base));
}
if(u.pathname==='/__options')return Response.json(await downloadOptions('<a href="magnet:?xt=urn:btih:'+'a'.repeat(40)+'&amp;dn=Demo">1080p | 4.50 GB | Dual Áudio</a><a href="/demo.mp4" download>720p</a><a href="/filmes/other">Outro filme</a>',base));
`;
const mf=new Miniflare({compatibilityDate:'2026-07-30',modules:[{type:'ESModule',path:'harness.mjs',contents:harness+handler.replace('return worker.fetch(request,env);',checks+'return worker.fetch(request,u.pathname==="/api/downloads"?{}:env);')},{type:'ESModule',path:'worker.mjs',contents:entry},{type:'ESModule',path:'importer.mjs',contents:await readFile(new URL('../importer.mjs',import.meta.url),'utf8')},{type:'ESModule',path:'accounts.mjs',contents:await readFile(new URL('../accounts.mjs',import.meta.url),'utf8')}],bindings:{AUTH_HASH:createHash('sha256').update(key).digest('hex')},d1Databases:{DB:'test-db'},durableObjects:{ACCOUNTS:{className:'AccountStore',useSQLite:true}}});
after(()=>mf.dispose());const db=await mf.getD1Database('DB');await db.exec(await readFile(new URL('../schema.sql',import.meta.url),'utf8'));const headers={Authorization:'Bearer '+key,'Content-Type':'application/json'};
async function parse(markup){const response=await mf.dispatchFetch('https://portal.test/__parse',{method:'POST',body:JSON.stringify({html:markup,url:'https://movies.public-domain.org/film/demo'})});assert.equal(response.status,200);return response.json();}
test('OpenGraph, HTML video and explicitly offered download',async()=>{const [item]=await parse('<title>Demo film</title><meta property="og:image" content="/poster.jpg"><meta name="description" content="A description"><video><source src="https://media.w3.org/demo.mp4"></video><a download href="https://media.w3.org/demo.mp4">Download</a>');assert.equal(item.title,'Demo film');assert.equal(item.coverUrl,'https://movies.public-domain.org/poster.jpg');assert.equal(item.streamUrl,'https://media.w3.org/demo.mp4');assert.equal(item.downloadUrl,item.streamUrl);});
test('JSON-LD Movie and nested video do not create duplicates',async()=>{const [item,...other]=await parse('<script type="application/ld+json">'+JSON.stringify({'@type':'Movie',name:'Example',datePublished:'2020-03-01',image:'/a.png',video:{'@type':'VideoObject',name:'Trailer',contentUrl:'https://media.w3.org/trailer.mp4'}})+'</script>');assert.equal(item.title,'Example');assert.equal(item.year,'2020');assert.equal(item.streamUrl,'https://media.w3.org/trailer.mp4');assert.equal(other.length,0);assert.equal(item.downloadUrl,'');});
test('structured catalog list imports multiple movies',async()=>{const result=await parse('<script type="application/ld+json">'+JSON.stringify({'@type':'ItemList',itemListElement:[{item:{'@type':'Movie',name:'One',url:'/one'}},{item:{'@type':'Movie',name:'Two',url:'/two'}}]})+'</script>');assert.equal(result.length,2);});
test('catalog cards and JSON-LD keep series, seasons and episode numbers',async()=>{
 const cards=await parse('<h2><a href="/filmes/aventura">Aventura (2020)</a></h2><h2><a href="/series/aurora-3">Aurora 3ª Temporada (2025)</a></h2>');
 assert.deepEqual(cards.map(({mediaType,seriesTitle,season})=>[mediaType,seriesTitle,season]),[['',undefined,undefined],['series','Aurora',3]]);
 const [episode]=await parse('<script type="application/ld+json">'+JSON.stringify({
  '@type':'TVEpisode',name:'O retorno',episodeNumber:4,partOfSeason:{seasonNumber:2,partOfSeries:{name:'Aurora'}}
 })+'</script>');
 assert.deepEqual([episode.mediaType,episode.seriesTitle,episode.season,episode.episode],['series','Aurora',2,4]);
});
test('pagination continues in batches and includes a final page with one movie',async()=>{
 const first=await(await mf.dispatchFetch('https://portal.test/__catalog')).json();
 assert.deepEqual(first.items.map(item=>item.title),['Filme 1','Filme 2','Filme 3','Filme 4']);
 assert.match(first.next,/page=5$/);
 const last=await(await mf.dispatchFetch('https://portal.test/__catalog?more')).json();
 assert.deepEqual(last.items.map(item=>item.title),['Filme 5','Filme 6']);
 assert.equal(last.next,'');
});
test('numbered pagination retains visible cards missing from metadata and reports a blocked page',async()=>{
 const result=await(await mf.dispatchFetch('https://portal.test/__numbered')).json();
 assert.deepEqual(result.items.map(item=>item.title),['Filme 1','Filme 11','Filme 2','Filme 12']);
 assert.equal(result.items[0].description,'Sinopse');
 assert.equal(result.items[0].coverUrl,'https://catalog.example.org/1.jpg');
 assert.match(result.next,/page=3$/);
 assert.match(result.partial,/bloqueou/);
});
test('a film page with related cards imports the selected film',async()=>{
 const items=await parse('<meta property="og:type" content="article"><meta property="og:title" content="Filme selecionado - Nome do site"><main><h1>Filme selecionado</h1></main><a href="/filmes/1"><img src="/1.jpg" alt="Related 1"></a><a href="/filmes/2"><img src="/2.jpg" alt="Related 2"></a>');
 assert.deepEqual(items.map(item=>item.title),['Filme selecionado']);
 assert.equal(items[0].linkOnly,false);
});
test('download choices preserve the source label and distinguish magnet from direct video',async()=>{
 const options=await(await mf.dispatchFetch('https://portal.test/__options')).json();
 assert.deepEqual(options.map(({kind,label})=>[kind,label]),[['torrent','1080p | 4.50 GB | Dual Áudio'],['file','720p']]);
 assert.match(options[0].url,/&dn=Demo$/);
});
test('HTML catalog imports each visible movie card and skips navigation',async()=>{
 const result=await parse(`<title>Catálogo</title>
 <a href="/categoria/comedia">Comédia</a><a href="/filmes/">Filmes</a>
 <a href="/filmes/um"><img src="/placeholder.png" data-src="/um.jpg" alt="Um (2024)"></a>
 <a href="/filmes/um"><h2>Um (2024)</h2></a>
 <a href="/filmes/dois"><img srcset="/dois.jpg 320w, /dois-xl.jpg 640w"><h2>Dois</h2></a>`);
 assert.deepEqual(result.map(x=>[x.title,x.year,new URL(x.coverUrl).pathname]),[['Um (2024)','2024','/um.jpg'],['Dois','','/dois.jpg']]);
 assert.ok(result.every(x=>!x.streamUrl&&!x.downloadUrl));
});
test('embeds are links, not extracted stream URLs',async()=>{const [item]=await parse('<title>Film</title><meta property="og:video" content="https://www.youtube.com/embed/123"><iframe src="https://private.local/player"></iframe>');assert.equal(item.streamUrl,'');assert.equal(item.downloadUrl,'');});
test('direct file import and unsafe redirect rejection',async()=>{const result=await(await mf.dispatchFetch('https://portal.test/__direct')).json();assert.equal(result[0].downloadUrl,'https://media.w3.org/test.mp4');assert.equal(await(await mf.dispatchFetch('https://portal.test/__blocked')).text(),'BLOCKED');});
test('catalog API requires credential and enforces origin',async()=>{assert.equal((await mf.dispatchFetch('https://portal.test/api/items')).status,401);assert.equal((await mf.dispatchFetch('https://portal.test/api/items',{headers:{...headers,Origin:'https://evil.org'}})).status,403);assert.equal((await mf.dispatchFetch('https://portal.test/api/items',{headers})).status,200);});
test('catalog is durable, removable and does not store credentials',async()=>{const id='a'.repeat(64),item={title:'Test',sourceUrl:'https://media.w3.org/demo.mp4'};await db.prepare('INSERT INTO catalog_items(id,owner_hash,source_url,title,data,created_at) VALUES(?,?,?,?,?,?)').bind(id,createHash('sha256').update(key).digest('hex'),item.sourceUrl,item.title,JSON.stringify(item),'2026-11-14').run();const response=await mf.dispatchFetch('https://portal.test/api/items',{headers});const data=await response.json();assert.equal(data.items[0].title,'Test');assert.ok(!JSON.stringify(data).includes(key));assert.equal((await mf.dispatchFetch('https://portal.test/api/items/'+id,{method:'DELETE',headers})).status,200);assert.equal((await(await mf.dispatchFetch('https://portal.test/api/items',{headers})).json()).items.length,0);});
test('static site headers and missing route',async()=>{const response=await mf.dispatchFetch('https://portal.test/');assert.equal(response.status,200);assert.ok(response.headers.get('Content-Security-Policy').includes("frame-ancestors 'none'"));assert.equal((await mf.dispatchFetch('https://portal.test/missing')).status,404);});
test('no access-key screen; automatic HttpOnly session isolates browsers',async()=>{const page=await mf.dispatchFetch('https://portal.test/');const cookie=page.headers.get('set-cookie');assert.ok(cookie.includes('HttpOnly'));assert.ok(!(await page.text()).includes('id="login"'));const result=await mf.dispatchFetch('https://portal.test/api/items',{headers:{Cookie:cookie.split(';')[0]}});assert.equal(result.status,200);const owner=createHash('sha256').update(key).digest('hex');await db.prepare('INSERT OR IGNORE INTO catalog_items(id,owner_hash,source_url,title,data,created_at) VALUES(?,?,?,?,?,?)').bind('c'.repeat(64),owner,'https://media.w3.org/private','Private',JSON.stringify({title:'Private'}),'2026-11-14').run();const own=await(await mf.dispatchFetch('https://portal.test/api/items',{headers})).json();assert.ok(own.items.some(i=>i.title==='Private'));const other=await(await mf.dispatchFetch('https://portal.test/api/items',{headers:{Cookie:cookie.split(';')[0]}})).json();assert.ok(!other.items.some(i=>i.title==='Private'));const denied=await mf.dispatchFetch('https://portal.test/api/items/'+'c'.repeat(64),{method:'DELETE',headers:{Cookie:cookie.split(';')[0]}});assert.equal(denied.status,200);assert.ok(await db.prepare('SELECT id FROM catalog_items WHERE id=?').bind('c'.repeat(64)).first());});
test('public URL and address validation',async()=>{const {publicUrl,publicIp}=await import('../importer.mjs');for(const url of ['http://example.org','https://127.1/','https://user:pass@example.org','https://intranet.local/','https://[::1]/','https://example.org:444/'])assert.throws(()=>publicUrl(url));for(const ip of ['127.0.0.1','10.0.0.1','169.254.1.1','192.168.1.1','::1','fc00::1','::ffff:127.0.0.1'])assert.equal(publicIp(ip),false);assert.equal(publicIp('1.1.1.1'),true);});

test('WordPress public catalog follows every API page with title, cover and synopsis',async()=>{
 const first=await(await mf.dispatchFetch('https://portal.test/__wp')).json();
 assert.equal(first.items.length,8);
 assert.equal(first.total,10);
 assert.equal(first.items[0].title,'Filme – 10 (2024)');
 assert.equal(first.items[0].description,'Sinopse pública.');
 assert.equal(first.items[0].coverUrl,'https://wp.example.org/capa.jpg');
 assert.equal(first.items[0].importedFrom,'https://wp.example.org/');
 assert.equal(new URL(first.next).searchParams.get('page'),'5');
 const last=await(await mf.dispatchFetch('https://portal.test/__wp?more')).json();
 assert.equal(last.items.length,2);
 assert.equal(last.next,'');
});

test('switching legacy catalog to WordPress preserves IDs and concurrent imports do not duplicate titles',async()=>{
 const owner=createHash('sha256').update(key).digest('hex');
 const id='e'.repeat(64),source='https://wp.example.org/film-10';
 await db.prepare('INSERT INTO catalog_items(id,owner_hash,source_url,title,data,created_at) VALUES(?,?,?,?,?,?)')
  .bind(id,owner,source,'Título antigo com qualidade',JSON.stringify({title:'Título antigo com qualidade',sourceUrl:source,downloadUrl:'https://wp.example.org/a.mp4'}),'2020-01-01').run();
 const run=()=>mf.dispatchFetch('https://portal.test/api/import',{method:'POST',headers,body:JSON.stringify({url:'https://wp.example.org/'})});
 const responses=await Promise.all([run(),run()]);
 for(const response of responses)assert.equal(response.status,200);
 const {results}=await db.prepare('SELECT id,data FROM catalog_items WHERE owner_hash=? AND source_url LIKE ?').bind(owner,'https://wp.example.org/%').all();
 assert.equal(results.length,8);
 const record=results.find(row=>row.id===id);
 assert.ok(record);
 assert.equal(JSON.parse(record.data).downloadUrl,'https://wp.example.org/a.mp4');
 assert.equal(JSON.parse(record.data).catalogSource,'wordpress');
});

test('download lookup works with no database binding and requires a session',async()=>{
 const request={method:'POST',body:JSON.stringify({url:'https://media.example.org/licensed-film'})};
 const url='https://portal.test/api/downloads';
 assert.equal((await mf.dispatchFetch(url,request)).status,401);
 const response=await mf.dispatchFetch(url,{...request,headers});
 assert.equal(response.status,200);
 assert.deepEqual((await response.json()).options,[{url:'https://media.example.org/example.mp4',label:'Baixar vídeo',kind:'file'}]);
 const privateUrl=await mf.dispatchFetch(url,{...request,headers,body:JSON.stringify({url:'https://127.0.0.1/'})});
 assert.equal(privateUrl.status,400);
});

test('download quality uses the own magnet or link label and ignores category badges',async()=>{
 const options=await(await mf.dispatchFetch('https://portal.test/__quality')).json();
 assert.deepEqual(options.map(({kind,label})=>[kind,label]),[['torrent','1080p'],['torrent','Qualidade não informada'],['torrent','4K'],['file','480p']]);
 assert.match(options[0].url,/&dn=Example%2E1080p%2EHEVC&tr=udp%3A%2F%2Ftracker\.example\.org$/);
});
async function accountRequest(route,body,token){return mf.dispatchFetch('https://portal.test/api/account/'+route,{method:body?'POST':'GET',headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},...(body?{body:JSON.stringify(body)}:{})});}
test('accounts authenticate, revoke sessions and keep passwords out of responses',async()=>{
 const login={username:'qa-login',password:'Uma-senha-de-teste-123'};
 const created=await accountRequest('register',login);assert.equal(created.status,200);const auth=await created.json();assert.equal(auth.username,login.username);assert.match(auth.token,/\.[a-f0-9]{64}$/);assert.equal(auth.password,undefined);
 assert.equal((await accountRequest('login',{...login,password:'senha-incorreta-123'})).status,401);
 const logged=await accountRequest('login',login);assert.equal(logged.status,200);const token=(await logged.json()).token;
 assert.equal((await accountRequest('me',null,token)).status,200);
 assert.equal((await accountRequest('logout',{},token)).status,200);
 assert.equal((await accountRequest('me',null,token)).status,401);

});
test('account synchronization isolates owners, rejects stale writes and excludes personal paths',async()=>{
 const a=await(await accountRequest('register',{username:'qa-owner-a',password:'Senha-de-teste-12345'})).json();
 const b=await(await accountRequest('register',{username:'qa-owner-b',password:'Senha-de-teste-67890'})).json();
 const title={title:'Filme de teste',year:'2020',genre:'Drama',seriesTitle:'',sourceUrl:'',streamUrl:'',coverUrl:'',tmdbUrl:'',member:true,favorite:false,watched:false,resume:45000,duration:3600000,season:0,episode:0,path:'C:/privado/video.mp4',password:'Nunca sincronizar'};
 const id='a'.repeat(64);let saved=await(await accountRequest('titles',{items:[{id,data:title,version:0}]},a.token)).json();assert.equal(saved.items[0].version,1);
 const other=await(await accountRequest('titles',null,b.token)).json();assert.equal(other.items.length,0);
 const own=await(await accountRequest('titles',null,a.token)).json();assert.equal(own.items[0].data.resume,45000);assert.equal(own.items[0].data.path,undefined);assert.equal(own.items[0].data.password,undefined);
 const changes=await Promise.all([true,false].map(favorite=>accountRequest('titles',{items:[{id,data:{...title,favorite},version:1}]},a.token).then(r=>r.json())));
 assert.equal(changes.filter(r=>r.items[0].conflict).length,1);
 const final=await(await accountRequest('titles',null,a.token)).json();assert.equal(final.items[0].version,2);
 assert.equal((await accountRequest('titles',{items:[{id,data:{...title,streamUrl:'https://192.168.1.1/video.mp4'},version:2}]},a.token)).status,400);
 const profile='c'.repeat(32);
 assert.equal((await accountRequest('profiles',{profiles:[{id:profile,name:'Pessoa B'}]},a.token)).status,200);
 const profiles=await(await accountRequest('profiles',null,a.token)).json();assert.equal(profiles.profiles.length,2);
 const empty=await(await accountRequest('titles?profile='+profile,null,a.token)).json();assert.equal(empty.items.length,0);
 const isolated=await(await accountRequest('titles?profile='+profile,{items:[{id,data:{...title,resume:90000,favorite:true},version:0}]},a.token)).json();assert.equal(isolated.items[0].version,1);
 const unchanged=await(await accountRequest('titles',null,a.token)).json();assert.equal(unchanged.items[0].data.resume,45000);
 assert.equal((await accountRequest('titles?profile='+profile,null,b.token)).status,400);
 assert.equal((await accountRequest('delete',{password:'Senha-de-teste-12345'},a.token)).status,200);assert.equal((await accountRequest('me',null,a.token)).status,401);
 const anonymous=await mf.dispatchFetch('https://portal.test/api/account/titles',{headers});assert.equal(anonymous.status,401);
});
