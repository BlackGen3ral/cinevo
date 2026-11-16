const transferCards = new Map();
let transferFilter = 'all';
let transferRequest;
let transferRefreshQueued = false;

async function transferAction(name, id, address, downloadFolder) {
  const response = await request('/api/action', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, id, address, downloadFolder })
  });
  return response.status === 204 ? null : response.json();
}

function storageSize(value) {
  const sizes = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
  const bytes = Math.max(0, value || 0);
  const unit = bytes ? Math.min(4, Math.floor(Math.log(bytes) / Math.log(1024))) : 0;
  return (bytes / 1024 ** unit).toLocaleString('pt-BR', { maximumFractionDigits: unit ? 1 : 0 }) + ' ' + sizes[unit];
}

function timeLeft(seconds) {
  if (!Number.isFinite(seconds) || seconds < 0 || seconds >= 8640000) return 'Sem estimativa';
  if (seconds >= 86400) return Math.floor(seconds / 86400) + ' d ' + Math.floor(seconds % 86400 / 3600) + ' h';
  if (seconds >= 3600) return Math.floor(seconds / 3600) + ' h ' + Math.floor(seconds % 3600 / 60) + ' min';
  if (seconds >= 60) return Math.floor(seconds / 60) + ' min ' + seconds % 60 + ' s';
  return seconds + ' s';
}

function transferState(item) {
  const state = item.state || '';
  if (['error', 'missingFiles', 'unknown'].includes(state)) return ['Erro no qBittorrent', 'error'];
  if (/checking|allocating|moving/i.test(state)) return ['Verificando arquivos', 'wait'];
  if (item.progress >= 1) return [/(upload|seed|UP$)/i.test(state) && !/^(stopped|paused)/.test(state) ? 'Concluído · compartilhando' : 'Concluído', 'done'];
  if (/^(paused|stopped)/.test(state)) return ['Pausado', 'paused'];
  if (/meta/i.test(state)) return ['Buscando dados do torrent', 'wait'];
  if (/queued/i.test(state)) return ['Na fila', 'wait'];
  if (/stalled/i.test(state)) return ['Aguardando pares', 'wait'];
  return ['Baixando', 'active'];
}

function setText(element, value) {
  if (element.textContent !== value) element.textContent = value;
}

function createTransferCard(hash) {
 const card=node('article','transfer-card');card.dataset.hash=hash;card.setAttribute('aria-labelledby','transfer-title-'+hash);
 const artwork=node('div','transfer-art');const fallback=node('span','transfer-art-fallback','↓');fallback.setAttribute('aria-hidden','true');artwork.append(fallback);let coverKey='';
 const body=node('div','transfer-body'),title=node('h2');title.id='transfer-title-'+hash;
 const badge=node('span','transfer-badge'),header=node('div','transfer-card-header');header.append(title,badge);
 const percent=node('strong','transfer-percent'),bytes=node('span'),metrics=node('div','transfer-metrics');metrics.append(percent,bytes);
 const track=node('div','transfer-track');track.setAttribute('role','progressbar');track.setAttribute('aria-label','Progresso do download');track.setAttribute('aria-valuemin','0');track.setAttribute('aria-valuemax','100');const fill=node('span');track.append(fill);
 const speedValue=node('strong'),etaValue=node('strong'),peersValue=node('strong'),vitals=node('div','transfer-vitals');for(const [label,value] of [['Velocidade',speedValue],['Tempo restante',etaValue],['Pares conectados',peersValue]]){const item=node('div');item.append(node('small','',label),value);vitals.append(item);}
 const foot=node('div','transfer-card-foot'),rates=node('p','transfer-note'),actions=node('div','transfer-actions');let mode;
 const control=button('','primary',event=>actOnTransfer(control.dataset.action,hash,event.currentTarget));
 const diagnose=button('Diagnóstico','secondary',()=>diagnoseTransfer(hash));diagnose.dataset.icon='info';
 const extra=node('details','menu transfer-menu'),toggle=node('summary','','Mais ações');toggle.setAttribute('aria-label','Mais ações para este download');extra.append(toggle);const menu=node('div');extra.append(menu);
 const reannounce=button('Buscar pares novamente','secondary',event=>actOnTransfer('torrent-reannounce',hash,event.currentTarget));
 menu.append(button('Fila e episódios','secondary',()=>{extra.open=false;organizeTorrent(hash);}),reannounce,button('Remover da fila','secondary',()=>{extra.open=false;confirmRemoval('Remover transferência?',title.textContent+' sai da fila do qBittorrent. Os arquivos ficam no PC.',async()=>{await transferAction('torrent-remove',hash);refreshTransfers(true);});}));
 actions.append(control,diagnose,extra);foot.append(actions);
 const technical=node('details','transfer-technical'),technicalLabel=node('summary','','Arquivo e informações'),fileName=node('small','transfer-filename');technical.append(technicalLabel,fileName);
 body.append(header,metrics,track,vitals,rates,foot,technical);card.append(artwork,body);
 return {card,update(item){
  const [label,color]=transferState(item),progress=Math.max(0,Math.min(100,(item.progress||0)*100)),value=Math.floor(progress*10)/10;
  card.dataset.name=item.movie?.title||item.name||hash;card.dataset.progress=progress;card.dataset.speed=item.speed||0;card.dataset.priority=item.priority||0;
  setText(title,card.dataset.name);setText(fileName,item.name||card.dataset.name);setText(technicalLabel,item.priority>0?'Arquivo · posição '+item.priority+' na fila':'Arquivo e informações');
  const nextCover=item.movie?.hasCover?item.movie.id+'|'+item.movie.coverVersion:'';if(nextCover!==coverKey){coverKey=nextCover;const old=artwork.querySelector('img');if(old)imageObserver.unobserve(old);artwork.replaceChildren();if(nextCover){const image=poster(item.movie);image.alt='Capa de '+item.movie.title;artwork.append(image);imageObserver.observe(image);}else artwork.append(fallback);}
  setText(speedValue,item.speed>0?storageSize(item.speed)+'/s':color==='done'?'Finalizado':'—');const estimated=item.speed>0&&Number.isFinite(item.eta)&&item.eta>0&&item.eta<8640000;setText(etaValue,color==='done'?'Pronto para assistir':estimated?timeLeft(item.eta):'Aguardando estimativa');
  const peers=Number.isInteger(item.seeds)&&item.seeds>=0&&Number.isInteger(item.leechers)&&item.leechers>=0?item.seeds+item.leechers:null;setText(peersValue,peers==null?'—':peers.toLocaleString('pt-BR'));
  setText(badge,label);badge.dataset.status=color;setText(percent,value.toLocaleString('pt-BR',{maximumFractionDigits:1})+'%');setText(bytes,storageSize(item.completed)+(item.size>0?' de '+storageSize(item.size):''));fill.style.width=value+'%';track.setAttribute('aria-valuenow',value);
  const waiting=/meta|stalledDL/i.test(item.state||'')&&color==='wait';reannounce.hidden=!waiting;
  setText(rates,(item.scheduled?'Programado para '+new Date(item.scheduled).toLocaleString('pt-BR'):'')||item.fallback||(waiting?(peers===0?'A origem ainda não tem pares conectados. Consulte o diagnóstico para entender o motivo.':'Aguardando os pares enviarem os dados do torrent.'):color==='done'?'O vídeo está disponível no PC. Os arquivos são preservados ao remover da fila.':''));rates.hidden=!rates.textContent;
  const nextMode=color==='done'?'torrent-files':color==='paused'?'torrent-resume':'torrent-pause';card.dataset.status=color;
  if(mode!==nextMode){mode=nextMode;control.dataset.action=mode;setText(control,mode==='torrent-files'?'Assistir agora':mode==='torrent-resume'?'Retomar download':'Pausar download');}
  control.hidden=color==='error'||/checking|moving|allocating/i.test(item.state||'');
 }};
}

function renderTransfers(data) {
  const list = data.transfers || [];
  $('#torrent-connection').hidden = data.configured;
  setText($('#torrent-connection'), data.configured ? '' : 'Ative a Interface Web no qBittorrent e clique em Conectar qBittorrent.');
  setText($('#torrent-configure'), data.configured ? 'Configurar conexão' : 'Conectar qBittorrent');
  $('#torrent-form').hidden = !data.configured;
  $('#torrent-empty').hidden = !data.configured || list.length > 0;
  $('#torrent-summary').hidden = !data.configured || !list.length;
  const speed = list.reduce((total, item) => total + Math.max(0, item.speed || 0), 0);
  const counters=[[list.filter(item=>transferState(item)[1]==='active').length,'baixando agora'],[storageSize(speed)+'/s','velocidade total'],[storageSize(list.reduce((sum,item)=>sum+Math.max(0,(item.size||0)-(item.completed||0)),0)),'restantes nos downloads'],[list.filter(item=>item.progress>=1).length,'prontos para assistir']];
  for (const [index, [value, label]] of counters.entries()) {
    const summary = $('#torrent-summary');
    if (!summary.children[index]) {
      const metric = node('div', 'transfer-summary-card');
      metric.append(node('strong'), node('span'));
      summary.append(metric);
    }
    setText(summary.children[index].firstChild, String(value));
    setText(summary.children[index].lastChild, label);
  }
  const rows = $('#torrent-rows');
  const focused=rows.contains(document.activeElement)?document.activeElement:null;
  const current = new Set();
  for (const item of list) {
    if (current.has(item.hash)) continue;
    const index = current.size;
    current.add(item.hash);
    if (!transferCards.has(item.hash)) transferCards.set(item.hash, createTransferCard(item.hash));
    const entry = transferCards.get(item.hash);
    entry.update(item);
    if (rows.children[index] !== entry.card) rows.insertBefore(entry.card, rows.children[index] || null);
  }
  for (const [hash, entry] of transferCards) {
    if (current.has(hash)) continue;
    entry.card.remove();
    transferCards.delete(hash);
  }
  applyTransferFilter();if(focused?.isConnected&&document.activeElement===document.body)focused.focus({preventScroll:true});
  rows.classList.remove('stale-transfers');
}

function applyTransferFilter() {
  let visible = 0;const search=($('#transfer-search')?.value||'').trim().toLocaleLowerCase();
  for (const { card } of transferCards.values()) {
    card.hidden=(transferFilter!=='all'&&card.dataset.status!==transferFilter)||!!search&&!card.dataset.name.toLocaleLowerCase().includes(search);
    if (!card.hidden) visible++;
  }
  for (const filter of document.querySelectorAll('[data-transfer-filter]'))
    {filter.setAttribute('aria-pressed',filter.dataset.transferFilter===transferFilter);if(!filter.dataset.label)filter.dataset.label=filter.textContent;const count=[...transferCards.values()].filter(({card})=>filter.dataset.transferFilter==='all'||card.dataset.status===filter.dataset.transferFilter).length;filter.replaceChildren(node('span','',filter.dataset.label),node('small','',String(count)));}
  const order=$('#transfer-sort')?.value||'queue',rows=$('#torrent-rows'),focused=rows.contains(document.activeElement)?document.activeElement:null;const ordered=[...transferCards.values()].sort((a,b)=>order==='name'?a.card.dataset.name.localeCompare(b.card.dataset.name,'pt-BR'):order==='progress'?Number(b.card.dataset.progress)-Number(a.card.dataset.progress):order==='speed'?Number(b.card.dataset.speed)-Number(a.card.dataset.speed):Number(a.card.dataset.priority||999999)-Number(b.card.dataset.priority||999999));for(const [index,item] of ordered.entries())if(rows.children[index]!==item.card)rows.insertBefore(item.card,rows.children[index]||null);if(focused?.isConnected&&document.activeElement===document.body)focused.focus({preventScroll:true});
  if (transferCards.size) {
    $('#torrent-empty').hidden = visible > 0;
    $('#torrent-empty').textContent = 'Nenhum download neste filtro.';
  }
}

async function diagnoseTransfer(hash) {
  const dialog = $('#diagnostic-dialog');
  if (!dialog.open) dialog.showModal();
  const result = $('#diagnostic-result');
  result.replaceChildren(node('p', '', 'Consultando o qBittorrent…'));
  try {
    const data = await transferAction('torrent-diagnose', hash);
    const label = transferState({ state: data.state, progress: data.progress })[0];
    const explanation = /meta/i.test(data.state) ? 'Aguardando receber os metadados do torrent de um par acessível.' :
      /^(paused|stopped)/i.test(data.state) ? 'A transferência está pausada. Use Retomar para continuar.' :
      /queued/i.test(data.state) ? 'O qBittorrent está aguardando uma vaga na fila.' :
      /stalledDL/i.test(data.state) ? 'O download está ativo, mas ainda não conseguiu transferir dados dos pares.' :
      /error|missingFiles/i.test(data.state) ? 'Confira a pasta de destino, o espaço livre e as permissões no qBittorrent.' :
      data.progress >= 1 ? 'O download foi concluído.' : 'A transferência está em andamento.';
    result.replaceChildren(node('h3', '', label), node('p', '', explanation),
      node('p', '', 'Semeadores conectados: ' + Math.max(0, data.seeds) + ' · Outros pares: ' + Math.max(0, data.leechers)),
      node('p', '', 'Rede: ' + ({ connected: 'conectada', firewalled: 'conexões de entrada limitadas', disconnected: 'desconectada' }[data.connection] || data.connection) + ' · Nós DHT: ' + data.dht));
    if (!data.seeds && !data.leechers) result.append(node('p', '', 'Nenhum par conectado agora. Você pode buscar pares novamente ou consultar outra fonte do filme.'));
    result.append(node('h3', '', 'Trackers'));
    for (const tracker of data.trackers) {
      const section = node('section', 'settings-card');
      section.append(node('strong', '', tracker.name), node('p', '',
        (['Desativado', 'Ainda não consultado', 'Respondendo', 'Atualizando', 'Falhou'][tracker.status] || 'Estado desconhecido') +
        (tracker.seeds >= 0 ? ' · ' + tracker.seeds + ' semeadores informados' : '')));
      if (tracker.message) section.append(node('p', '', tracker.message));
      result.append(section);
    }
  } catch (error) { result.replaceChildren(node('p', '', error.message)); }
}
$('#diagnostic-close').onclick = () => $('#diagnostic-dialog').close();
for (const filter of document.querySelectorAll('[data-transfer-filter]')) filter.onclick = () => {
  transferFilter = filter.dataset.transferFilter;
  applyTransferFilter();
};

async function refreshTransfers(force = false) {
  if (state.section !== 'downloads' || (document.hidden && !force)) return;
  if (transferRequest) { if (force) transferRefreshQueued = true; return; }
  transferRequest = transferAction('torrent-list');
  try {
    const data = await transferRequest;
    if (state.section === 'downloads') renderTransfers(data);
  } catch (error) {
    $('#torrent-connection').hidden = false;
    setText($('#torrent-connection'), error.message);
    $('#torrent-rows').classList.add('stale-transfers');
    $('#torrent-summary').hidden = $('#torrent-empty').hidden = true;
  } finally {
    transferRequest = null;
    if (transferRefreshQueued) { transferRefreshQueued = false; refreshTransfers(true); }
  }
}

async function actOnTransfer(name, hash, trigger, address) {
  trigger.disabled = true;
  setText($('#torrent-message'), '');
  try {
    const result = await transferAction(name, hash, address, name === 'torrent-add' ? magnetFolder.value : undefined);
    if (name === 'torrent-files') {
      if (!result.length) throw new Error('Nenhum vídeo compatível foi encontrado na pasta do torrent.');
      if (result.length === 1) {
        await transferAction('torrent-play', hash, String(result[0].index));
        return true;
      }
      const picker = $('#torrent-file-list');
      picker.replaceChildren(...result.map(file => button(file.name + ' · ' + storageSize(file.size), 'secondary',
        event => actOnTransfer('torrent-play', hash, event.currentTarget, String(file.index)))));
      $('#torrent-files').showModal();
    } else if (name === 'torrent-reannounce') {
      setText($('#torrent-message'), 'O qBittorrent solicitou uma nova busca aos trackers.');
    } else if (name === 'torrent-add') {
      setText($('#torrent-message'), 'Link enviado. Se escolheu consultar arquivos, abra Organizar / episódios após receber os metadados.');
    }
    refreshTransfers(true);
    return true;
  } catch (error) {
    if ($('#torrent-files').open) $('#torrent-files').close();
    setText($('#torrent-message'), error.message);
    return false;
  } finally { trigger.disabled = false; }
}

$('#torrent-open').onclick = event => actOnTransfer('torrent-open', null, event.currentTarget);
$('#torrent-configure').onclick = () => openSettings('connection');
$('#close-torrent-files').onclick = () => $('#torrent-files').close();
const magnetFolder = folderPicker();
$('#torrent-form').append(magnetFolder.element);
$('#torrent-form').onsubmit = async event => {
  event.preventDefault();
  const input = $('#torrent-magnet');
  const submit = event.submitter || $('#torrent-form button[type="submit"]');
  if($('#torrent-choose-before')?.checked){try{await smartCall('smart-metadata',null,{url:input.value.trim(),folder:magnetFolder.value||''});input.value='';setText($('#torrent-message'),'Consultando metadados. Abra Organizar / episódios para selecionar os arquivos.');refreshTransfers(true);}catch(error){setText($('#torrent-message'),error.message);}}
  else if (await actOnTransfer('torrent-add', null, submit, input.value.trim())) input.value = '';
};
$('#transfer-search').oninput=applyTransferFilter;$('#transfer-sort').onchange=applyTransferFilter;
setInterval(() => refreshTransfers(), 1500);

function sourceOptionInfo(option){
  let name='',bytes=0;try{if(option.url?.startsWith('magnet:')){const url=new URL(option.url);name=url.searchParams.get('dn')||'';bytes=Number(url.searchParams.get('xl'))||0;}}catch{}
  const label=option.label||'Download da origem',text=label+' '+name;
  const resolution=text.match(/\b(4320|2160|1440|1080|720|576|480)p\b/i)?.[0].toLowerCase()||(/\b4k\b/i.test(text)?'2160p':'');
  const size=label.match(/\b\d+(?:[.,]\d+)?\s*(?:TiB|GiB|MiB|KiB|TB|GB|MB|KB)\b/i)?.[0]||(Number.isSafeInteger(bytes)&&bytes>0?storageSize(bytes):'');
  const audio=/\bdual\b|dublado\s+e\s+legendado/i.test(text)?'Dual áudio':/\bdublado\b/i.test(text)?'Dublado':/\blegendado\b/i.test(text)?'Legendado':'';
  const format=/\bWEB[ .-]?DL\b/i.test(text)?'WEB-DL':/\bBlu[ .-]?Ray\b/i.test(text)?'Blu-ray':'';
  return {label,resolution,size,audio,format};
}
function renderSourceOption(control,option){
  const info=sourceOptionInfo(option),quality=node('span','source-quality',info.resolution||'Vídeo');quality.dataset.quality=info.resolution;
  const copy=node('span','source-copy'),meta=node('span','source-specs');
  copy.append(node('strong','',info.resolution==='2160p'?'4K Ultra HD':info.resolution==='1080p'?'Full HD':info.resolution==='720p'?'HD':info.resolution||'Qualidade não informada'));
  for(const text of [info.resolution,info.size,info.audio,info.format].filter(Boolean))meta.append(node('span','',text));if(meta.childElementCount)copy.append(meta);
  copy.append(node('small','source-original',info.label));
  const action=node('span','source-option-action'),kind=option.kind==='torrent'?'qBittorrent':option.kind==='file'?'Arquivo direto':option.kind==='resolve'?'Verificar origem':'Link externo';
  action.append(node('span','source-cta',option.kind==='resolve'?'Consultar ↗':option.kind==='link'?'Abrir ↗':'Baixar ↓'),node('small','source-kind',kind));
  control.setAttribute('aria-label',[info.resolution||'Qualidade não informada',info.size,info.audio,info.label,kind].filter(Boolean).join(' · '));control.append(quality,copy,action);
}
async function showSourceDownloads(movie, trigger) {
  const existing=document.querySelector('.source-dialog[open] .source-downloads');
  if (existing) {
    if(existing.dataset.movie===movie.id){existing.closest('dialog').focus();return;}existing.closest('dialog').close();
  }
  const view=smartDialog('Opções de download');view.dialog.classList.add('source-dialog');view.dialog.setAttribute('aria-label','Opções de download de '+movie.title);
  const choices=node('div','source-downloads');choices.dataset.movie=movie.id;view.body.append(choices);
  const selected=node('div','source-selected'),cover=poster(movie);if(cover){selected.append(cover);imageObserver.observe(cover);}const selectedCopy=node('div');const availability=node('p','source-availability','Consultando fontes disponíveis…');selectedCopy.append(node('p','eyebrow','ESCOLHA SUA VERSÃO'),node('strong','',movie.title),availability);selected.append(selectedCopy);view.body.prepend(selected);
  const message = node('p', 'source-message');
  choices.append(message);choices.setAttribute('aria-busy','true');
  message.setAttribute('role', 'status');
  const sourceFolder = folderPicker();
  const preferences=node('div','source-preferences');preferences.append(node('p','source-preference-label','DESTINO DO TORRENT'),sourceFolder.element);choices.append(preferences);
  const selectEpisodes=smartField(preferences,'Escolher episódios / arquivos antes de baixar (qBittorrent 5+)',null,'checkbox');
  selectEpisodes.parentElement.classList.add('source-file-choice');
  const sourceList=node('div','source-list');choices.append(sourceList);
  trigger.disabled = true;
  try {
    const sources = await transferAction('movie-sources', movie.id);
    let found = 0,offering=0;
    for (const source of sources) {
    if(!choices.isConnected)break;
    const group = node('section', 'source-group');
    const heading=node('div','source-group-heading'),count=node('span','source-count','Consultando…');heading.append(node('h4', '', source.site || 'Origem'),count);group.append(heading);
    sourceList.append(group);const loading=node('div','source-loading');loading.setAttribute('aria-hidden','true');loading.append(node('span'),node('span'));group.append(loading);
    let options;
    try { options = await transferAction('download-options', source.id); }
    catch (error) { count.textContent='Indisponível';group.append(node('p', '', error.message)); continue; }
    finally{loading.remove();}
    if(!choices.isConnected)break;
    found += options.length;
    if(options.length)offering++;count.textContent=options.length+' '+(options.length===1?'opção':'opções');availability.textContent=found+' '+(found===1?'opção':'opções')+' em '+offering+' '+(offering===1?'fonte':'fontes');
    if (!options.length) group.append(node('p', '', 'Nenhuma opção disponível nesta origem.'));
    for (const option of options) {
      const choose=button('', 'secondary source-option', async event => {
        const selected = event.currentTarget;
        selected.disabled = true;
        let torrentSelected = option.kind === 'torrent';
        setText(message, '');
        choices.querySelector('.source-connection')?.remove();
        try {
          if (option.kind === 'resolve') setText(message, 'Consultando a liberação do download na origem…');
          const resolved = option.kind === 'resolve' ? await transferAction('download-resolve', source.id, option.url) : option;
          torrentSelected = resolved.kind === 'torrent';
          if (resolved.kind !== 'torrent') {
            await transferAction('download-source', source.id, option.url);
            return;
          }
          setText(message, 'Enviando ao qBittorrent…');
          if(selectEpisodes.checked) await smartCall('smart-metadata', source.id, {url:resolved.url,folder:sourceFolder.value||''});
          else await transferAction('torrent-add', source.id, resolved.url, sourceFolder.value);
          if (choices.isConnected) {
            view.dialog.close();if($('#details').open)$('#details').close();
            changeSection('downloads');
            setText($('#torrent-message'), 'Link enviado. Se escolheu consultar arquivos, abra Organizar / episódios após receber os metadados.');
          }
        } catch (error) {
          setText(message, error.message);
          if (torrentSelected) {
            const actions = node('div', 'actions source-connection');
            const open = button('Abrir qBittorrent', 'secondary', async () => {
              open.disabled = true;
              try {
                await transferAction('torrent-open');
                setText(message, 'Aguarde o qBittorrent abrir e selecione a opção de download novamente.');
              } catch (launchError) { setText(message, launchError.message); }
              finally { open.disabled = false; }
            });
            actions.append(open, button('Configurar conexão', 'secondary', async () => {
              try { await openSettings('connection'); }
              catch (setupError) { setText(message, setupError.message); }
            }));
            choices.append(actions);
          }
        } finally { selected.disabled = false; }
      });renderSourceOption(choose,option);group.append(choose);
    }
    }
    setText(message, found ? '' : 'Nenhuma fonte retornou opções. Confira as mensagens de cada site.');
    if(!found)availability.textContent='Sem opções disponíveis nesta consulta';
    if (!found) choices.append(button('Consultar fontes novamente', 'secondary', () => {
      view.dialog.close();
      showSourceDownloads(movie, trigger);
    }));
  } catch (error) {
    availability.textContent='Não foi possível consultar as fontes';
    setText(message, error.message || 'A consulta falhou. Tente novamente.');
    choices.append(button('Tentar novamente', 'secondary', () => {
      view.dialog.close();
      return showSourceDownloads(movie, trigger);
    }));
  } finally { trigger.disabled = false;choices.setAttribute('aria-busy','false'); }
}
