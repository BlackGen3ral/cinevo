export class ImportError extends Error { constructor(message,status=400){super(message);this.status=status;} }
export function publicUrl(value,base){
 let u;try{u=new URL(value,base);}catch{throw new ImportError('Endereço inválido. Cole um link HTTPS completo.');}
 const h=u.hostname.toLowerCase().replace(/\.$/,'');
 if(u.protocol!=='https:'||u.username||u.password||u.port&&u.port!=='443'||!h.includes('.')||h.includes(':')||/^\d+[.\d]*$/.test(h)||/(^|\.)(localhost|local|internal|lan|home|test|invalid|example)$/.test(h))throw new ImportError('Use um endereço HTTPS público, sem senha no link.');
 u.hash=''; return u;
}
export function publicIp(ip){
 if(ip.includes(':')) return /^(2|3)[0-9a-f]{0,3}:/i.test(ip)&&!ip.toLowerCase().startsWith('2001:db8:');
 const p=ip.split('.').map(Number);if(p.length!==4||p.some(n=>!Number.isInteger(n)||n<0||n>255))return false;
 const [a,b]=p;return !(a===0||a===10||a===127||a===169&&b===254||a===172&&b>=16&&b<=31||a===192&&(b===168||b===0||b===2)||a===100&&b>=64&&b<=127||a===198&&(b===18||b===19||b===51)||a===203&&b===0||a>=224);
}
export async function checkDns(url,fetcher=fetch){
 for(const type of ['A','AAAA']){
  const r=await fetcher('https://cloudflare-dns.com/dns-query?name='+encodeURIComponent(url.hostname)+'&type='+type,{headers:{Accept:'application/dns-json'},signal:AbortSignal.timeout(6000)});
  if(!r.ok)throw new ImportError('Não foi possível validar a origem.',502);
  const j=await r.json();if(j.Status!==0&&j.Status!==3)throw new ImportError('Não foi possível resolver o site.',502);
  const addresses=(j.Answer||[]).filter(a=>a.type===1||a.type===28);if(addresses.some(a=>!publicIp(a.data)))throw new ImportError('A origem aponta para uma rede privada.');
 }
}
export async function fetchPublic(value,{method='GET',headers={},fetcher=fetch,validate=checkDns}={}){
 let url=publicUrl(value);
 for(let i=0;i<5;i++){
  await validate(url,fetcher);
  const response=await fetcher(url.href,{method,headers:{'User-Agent':'CinevoPortal/0.1 (+https://github.com/BlackGen3ral/cinevo)',...headers},redirect:'manual',signal:AbortSignal.timeout(15000)});
  if([301,302,303,307,308].includes(response.status)){await response.body?.cancel();url=publicUrl(response.headers.get('location'),url);continue;}
  return {response,url};
 }throw new ImportError('O site redirecionou muitas vezes.',422);
}
export async function boundedText(response,max=2*1024*1024){
 if(Number(response.headers.get('content-length'))>max){await response.body?.cancel();throw new ImportError('A página é grande demais para importar.',413);}
 const reader=response.body?.getReader();if(!reader)return '';let total=0;const chunks=[];try{while(true){const {done,value}=await reader.read();if(done)break;total+=value.byteLength;if(total>max)throw new ImportError('A página é grande demais para importar.',413);chunks.push(value);}}finally{await reader.cancel();}
 const data=new Uint8Array(total);let offset=0;for(const c of chunks){data.set(c,offset);offset+=c.length;}return new TextDecoder().decode(data);
}
function entities(value){return value.replace(/&amp;/gi,'&').replace(/&quot;/gi,'"').replace(/&#39;|&apos;/gi,"'").replace(/&lt;/gi,'<').replace(/&gt;/gi,'>');}
function safe(value,base){if(typeof value!=='string'||!value.trim())return '';try{return publicUrl(entities(value),base).href;}catch{return '';}}
function clean(value,max=800){return typeof value==='string'?entities(value.replace(/<[^>]*>/g,' ')).replace(/\s+/g,' ').trim().slice(0,max):'';}
export function mediaUrl(value,base){const url=safe(value,base);return url&&/\.(mp4|mkv|webm|mov|m4v|avi|m3u8|mpd)$/i.test(new URL(url).pathname)?url:'';}
const filmPath = /\/(?:filme?s?|movies?|film|titulo|title|watch|tv|series|epis[oó]dios?|episodes?)\//i;
const listingPath = /\/(?:category|categoria|tag|genre|genero|page|pagina|search|busca)\//i;

async function catalogCards(html, pageUrl, Rewriter) {
 const cards = new Map();
 let card, heading;
 const parser = new Rewriter()
  .on('a[href]', {
   element(element) {
    const sourceUrl = safe(element.getAttribute('href'), pageUrl);
    if (!sourceUrl) return;
    const target = new URL(sourceUrl);
    if (target.origin !== new URL(pageUrl).origin || sourceUrl === pageUrl || listingPath.test(target.pathname) || mediaUrl(sourceUrl, pageUrl)) return;
    card = { sourceUrl, title: '', image: '', alt: '', label: '', heading: '' };
    card.label = clean(element.getAttribute('title') || element.getAttribute('aria-label'), 240);
    element.onEndTag(() => {
     const name = clean(card.heading || card.label || card.alt || card.title, 240);
     if (name && !/^(?:assistir|ver filme|saiba mais|ver mais|poster|capa|play|home|início|filmes|séries|download)$/i.test(name) &&
      (card.image || filmPath.test(target.pathname)) && (filmPath.test(target.pathname) || card.heading || card.alt)) {
      const year = name.match(/\b(19\d{2}|20\d{2})\s*\)?$/)?.[1] || '';
      const item = { title: name, description: '', year, coverUrl: card.image, sourceUrl, streamUrl: '', downloadUrl: '', importedFrom: pageUrl };
      if ((!cards.has(sourceUrl) && cards.size < 40) || (cards.has(sourceUrl) && !cards.get(sourceUrl).coverUrl && item.coverUrl)) cards.set(sourceUrl, item);
     }
     card = undefined;
    });
   },
   text(part) { if (card && card.title.length < 300) card.title += part.text; }
  })
  .on('a[href] img', { element(element) {
   if (!card) return;
   card.alt = clean(element.getAttribute('alt'), 240);
   const srcset = element.getAttribute('data-srcset') || element.getAttribute('srcset') || '';
   card.image = safe(element.getAttribute('data-src') || element.getAttribute('data-lazy-src') ||
    element.getAttribute('data-original') || srcset.trim().split(/\s+/)[0] || element.getAttribute('src'), pageUrl);
  } })
  .on('a[href] h2, a[href] h3, a[href] h4', {
   element(element) { heading = card; element.onEndTag(() => { heading = undefined; }); },
   text(part) { if (heading && heading.heading.length < 300) heading.heading += part.text; }
  });
 await parser.transform(new Response(html)).text();
 return [...cards.values()];
}
export async function parsePage(html,url,Rewriter=HTMLRewriter){
 const meta={},scripts=[],downloads=[],videos=[];let title='',script=null;
 const parser=new Rewriter().on('meta',{element(el){const key=(el.getAttribute('property')||el.getAttribute('name')||'').toLowerCase();if(key&&!meta[key])meta[key]=el.getAttribute('content')||'';}})
 .on('title',{text(t){title+=t.text;}})
 .on('script[type="application/ld+json"]',{element(el){script={text:''};scripts.push(script);el.onEndTag(()=>{script=null;});},text(t){if(script)script.text+=t.text;}})
 .on('video,video source',{element(el){const src=mediaUrl(el.getAttribute('src'),url);if(src)videos.push(src);}})
 .on('a[download]',{element(el){const src=mediaUrl(el.getAttribute('href'),url);if(src&&!/\.(m3u8|mpd)$/i.test(new URL(src).pathname))downloads.push(src);}});
 await parser.transform(new Response(html)).text();
 const nodes=[];function visit(n,depth=0){if(!n||typeof n!=='object'||depth>12||nodes.length>500)return;if(Array.isArray(n)){for(const item of n)visit(item,depth+1);return;}nodes.push(n);for(const key of ['@graph','itemListElement','item','mainEntity','video'])visit(n[key],depth+1);}
 for(const s of scripts){try{visit(JSON.parse(s.text));}catch{}}
 const movies=nodes.filter(n=>[n['@type']].flat().some(t=>['Movie','TVSeries','TVEpisode'].includes(t)));
 const structured=movies.length?movies:nodes.filter(n=>[n['@type']].flat().includes('VideoObject'));
 const items=[];
 for(const n of structured.slice(0,40)){
  const video=[n.video].flat().find(v=>v&&typeof v==='object')||{};
  const stream=mediaUrl(n.contentUrl||video.contentUrl,url);
  const source=safe(n.url,url)||url;
  const poster=[n.image||n.thumbnailUrl||video.thumbnailUrl].flat()[0];
  items.push({title:clean(n.name,240),description:clean(n.description),year:String(n.datePublished||'').match(/\b(19\d{2}|20\d{2})\b/)?.[0]||'',coverUrl:safe(typeof poster==='object'?poster?.url:poster,url),sourceUrl:source,streamUrl:stream,downloadUrl:downloads.find(d=>d===stream)||'',importedFrom:url});
 }
 if (!items.length && !videos.length && !downloads.length && !/^video\./i.test(meta['og:type'] || '') && !meta['og:video'] && !meta['og:video:secure_url']) {
  const cards = await catalogCards(html, url, Rewriter);
  if (cards.length >= 2) return cards;
 }
 if(!items.length||items.length===1){
  const i=items[0]||{sourceUrl:url,importedFrom:url};
  i.title=i.title||clean(meta['og:title']||meta['twitter:title']||title,240);
  i.description=i.description||clean(meta['og:description']||meta.description);
  i.coverUrl=i.coverUrl||safe(meta['og:image']||meta['twitter:image'],url);
  i.streamUrl=i.streamUrl||mediaUrl(meta['og:video:secure_url']||meta['og:video']||videos[0]||downloads[0],url);
  i.downloadUrl=i.downloadUrl||downloads[0]||'';i.year=i.year||'';
  i.linkOnly = !items.length && !i.streamUrl && !/^video\./i.test(meta['og:type'] || '');
  if(i.title)items.splice(0,items.length,i);
 }
 const unique=new Map();for(const item of items){if(!item.title)continue;const key=item.sourceUrl+'|'+item.title.toLocaleLowerCase();if(!unique.has(key))unique.set(key,item);}
 return [...unique.values()];
}
export async function importAddress(value,options={}){
 const initial=publicUrl(value);const {response,url}=await fetchPublic(initial.href,options);
 if(!response.ok){await response.body?.cancel();throw new ImportError(response.status===401||response.status===403?'O site exige acesso ou bloqueou a importação. Abra-o na origem.':'O site não respondeu com uma página disponível.',422);}
 const type=response.headers.get('content-type')||'';
 if(type.startsWith('video/')||mediaUrl(url.href,url)&&!type.includes('html')){
  await response.body?.cancel();const isFile=!/\.(m3u8|mpd)$/i.test(url.pathname);
  return [{title:clean(decodeURIComponent(url.pathname.split('/').pop()||url.hostname).replace(/\.[^.]+$/,'').replace(/[._]/g,' '),240),description:'Vídeo disponibilizado diretamente pela origem.',year:'',coverUrl:'',sourceUrl:url.href,streamUrl:url.href,downloadUrl:isFile?url.href:'',importedFrom:initial.href}];
 }
 if(!/text\/html|application\/xhtml/.test(type)){await response.body?.cancel();throw new ImportError('Este endereço não contém uma página de filme ou vídeo direto.',422);}
 const html=await boundedText(response);const items=await parsePage(html,url.href,options.Rewriter);
 if(!items.length)throw new ImportError('Não encontramos informações públicas nessa página. Sites que dependem de login ou carregamento por scripts podem não ser compatíveis.',422);
 return items;
}
