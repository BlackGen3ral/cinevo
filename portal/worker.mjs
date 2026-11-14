import {importCatalog,ImportError,boundedText,fetchPublic,loadDownloads} from './importer.mjs';
import html from './index.html';
const security={'X-Content-Type-Options':'nosniff','Referrer-Policy':'no-referrer','Cache-Control':'no-store','Permissions-Policy':'camera=(), microphone=(), geolocation=()'};
function json(value,status=200){return Response.json(value,{status,headers:security});}
function sessionKey(request){const bearer=(request.headers.get('Authorization')||'').replace(/^Bearer /,'');const cookie=(request.headers.get('Cookie')||'').match(/(?:^|;\s*)cinevo_session=([a-f0-9]{64})(?:;|$)/)?.[1]||'';const key=bearer||cookie;return /^[a-f0-9]{64}$/.test(key)?key:'';}
function newKey(){return [...crypto.getRandomValues(new Uint8Array(32))].map(b=>b.toString(16).padStart(2,'0')).join('');}
function sessionCookie(key){return `cinevo_session=${key}; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=31536000`;}
async function ownerHash(key){return [...new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(key)))].map(b=>b.toString(16).padStart(2,'0')).join('');}
export default {async fetch(request,env){
 try{
  const url=new URL(request.url);
  if(url.pathname==='/health'&&request.method==='GET')return json({ok:true,app:'Cinevo Portal'});
  if(url.pathname==='/'&&['GET','HEAD'].includes(request.method))return new Response(request.method==='HEAD'?null:html,{headers:{...security,'Set-Cookie':sessionCookie(sessionKey(request)||newKey()),'Content-Type':'text/html; charset=utf-8','Content-Security-Policy':"default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src https:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'"}});
  if(url.pathname==='/robots.txt')return new Response('User-agent: *\nDisallow: /',{headers:{'Content-Type':'text/plain'}});
  if(!url.pathname.startsWith('/api/'))return json({error:'Página não encontrada.'},404);
  const origin=request.headers.get('Origin');if(origin&&origin!==url.origin)return json({error:'Origem inválida.'},403);
  if(url.pathname==='/api/session'&&request.method==='POST'){
   let body;try{body=JSON.parse(await boundedText(request,512));}catch{throw new ImportError('Conexão inválida.');}
   if(!/^[a-f0-9]{64}$/.test(body.connection||''))throw new ImportError('Conexão inválida.');
   return Response.json({ok:true},{headers:{...security,'Set-Cookie':sessionCookie(body.connection)}});
  }
  const key=sessionKey(request);if(!key)return json({error:'Abra o portal no navegador para iniciar sua coleção.'},401);const owner=await ownerHash(key);
  if(url.pathname==='/api/connection'&&request.method==='GET')return json({endpoint:url.origin,key});
  if(url.pathname==='/api/items'&&request.method==='GET'){
   const paged = url.searchParams.has('offset');
   const offset = Number(url.searchParams.get('offset') || 0);
   if (!Number.isSafeInteger(offset) || offset < 0) throw new ImportError('Página inválida.');
   const size = paged ? 200 : 1000;
   const {results}=await env.DB.prepare('SELECT id,data,created_at FROM catalog_items WHERE owner_hash=? ORDER BY created_at DESC,id LIMIT ? OFFSET ?').bind(owner,size+1,offset).all();
   const hasMore = results.length > size;
   return json({items:results.slice(0,size).map(r=>({...JSON.parse(r.data),id:r.id,added:r.created_at})),limitReached:hasMore,nextOffset:hasMore?offset+size:null});
  }
  if(url.pathname==='/api/import'&&request.method==='POST'){
   const text=await boundedText(request,8192);let data;try{data=JSON.parse(text);}catch{throw new ImportError('Endereço inválido.');}
   if(typeof data.url!=='string'||data.url.length>4000)throw new ImportError('Cole o endereço da página.');
   const {items:imported,next,partial}=await importCatalog(data.url,{catalogPage:data.continuation===true});
   const statements=[];
   for(const item of imported){
    const key=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(owner+'|'+item.sourceUrl+'|'+item.title.toLowerCase()));const id=[...new Uint8Array(key)].map(b=>b.toString(16).padStart(2,'0')).join('');
    statements.push(env.DB.prepare('INSERT INTO catalog_items(id,owner_hash,source_url,title,data,created_at) VALUES(?,?,?,?,?,?) ON CONFLICT(owner_hash,source_url,title) DO UPDATE SET data=excluded.data').bind(id,owner,item.sourceUrl,item.title,JSON.stringify(item),new Date().toISOString()));
   }
   for(let start=0;start<statements.length;start+=40) await env.DB.batch(statements.slice(start,start+40));
   const count = imported.length;
   const savedLink = count === 1 && imported[0].linkOnly;
   const message = savedLink?'Salvamos o link da página. Nenhum filme foi identificado. Tente o endereço de um filme específico.':`${count} ${count===1?'título processado':'títulos processados'}.`;
   return json({count,next,message,partial});
  }
  const match=url.pathname.match(/^\/api\/items\/([a-f0-9]{64})(\/cover|\/downloads)?$/);
  if(match&&request.method==='DELETE'&&!match[2]){await env.DB.prepare('DELETE FROM catalog_items WHERE id=? AND owner_hash=?').bind(match[1],owner).run();return json({ok:true});}
  if(match&&match[2]==='/downloads'&&request.method==='GET'){
   const row=await env.DB.prepare('SELECT data FROM catalog_items WHERE id=? AND owner_hash=?').bind(match[1],owner).first();
   if(!row)return json({error:'Título não encontrado na coleção do portal.'},404);
   const item=JSON.parse(row.data);
   const options=await loadDownloads(item.sourceUrl);
   if(item.downloadUrl&&!options.some(option=>option.url===item.downloadUrl))
    options.unshift({url:item.downloadUrl,label:'Baixar arquivo',kind:'file'});
   return json({options});
  }
  if(match&&match[2]==='/cover'&&request.method==='GET'){
   const row=await env.DB.prepare('SELECT data FROM catalog_items WHERE id=? AND owner_hash=?').bind(match[1],owner).first();if(!row)return json({error:'Capa não encontrada.'},404);
   const item=JSON.parse(row.data);if(!item.coverUrl)return new Response(null,{status:404,headers:security});
   const {response}=await fetchPublic(item.coverUrl);if(!response.ok||!/^image\/(jpeg|png|webp|gif)(;|$)/i.test(response.headers.get('content-type')||'')){await response.body?.cancel();return new Response(null,{status:404,headers:security});}
   const data=await boundedTextBytes(response,5*1024*1024);return new Response(data,{headers:{...security,'Content-Type':response.headers.get('content-type'),'Cache-Control':'private, max-age=86400'}});
  }
  return json({error:'Operação não encontrada.'},404);
 }catch(e){if(e instanceof ImportError)return json({error:e.message},e.status);console.error(JSON.stringify({event:'request_failed',type:e?.name||'Error'}));return json({error:'Não foi possível concluir agora. Tente novamente.'},502);}
}};
async function boundedTextBytes(response,max){const reader=response.body.getReader();const chunks=[];let total=0;try{while(true){const {done,value}=await reader.read();if(done)break;total+=value.byteLength;if(total>max)throw new ImportError('Imagem grande demais.',413);chunks.push(value);}}finally{await reader.cancel();}const bytes=new Uint8Array(total);let offset=0;for(const c of chunks){bytes.set(c,offset);offset+=c.length;}return bytes;}
