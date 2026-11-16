import {DurableObject} from 'cloudflare:workers';
import {boundedText,ImportError,publicUrl} from './importer.mjs';
const encoder=new TextEncoder();
const hex=bytes=>[...new Uint8Array(bytes)].map(x=>x.toString(16).padStart(2,'0')).join('');
const random=()=>hex(crypto.getRandomValues(new Uint8Array(32)));
const digest=async value=>hex(await crypto.subtle.digest('SHA-256',encoder.encode(value)));
async function derive(password,salt){const key=await crypto.subtle.importKey('raw',encoder.encode(password),'PBKDF2',false,['deriveBits']);return hex(await crypto.subtle.deriveBits({name:'PBKDF2',salt:encoder.encode(salt),iterations:100000,hash:'SHA-256'},key,256));}
async function body(request,max=262144){try{return JSON.parse(await boundedText(request,max));}catch(e){if(e instanceof ImportError)throw e;throw new ImportError('Dados inválidos.');}}
const textKeys=['title','year','genre','seriesTitle','sourceUrl','streamUrl','coverUrl','tmdbUrl'];
function clean(input){if(!input||typeof input!=='object'||Array.isArray(input))throw new ImportError('Título inválido.');const out={};for(const key of textKeys){if(typeof input[key]!=='string'||input[key].length>2000)throw new ImportError('Metadados inválidos.');out[key]=input[key];}
 if(!out.title||out.title.length>500)throw new ImportError('Informe o título.');
 for(const key of ['sourceUrl','streamUrl','coverUrl','tmdbUrl'])if(out[key]&&!publicUrl(out[key]))throw new ImportError('Links privados não podem ser sincronizados.');
 for(const key of ['member','favorite','watched']){if(typeof input[key]!=='boolean')throw new ImportError('Estado inválido.');out[key]=input[key];}
 for(const key of ['resume','duration','season','episode']){if(!Number.isSafeInteger(input[key])||input[key]<0||input[key]>Number.MAX_SAFE_INTEGER/2)throw new ImportError('Progresso inválido.');out[key]=input[key];}
 return out;
}
const headers={'Cache-Control':'no-store','X-Content-Type-Options':'nosniff','Referrer-Policy':'no-referrer'};
const json=(value,status=200)=>Response.json(value,{status,headers});
function stub(env,name){return env.ACCOUNTS.get(env.ACCOUNTS.idFromName(name));}
export async function accountApi(request,env){
 const path=new URL(request.url).pathname,auth=['/api/account/register','/api/account/login'].includes(path)&&request.method==='POST';
 const ip=request.headers.get('CF-Connecting-IP')||'local';
 const rate=await stub(env,'rate:'+await digest(ip)+'|'+(auth?'auth':'sync')).fetch(new Request('https://accounts.internal/rate',{method:'POST',body:JSON.stringify({limit:auth?12:240})}));
 if(!rate.ok)return rate;
 let username;
 if(auth){const input=await body(request.clone(),1024);username=String(input.username||'').toLowerCase().trim();if(!/^[a-z0-9_.-]{3,40}$/.test(username))throw new ImportError('Use um nome de 3–40 letras ou números.');}
 else{const token=(request.headers.get('Authorization')||'').replace(/^Bearer /,'');const match=token.match(/^([a-z0-9_.-]{3,40})\.([a-f0-9]{64})$/);if(!match)return json({error:'Entre na sua conta.'},401);username=match[1];}
 return stub(env,'account:'+username).fetch(request);
}
export class AccountStore extends DurableObject {
 constructor(ctx,env){super(ctx,env);this.sql=ctx.storage.sql;ctx.blockConcurrencyWhile(async()=>{
  this.sql.exec('CREATE TABLE IF NOT EXISTS identity (id INTEGER PRIMARY KEY CHECK(id=1), username TEXT NOT NULL, salt TEXT NOT NULL, verifier TEXT NOT NULL)');
  this.sql.exec('CREATE TABLE IF NOT EXISTS sessions (hash TEXT PRIMARY KEY, expires_at INTEGER NOT NULL)');
  this.sql.exec('CREATE INDEX IF NOT EXISTS sessions_expiry ON sessions(expires_at)');
  this.sql.exec('CREATE TABLE IF NOT EXISTS titles (id TEXT PRIMARY KEY, data TEXT NOT NULL, version INTEGER NOT NULL)');
  this.sql.exec('CREATE TABLE IF NOT EXISTS rates (window INTEGER PRIMARY KEY, count INTEGER NOT NULL)');
 });}
 one(query,...args){return this.sql.exec(query,...args).toArray()[0];}
 async fetch(request){try{return await this.handle(request);}catch(e){if(e instanceof ImportError)return json({error:e.message},e.status);console.error(JSON.stringify({event:'account_failed',type:e?.name||'Error'}));return json({error:'Não foi possível acessar a conta agora. Tente novamente.'},503);}}
 async handle(request){const path=new URL(request.url).pathname;
  if(path==='/rate'){
   const {limit}=await request.json(),window=Math.floor(Date.now()/60000);
   const row=this.one('INSERT INTO rates(window,count) VALUES(?,1) ON CONFLICT(window) DO UPDATE SET count=count+1 RETURNING count',window);
   this.sql.exec('DELETE FROM rates WHERE window<?',window-1);
   return row.count>limit?json({error:'Muitas tentativas. Aguarde um minuto.'},429):json({ok:true});
  }
  if(['/api/account/register','/api/account/login'].includes(path)&&request.method==='POST'){
   const input=await body(request,1024),username=String(input.username||'').toLowerCase().trim(),password=input.password;
   if(!/^[a-z0-9_.-]{3,40}$/.test(username)||typeof password!=='string'||password.length<12||password.length>128)throw new ImportError('Use um nome de 3–40 letras/números e uma senha de 12–128 caracteres.');
   const account=this.one('SELECT * FROM identity WHERE id=1');
   if(path.endsWith('/register')){
    if(account)throw new ImportError('Nome de conta indisponível.',409);
    const salt=random(),verifier=await derive(password,salt);
    // Re-check after Web Crypto yields; another registration may have completed.
    if(this.one('SELECT id FROM identity WHERE id=1'))throw new ImportError('Nome de conta indisponível.',409);
    this.sql.exec('INSERT INTO identity(id,username,salt,verifier) VALUES(1,?,?,?)',username,salt,verifier);
   }else{const candidate=await derive(password,account?.salt||'0'.repeat(64));if(!account||!crypto.subtle.timingSafeEqual(encoder.encode(candidate),encoder.encode(account.verifier)))throw new ImportError('Nome ou senha incorretos.',401);}
   const token=username+'.'+random();this.sql.exec('DELETE FROM sessions WHERE expires_at<?',Date.now());this.sql.exec('INSERT INTO sessions(hash,expires_at) VALUES(?,?)',await digest(token),Date.now()+90*86400000);return json({token,username});
  }
  const token=(request.headers.get('Authorization')||'').replace(/^Bearer /,'');
  if(!this.one('SELECT hash FROM sessions WHERE hash=? AND expires_at>?',await digest(token),Date.now()))return json({error:'Sua sessão expirou. Entre novamente.'},401);
  if(path==='/api/account/logout'&&request.method==='POST'){this.sql.exec('DELETE FROM sessions WHERE hash=?',await digest(token));return json({ok:true});}
  if(path==='/api/account/delete'&&request.method==='POST'){
   const input=await body(request,1024),account=this.one('SELECT * FROM identity WHERE id=1');
   if(typeof input.password!=='string'||input.password.length>128)throw new ImportError('Confira sua senha.');
   const candidate=await derive(input.password,account.salt);
   if(!crypto.subtle.timingSafeEqual(encoder.encode(candidate),encoder.encode(account.verifier)))throw new ImportError('Senha incorreta.',401);
   this.ctx.storage.transactionSync(()=>{this.sql.exec("DELETE FROM titles");this.sql.exec("DELETE FROM sessions");this.sql.exec("DELETE FROM identity");});return json({ok:true});
  }
  if(path==='/api/account/me'&&request.method==='GET')return json({username:this.one('SELECT username FROM identity WHERE id=1').username});
  if(path==='/api/account/titles'&&request.method==='GET'){
   const after=new URL(request.url).searchParams.get('after')||'',rows=this.sql.exec('SELECT id,data,version FROM titles WHERE id>? ORDER BY id LIMIT 51',after).toArray();
   return json({items:rows.slice(0,50).map(r=>({id:r.id,data:JSON.parse(r.data),version:r.version})),next:rows.length>50?rows[49].id:''});
  }
  if(path==='/api/account/titles'&&request.method==='POST'){
   const input=await body(request);if(!Array.isArray(input.items)||input.items.length>40)throw new ImportError('Lote inválido.');
   const prepared=input.items.map(item=>{if(!/^[a-f0-9]{64}$/.test(item.id)||!Number.isSafeInteger(item.version)||item.version<0)throw new ImportError('Versão inválida.');return {...item,data:clean(item.data)};});
   const items=prepared.map(item=>{
    const row=item.version===0?this.one('INSERT INTO titles(id,data,version) VALUES(?,?,1) ON CONFLICT(id) DO NOTHING RETURNING version',item.id,JSON.stringify(item.data)):
      this.one('UPDATE titles SET data=?,version=version+1 WHERE id=? AND version=? RETURNING version',JSON.stringify(item.data),item.id,item.version);
    return {id:item.id,version:row?.version||0,conflict:!row};
   });return json({items});
  }
  return json({error:'Operação de conta não encontrada.'},404);
 }
}
