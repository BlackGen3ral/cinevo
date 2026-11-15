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
function entities(value){return value.replace(/&amp;/gi,'&').replace(/&quot;/gi,'"').replace(/&#39;|&apos;/gi,"'").replace(/&lt;/gi,'<').replace(/&gt;/gi,'>').replace(/&nbsp;/gi,' ').replace(/&#(?:x([0-9a-f]+)|(\d+));/gi,(_,hex,decimal)=>{const point=parseInt(hex||decimal,hex?16:10);return point>0&&point<=0x10ffff?String.fromCodePoint(point):'';});}
function safe(value,base){if(typeof value!=='string'||!value.trim())return '';try{return publicUrl(entities(value),base).href;}catch{return '';}}
function clean(value,max=800){return typeof value==='string'?entities(value.replace(/<[^>]*>/g,' ')).replace(/\s+/g,' ').trim().slice(0,max):'';}
export function mediaUrl(value,base){const url=safe(value,base);return url&&/\.(mp4|mkv|webm|mov|m4v|avi|m3u8|mpd)$/i.test(new URL(url).pathname)?url:'';}
export async function downloadOptions(html, url, Rewriter = HTMLRewriter) {
 const options = new Map();
 let current;
 const parser = new Rewriter().on('a[href]', {
  element(element) {
   const href = entities(element.getAttribute('href') || '');
   if (!href) return;
   const magnet = /^magnet:\?xt=urn:btih:(?:[a-f0-9]{40}|[a-z2-7]{32})(?:&|$)/i.test(href);
   const address = magnet ? href : safe(href, url);
   if (!address || address.length > 4096) return;
   const explicit = element.hasAttribute('download');
   const label = element.getAttribute('aria-label') || element.getAttribute('title') || '';
   const entry = { address, label, text: '' };
   current = entry;
   element.onEndTag(() => {
    if (current === entry) current = undefined;
    const name = clean(entry.text, 160) || clean(label, 160);
    const offered = explicit || /\b(?:download|baixar|\d{3,4}p)\b/i.test(name);
    if (!magnet && !offered) return;
    const direct = !magnet && !!mediaUrl(address, url) && !/\.(?:m3u8|mpd)$/i.test(new URL(address).pathname);
    if (options.size < 20) options.set(address, { url: address, label: name || (magnet ? 'Torrent' : 'Baixar arquivo'), kind: magnet ? 'torrent' : direct ? 'file' : 'link' });
   });
  },
  text(part) { if (current && current.text.length < 300) current.text += part.text; }
 });
 await parser.transform(new Response(html)).text();
 return [...options.values()];
}

async function nextCatalogPage(html, url, Rewriter = HTMLRewriter) {
 const current = new URL(url);
 const number = address => {
  const target = new URL(address);
  return Number(target.searchParams.get('paged') || target.searchParams.get('page') ||
   target.pathname.match(/\/(?:page|pagina)\/(\d+)\/?$/i)?.[1] || 1);
 };
 let next = '', numbered = '';
 const navigation = '.pagination, .paginacao, .wp-pagenavi, .nav-links';
 const candidateFor = href => {
  const address = safe(href, url);
  return address && address !== url && new URL(address).origin === current.origin ? address : '';
 };
 const parser = new Rewriter().on('a[rel~="next"], link[rel~="next"], a.nextpostslink, a.next.page-numbers, .pagination a.next, a[aria-label="Próxima página"]', {
  element(element) {
   next ||= candidateFor(element.getAttribute('href'));
  }
 }).on(navigation.split(', ').map(selector => selector + ' a[href]').join(', '), {
  element(element) {
   const address = candidateFor(element.getAttribute('href'));
   if (address && number(address) === number(url) + 1) numbered ||= address;
  }
 });
 await parser.transform(new Response(html)).text();
 return next || numbered;
}
const filmPath = /\/(?:filme?s?|movies?|film|titulo|title|watch|tv|series|epis[oó]dios?|episodes?)\//i;
const listingPath = /\/(?:category|categoria|tag|genre|genero|page|pagina|search|busca)\//i;
const seriesPath = /\/(?:tv|series|séries|epis[oó]dios?|episodes?)\//i;

function classify(item, type, data = {}) {
 const name = item.title.replace(/\s*\(?\b(?:19|20)\d{2}\)?\s*$/, '').trim();
 const season = name.match(/\b(?:temporada\s*(\d{1,3})|(\d{1,3})\s*[ªºoa]?\s*temporada|(?:season\s*|s)(\d{1,3})(?=\b|e\d))/i);
 const episode = name.match(/(?:\bepis[oó]dio\s*|\bepisode\s*|\bs\d{1,3}e)(\d{1,4})\b/i);
 const number = value => /^\d{1,4}$/.test(String(value ?? '')) ? Number(value) : null;
 const structuredSeries = ['TVSeries', 'TVSeason', 'TVEpisode'].includes(type);
 const isSeries = structuredSeries || type !== 'Movie' && (season || episode || seriesPath.test(new URL(item.sourceUrl).pathname));
 if (!isSeries) return {...item, mediaType: type === 'Movie' ? 'movie' : ''};
 const suffix = Math.min(season?.index ?? name.length, episode?.index ?? name.length);
 const seriesTitle = clean(data.partOfSeries?.name || data.partOfSeason?.partOfSeries?.name, 240)
  || name.slice(0, suffix).replace(/[\s:–—-]+$/, '') || name;
 return {...item, mediaType: 'series', seriesTitle,
  season: number(data.seasonNumber ?? data.partOfSeason?.seasonNumber ?? (season && (season[1] || season[2] || season[3]))),
  episode: number(data.episodeNumber ?? episode?.[1])};
}

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
    const entry = card = { sourceUrl, title: '', image: '', alt: '', label: '', heading: '' };
    card.label = clean(element.getAttribute('title') || element.getAttribute('aria-label'), 240);
    element.onEndTag(() => {
     const name = clean(entry.heading || entry.alt || entry.label || entry.title, 240);
     if (name && !/^(?:assistir|ver filme|saiba mais|ver mais|poster|capa|play|home|início|filmes|séries|download)$/i.test(name) &&
      (entry.image || filmPath.test(target.pathname)) && (filmPath.test(target.pathname) || entry.heading || entry.alt)) {
      const year = name.match(/\b(19\d{2}|20\d{2})\s*\)?$/)?.[1] || '';
      const item = { title: name, description: '', year, coverUrl: entry.image, sourceUrl, streamUrl: '', downloadUrl: '', importedFrom: pageUrl };
      if (!cards.has(sourceUrl) || !cards.get(sourceUrl).coverUrl && item.coverUrl) cards.set(sourceUrl, item);
     }
     if (card === entry) card = undefined;
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
  }).on('h2 a[href], h3 a[href], h4 a[href]', {
   text(part) { if (card && card.heading.length < 300) card.heading += part.text; }
  });
 await parser.transform(new Response(html)).text();
 return [...cards.values()].map(item => classify(item));
}
export async function parsePage(html,url,Rewriter=HTMLRewriter, catalogPage=false){
 const meta={},scripts=[],downloads=[],videos=[],headings=[];let title='',script=null,heading;
 const parser=new Rewriter().on('meta',{element(el){const key=(el.getAttribute('property')||el.getAttribute('name')||'').toLowerCase();if(key&&!meta[key])meta[key]=el.getAttribute('content')||'';}})
 .on('title',{text(t){title+=t.text;}})
 .on('main h1, article h1',{element(el){heading={text:''};headings.push(heading);el.onEndTag(()=>{heading=undefined;});},text(t){if(heading&&heading.text.length<300)heading.text+=t.text;}})
 .on('script[type="application/ld+json"]',{element(el){script={text:''};scripts.push(script);el.onEndTag(()=>{script=null;});},text(t){if(script)script.text+=t.text;}})
 .on('video,video source',{element(el){const src=mediaUrl(el.getAttribute('src'),url);if(src)videos.push(src);}})
 .on('a[download]',{element(el){const src=mediaUrl(el.getAttribute('href'),url);if(src&&!/\.(m3u8|mpd)$/i.test(new URL(src).pathname))downloads.push(src);}});
 await parser.transform(new Response(html)).text();
 const nodes=[];function visit(n,depth=0){if(!n||typeof n!=='object'||depth>12||nodes.length>500)return;if(Array.isArray(n)){for(const item of n)visit(item,depth+1);return;}nodes.push(n);for(const key of ['@graph','itemListElement','item','mainEntity','video'])visit(n[key],depth+1);}
 for(const s of scripts){try{visit(JSON.parse(s.text));}catch{}}
 const filmTypes = ['Movie','TVSeries','TVSeason','TVEpisode'];
 const movies=nodes.filter(n=>[n['@type']].flat().some(t=>filmTypes.includes(t)));
 const structured=movies.length?movies:nodes.filter(n=>[n['@type']].flat().includes('VideoObject'));
 const detailPage = /^(?:article|video\.)/i.test(meta['og:type'] || '');
 const items=[];
 for(const n of structured){
  const video=[n.video].flat().find(v=>v&&typeof v==='object')||{};
  const stream=mediaUrl(n.contentUrl||video.contentUrl,url);
  const source=safe(n.url,url)||url;
  const poster=[n.image||n.thumbnailUrl||video.thumbnailUrl].flat()[0];
  const item = {title:clean(n.name,240),description:clean(n.description),year:String(n.datePublished||'').match(/\b(19\d{2}|20\d{2})\b/)?.[0]||'',coverUrl:safe(typeof poster==='object'?poster?.url:poster,url),sourceUrl:source,streamUrl:stream,downloadUrl:downloads.find(d=>d===stream)||'',importedFrom:url};
  items.push(classify(item, [n['@type']].flat().find(t => filmTypes.includes(t)), n));
 }
 if ((catalogPage || !items.length) && !videos.length && !downloads.length && !detailPage && !meta['og:video'] && !meta['og:video:secure_url']) {
  const cards = await catalogCards(html, url, Rewriter);
  if (cards.length >= 2 || catalogPage && cards.length) {
   const byUrl = new Map(cards.map(card => [card.sourceUrl, card]));
   for (const item of items) {
    if (!item.title || item.sourceUrl === url) continue;
    const card = byUrl.get(item.sourceUrl);
    byUrl.set(item.sourceUrl, card ? mergeItem(card, item) : item);
   }
   return [...byUrl.values()];
  }
 }
 if(!items.length||items.length===1){
  const i=items[0]||{sourceUrl:url,importedFrom:url};
  const detailHeading=detailPage&&headings.map(h=>clean(h.text,240)).find(Boolean);
  i.title=i.title||detailHeading||clean(meta['og:title']||meta['twitter:title']||title,240);
  i.description=i.description||clean(meta['og:description']||meta.description);
  i.coverUrl=i.coverUrl||safe(meta['og:image']||meta['twitter:image'],url);
  i.streamUrl=i.streamUrl||mediaUrl(meta['og:video:secure_url']||meta['og:video']||videos[0]||downloads[0],url);
  i.downloadUrl=i.downloadUrl||downloads[0]||'';i.year=i.year||'';
  i.linkOnly = !items.length && !i.streamUrl && !(detailPage && (meta['og:title'] || meta['og:image']));
  if(i.title)items.splice(0,items.length,i.mediaType ? i : classify(i, /^video\.(?:episode|tv_show)$/i.test(meta['og:type']) ? 'TVEpisode' : null));
 }
 const unique=new Map();for(const item of items){if(!item.title)continue;const key=item.sourceUrl+'|'+item.title.toLocaleLowerCase();if(!unique.has(key))unique.set(key,item);}
 return [...unique.values()];
}
function mergeItem(previous, item) {
 if (!previous) return item;
 return {...previous, ...item, description: item.description || previous.description, coverUrl: item.coverUrl || previous.coverUrl,
  year: item.year || previous.year, streamUrl: item.streamUrl || previous.streamUrl, downloadUrl: item.downloadUrl || previous.downloadUrl,
  mediaType: item.mediaType || previous.mediaType, seriesTitle: item.seriesTitle || previous.seriesTitle,
  season: item.season ?? previous.season, episode: item.episode ?? previous.episode };
}
function wordpressPostsUrl(url) {
 return /\/wp-json\/wp\/v2\/posts\/?$/.test(url.pathname) || url.searchParams.get('rest_route') === '/wp/v2/posts';
}

async function wordpressPage(response, url) {
 let posts;
 try { posts = JSON.parse(await boundedText(response)); }
 catch { throw new ImportError('A listagem pública do site retornou dados inválidos.',422); }
 if (!Array.isArray(posts)) throw new ImportError('Este endereço não contém uma listagem pública de filmes.',422);
 const items = posts.map(post => {
  const sourceUrl = safe(post.link, url);
  if (!sourceUrl || new URL(sourceUrl).origin !== url.origin || !Number.isSafeInteger(post.id)) return null;
  const title = clean(post.title?.rendered, 240);
  if (!title) return null;
  return {...classify({title, sourceUrl, description:clean(post.excerpt?.rendered),
   year:title.match(/\(((?:19|20)\d{2})\)/)?.[1] || title.match(/\b((?:19|20)\d{2})\b/)?.[1] || '',
   coverUrl:safe(post._embedded?.['wp:featuredmedia']?.[0]?.source_url,url),
   importedFrom:url.origin+'/',streamUrl:'',downloadUrl:''}),canonicalSource:true};
 }).filter(Boolean);
 const page = Number(url.searchParams.get('page') || 1);
 const pages = Number(response.headers.get('X-WP-TotalPages'));
 const next = new URL(url);
 next.searchParams.set('page',String(page+1));
 return {items,next:Number.isSafeInteger(pages) && page < pages ? next.href : '',total:Number(response.headers.get('X-WP-Total')) || 0};
}

async function importPage(value,options={}){
 const initial=publicUrl(value);const {response,url}=await fetchPublic(initial.href,options);
 if(!response.ok){await response.body?.cancel();throw new ImportError(response.status===401||response.status===403?'O site exige acesso ou bloqueou a importação. Abra-o na origem.':'O site não respondeu com uma página disponível.',422);}
 const type=response.headers.get('content-type')||'';
 if (wordpressPostsUrl(url) && /application\/json/.test(type)) return wordpressPage(response,url);
 if(type.startsWith('video/')||mediaUrl(url.href,url)&&!type.includes('html')){
  await response.body?.cancel();const isFile=!/\.(m3u8|mpd)$/i.test(url.pathname);
  return {items:[{title:clean(decodeURIComponent(url.pathname.split('/').pop()||url.hostname).replace(/\.[^.]+$/,'').replace(/[._]/g,' '),240),description:'Vídeo disponibilizado diretamente pela origem.',year:'',coverUrl:'',sourceUrl:url.href,streamUrl:url.href,downloadUrl:isFile?url.href:'',importedFrom:initial.href}],next:''};
 }
 if(!/text\/html|application\/xhtml/.test(type)){await response.body?.cancel();throw new ImportError('Este endereço não contém uma página de filme ou vídeo direto.',422);}
 const html=await boundedText(response);
 if (url.pathname === '/' && !url.search) {
  let api;
  await new (options.Rewriter || HTMLRewriter)().on('link[rel="https://api.w.org/"]', {element(element) {
   const address = safe(element.getAttribute('href'),url);
   if (address && new URL(address).origin === url.origin) api = address;
  }}).transform(new Response(html)).text();
  if (api) {
   const endpoint = new URL(api);
   endpoint.pathname = endpoint.pathname.replace(/\/$/,'') + '/wp/v2/posts';
   endpoint.search = new URLSearchParams({per_page:'50',page:'1',_embed:'wp:featuredmedia',_fields:'id,link,title,excerpt,_links,_embedded'}).toString();
   try {
    const feed = await fetchPublic(endpoint.href, options);
    if (feed.response.ok && /application\/json/.test(feed.response.headers.get('content-type') || '')) {
     const parsed = await wordpressPage(feed.response,feed.url);
     if (parsed.items.length) return parsed;
    } else await feed.response.body?.cancel();
   } catch { /* Keep importing the visible catalog when the optional public API is unavailable. */ }
  }
 }
 const pagination = await nextCatalogPage(html, url.href, options.Rewriter);
 const items=await parsePage(html,url.href,options.Rewriter, options.catalogPage || !!pagination);
 if(!items.length)throw new ImportError('Não encontramos informações públicas nessa página. Sites que dependem de login ou carregamento por scripts podem não ser compatíveis.',422);
 const next = items.some(item => item.linkOnly) || items.length > 1 || options.catalogPage || items[0]?.sourceUrl !== url.href && !items[0]?.streamUrl ? pagination : '';
 return {items,next};
}

export async function importAddress(value, options = {}) {
 return (await importPage(value,options)).items;
}

export async function importCatalog(value, options = {}) {
 const items = new Map();
 const visited = new Set();
 let next = publicUrl(value).href;
 let partial = '';
 let total = 0;
 while (next && !visited.has(next) && visited.size < 4) {
  const address = next;
  visited.add(address);
  let page;
  try { page = await importPage(address, {...options,catalogPage: options.catalogPage || visited.size > 1}); }
  catch (error) {
   if (!items.size) throw error;
   partial = error instanceof ImportError ? error.message : 'A página seguinte não respondeu.';
   break;
  }
  if (page.items.every(item => item.linkOnly) && (options.catalogPage || visited.size > 1 || page.next)) {
   partial = 'Nenhum título foi identificado nesta página do catálogo.';
   break;
  }
  total = Math.max(total,page.total || 0);
  for (const item of page.items) {
   if (!item.linkOnly || visited.size === 1 && !page.next && !options.catalogPage) {
    const key = item.sourceUrl + '|' + item.title.toLowerCase();
    items.set(key, mergeItem(items.get(key), item));
   }
  }
  next = page.next;
 }
 if (!items.size) throw new ImportError('Nenhum filme foi identificado nas páginas disponíveis. Tente o link de um filme específico.',422);
 if (visited.has(next) && !partial) {
  partial = 'O site repetiu uma página do catálogo.';
  next = '';
 }
 return {items:[...items.values()],next,partial,total};
}

export async function loadDownloads(value,options = {}) {
 const {response,url} = await fetchPublic(value,options);
 const type = response.headers.get('content-type') || '';
 if (!response.ok || !/text\/html|application\/xhtml/.test(type)) {
  await response.body?.cancel();
  return [];
 }
 return downloadOptions(await boundedText(response),url.href,options.Rewriter);
}
