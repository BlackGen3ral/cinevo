let smartFilters = {};
const smartCall = (name, id, data = {}) => transferAction(name, id, JSON.stringify(data));
function smartDialog(title) {
  const dialog = node('dialog', 'smart-dialog');
  const header = node('div', 'settings-heading'); header.append(node('h2','',title), button('Fechar','secondary',()=>dialog.close()));
  const body = node('div', 'smart-body'); const status = node('p','meta'); status.setAttribute('role','status');
  dialog.append(header,body,status); document.body.append(dialog); dialog.addEventListener('close',()=>dialog.remove(),{once:true}); dialog.showModal();
  return {dialog,body,status};
}
function smartField(form,label,value='',type='text') {
  const wrap=node('label','smart-field',label); const input=node('input'); input.type=type; input.value=value ?? ''; if(type==='number'){input.min=0; input.step='any';} wrap.append(input);form.append(wrap);return input;
}
function smartSubmit(form,label,callback,status) {
  const submit=button(label,'primary',()=>{});submit.type='submit';form.append(submit);
  form.onsubmit=async e=>{e.preventDefault();submit.disabled=true;status.textContent='Salvando…';try{await callback();}catch(error){status.textContent=error.message;}finally{submit.disabled=false;}};
}
function openSmartFilters() {
  const view=smartDialog('Biblioteca inteligente');const form=node('form','smart-grid');view.body.append(form);
  const genre=smartField(form,'Gênero',smartFilters.genre);
  const min=smartField(form,'Duração mínima (min)',smartFilters.minMinutes,'number');
  const max=smartField(form,'Duração máxima (min)',smartFilters.maxMinutes,'number');
  const score=smartField(form,'Nota mínima TMDB / informada (0–10)',smartFilters.score,'number'); score.max=10;
  const resolution=smartField(form,'Resolução (ex.: 1080p)',smartFilters.resolution);
  const language=smartField(form,'Idioma',smartFilters.language);
  const collection=smartField(form,'Coleção',smartFilters.collection);
  const duplicates=smartField(form,'Somente possíveis duplicados',null,'checkbox');duplicates.checked=!!smartFilters.duplicates;
  view.body.append(node('p','meta','Os filtros usam dados disponíveis ou corrigidos na ficha. Possíveis duplicados têm o mesmo título, ano e episódio; nenhum arquivo é excluído.'));
  smartSubmit(form,'Aplicar filtros',async()=>{smartFilters={genre:genre.value,minMinutes:+min.value,maxMinutes:+max.value,score:+score.value,resolution:resolution.value.trim().toLowerCase(),language:language.value,collection:collection.value,duplicates:duplicates.checked};await refresh(false);view.dialog.close();},view.status);
  form.append(button('Limpar filtros','secondary',async()=>{smartFilters={};await refresh(false);view.dialog.close();}));
  smartCall('smart-options').then(data=>{const list=node('datalist');list.id='smart-collections';for(const name of data.collections){const option=node('option');option.value=name;list.append(option);}form.append(list);collection.setAttribute('list',list.id);}).catch(error=>view.status.textContent=error.message);
}
function openIdentification(movie) {
  const view=smartDialog('Identificação e coleções'); const form=node('form','smart-grid');view.body.append(form);
  const fields={};
  for(const [key,label,value,type] of [
    ['title','Título',movie.title],['year','Ano',movie.year],['tmdb','Ficha ou temporada correta no TMDB',movie.tmdbLink,'url'],
    ['genre','Gêneros',movie.genre],['minutes','Duração (min)',Math.round((movie.durationMilliseconds||0)/60000),'number'],['score','Nota (0–10)',movie.userScore||0,'number'],
    ['resolution','Resolução (ex.: 1080p)',movie.resolution],['language','Idioma(s)',movie.language],['collections','Coleções (separadas por vírgula)',(movie.collections||[]).join(', ')],
    ['series','Nome da série (vazio para filme)',movie.seriesTitle],['season','Temporada',movie.season||0,'number'],['episode','Episódio',movie.episode||0,'number']]) fields[key]=smartField(form,label,value,type);
  fields.title.required=true;fields.score.max=10;
  view.body.append(node('p','meta','Ao informar uma ficha TMDB, título, capa, sinopse, gêneros, nota e duração vêm dela. Série, temporada, episódio, idioma e coleções podem ser corrigidos abaixo. Os vídeos permanecem no lugar.'));
  smartSubmit(form,'Salvar identificação',async()=>{const data=Object.fromEntries(Object.entries(fields).map(([key,input])=>[key,input.type==='number'?+input.value:input.value]));const updated=await smartCall('smart-identify',movie.id,data);await refresh(false,true);view.dialog.close();showDetails(updated);},view.status);
}
async function openStorage() {
  const view=smartDialog('Armazenamento');
  const load=async(kind)=>{view.status.textContent='Calculando…';try{const data=await smartCall(kind?'smart-clean':'smart-storage',null,kind?{kind}:{});view.body.replaceChildren();
    for(const [name,value] of [['Vídeos da biblioteca',data.videos],['Capas em cache',data.covers],['Pacotes temporários de atualização',data.temporary]]){const row=node('div','settings-card');row.append(node('h3','',name),node('strong','',storageSize(value)));view.body.append(row);}
    view.body.append(node('p','meta','Vídeos, legendas e capas em uso ficam protegidos. A limpeza remove apenas capas sem referência e pacotes de atualização com mais de 2 dias. Downloads em andamento ficam preservados.'));
    for(const [type,label,size] of [['covers','Limpar capas sem uso',data.cleanCovers],['updates','Limpar pacotes antigos',data.cleanTemporary]]){const control=button(label+' · '+storageSize(size),'secondary',()=>confirmRemoval('Limpar cache?',label+'. Os vídeos serão preservados.',()=>load(type)));control.disabled=size===0;view.body.append(control);}
    view.status.textContent=kind?'Liberado: '+storageSize(data.removed)+(data.skipped?' · Alguns arquivos estavam em uso.':''):'';
  }catch(error){view.status.textContent=error.message;}};await load();
}
function openDownloadLimits() {
  const view=smartDialog('Fila e velocidade');const form=node('form','smart-grid');view.body.append(form);
  const active=smartField(form,'Downloads simultâneos',2,'number');active.min=1;active.max=100;
  const limit=smartField(form,'Limite global de download (KiB/s; 0 = ilimitado)',0,'number');
  view.body.append(node('p','meta','Esses controles alteram a fila e o limite global do qBittorrent. O agendamento de início está nas opções de cada transferência.'));
  smartCall('smart-limits-read').then(data=>{active.value=Math.max(1,data.active);limit.value=Math.max(0,data.limit);}).catch(error=>view.status.textContent=error.message);
  smartSubmit(form,'Aplicar ao qBittorrent',async()=>{await smartCall('smart-limits',null,{active:+active.value,limit:Math.round(+limit.value)});view.status.textContent='Fila e limite aplicados.';},view.status);
}
async function organizeTorrent(hash) {
  const view=smartDialog('Organizar download');const actions=node('div','actions');view.body.append(actions);
  for(const [label,direction] of [['Prioridade alta','topPrio'],['Fim da fila','bottomPrio']])actions.append(button(label,'secondary',async()=>{try{await smartCall('smart-torrent-priority',hash,{direction});view.status.textContent='Prioridade alterada.';}catch(error){view.status.textContent=error.message;}}));
  const schedule=node('form','smart-grid');view.body.append(schedule);const at=smartField(schedule,'Iniciar em (Cinevo e qBittorrent precisam estar abertos)','','datetime-local');at.required=true;
  smartSubmit(schedule,'Pausar e agendar início',async()=>{await smartCall('smart-torrent-schedule',hash,{at:new Date(at.value).toISOString()});view.status.textContent='Início agendado. O download está pausado.';refreshTransfers(true);},view.status);
  try{const files=await smartCall('smart-torrent-files',hash);if(!files.length){view.body.append(node('p','meta','Aguardando os metadados. Reabra esta tela quando a lista de arquivos estiver disponível.'));return;}
    const form=node('form','smart-files');view.body.append(node('h3','','Arquivos e episódios'),node('p','meta','Arquivos desmarcados não serão baixados. Se já existirem no disco, serão preservados.'),form);const selected=[];
    for(const file of [...files].sort((a,b)=>a.name.localeCompare(b.name,undefined,{numeric:true}))){const input=smartField(form,file.name+' · '+storageSize(file.size),null,'checkbox');input.checked=file.priority>0;selected.push([input,file.index]);}
    smartSubmit(form,'Baixar somente os selecionados',async()=>{await smartCall('smart-torrent-select',hash,{ids:selected.filter(([input])=>input.checked).map(([,id])=>id)});view.status.textContent='Seleção aplicada. Download iniciado.';refreshTransfers(true);},view.status);
  }catch(error){view.status.textContent=error.message;}
}
const smartTools=node('div','actions smart-toolbar');smartTools.append(button('Filtros e duplicados','secondary',openSmartFilters),button('Armazenamento','secondary',openStorage));$('.tools').prepend(smartTools);
$('#torrent-form').before(button('Fila e velocidade','secondary',openDownloadLimits));

smartField($('#torrent-form'),'Escolher arquivos antes de baixar',null,'checkbox').id='torrent-choose-before';
