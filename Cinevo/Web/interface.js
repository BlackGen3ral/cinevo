// Preferences are stored by the local app, independently for each viewer.
let visualStyle = {covers:'medium',glow:65,clickVolume:30,density:'comfortable'};
function applyVisualStyle(value) {
  visualStyle=value;
  const root=document.documentElement;
  root.dataset.covers=value.covers;root.dataset.density=value.density;
  root.style.setProperty('--focus-glow',String(value.glow/100));
  root.dataset.glow=value.glow===0?'off':'on';
  window.CinevoControls?.setVolume(value.clickVolume);
}
window.refreshVisualPrefs=async()=>applyVisualStyle(await transferAction('visual-read'));
async function openAppearance(){
  const view=smartDialog('Aparência e sons');view.dialog.id='appearance-dialog';
  const original=await transferAction('visual-read');const form=node('form','smart-grid');view.body.append(form);
  const select=(label,name,options)=>{const wrap=node('label','smart-field',label),input=node('select');input.name=name;for(const [value,text] of options){const option=node('option','',text);option.value=value;input.append(option);}input.value=original[name];wrap.append(input);form.append(wrap);return input;};
  const covers=select('Tamanho das capas','covers',[['small','Pequenas'],['medium','Médias'],['large','Grandes']]);
  const density=select('Densidade da interface','density',[['compact','Compacta'],['comfortable','Confortável'],['spacious','Espaçosa']]);
  const range=(label,name)=>{const wrap=node('label','smart-field'),title=node('span'),input=node('input');input.name=name;input.type='range';input.min=0;input.max=100;input.step=1;input.value=original[name];const update=()=>title.textContent=label+' · '+input.value+'%';input.addEventListener('input',update);update();wrap.append(title,input);form.append(wrap);return input;};
  const glow=range('Intensidade do brilho','glow'),volume=range('Volume dos cliques','clickVolume');
  const read=()=>({covers:covers.value,density:density.value,glow:+glow.value,clickVolume:+volume.value});
  form.addEventListener('input',()=>applyVisualStyle(read()));
  view.body.append(node('p','meta','Prévia imediata. O brilho mantém o contorno de foco visível. As preferências ficam salvas neste PC por perfil e são aplicadas também no modo TV ao conectar.'));
  const sample=button('Ouvir clique','secondary',()=>window.CinevoControls?.previewSound?.());view.body.append(sample);
  let saved=false;view.dialog.addEventListener('close',()=>{if(!saved)applyVisualStyle(original);});
  smartSubmit(form,'Salvar aparência',async()=>{applyVisualStyle(await transferAction('visual-save',null,JSON.stringify(read())));saved=true;view.dialog.close();},view.status);
}
const appearance=button('Aparência e sons','secondary',()=>openAppearance().catch(error=>$('#settings-status').textContent=error.message));appearance.id='appearance-open';
$('#settings-dialog .settings-card').append(appearance);
window.refreshVisualPrefs().catch(()=>{});

function selectionInfo(movie){
  const minutes=Math.round((movie.durationMilliseconds||0)/60000);
  const availability=movie.hasLocalFile?'Disponível no PC':movie.canPlay?'Disponível online':movie.isOnline?'Na origem':'Arquivo indisponível';
  const info=node('span','selection-info');
  for(const text of [minutes?minutes+' min':'',movie.resolution||'',availability].filter(Boolean))info.append(node('span','',text));
  return info;
}
let searchGeneration=0;
const searchResults=node('section','organized-search');searchResults.id='search-results';searchResults.hidden=true;searchResults.setAttribute('aria-label','Resultados por categoria');$('#main').append(searchResults);
window.hideOrganizedSearch=()=>{searchGeneration++;searchResults.hidden=true;$('#collection').hidden=state.section==='downloads';};
window.organizedSearch=async query=>{
  const generation=++searchGeneration;state.busy=true;fetchCatalog?.abort();
  $('#hero').hidden=true;$('#profile-home').hidden=true;$('#collection').hidden=true;searchResults.hidden=false;
  searchResults.replaceChildren(node('p','meta','Buscando na coleção…'));$('#main').setAttribute('aria-busy','true');
  try{
    const data=await transferAction('search-organized',null,query);if(generation!==searchGeneration)return;
    state.movies=[...data.films,...data.series,...data.episodes];
    searchResults.replaceChildren(node('h1','','Resultados para “'+query+'”'),node('p','meta','Em toda a coleção · até 60 títulos por categoria'));
    const category=(title,movies)=>{const section=node('section','search-category'),grid=node('div','grid');section.append(node('h2','',title+' · '+movies.length));if(movies.length)grid.append(...movies.map(movieCard));else grid.append(node('p','meta','Nenhum título encontrado.'));section.append(grid);searchResults.append(section);return section;};
    category('Filmes',data.films);category('Séries',data.series);category('Episódios',data.episodes);
    const people=node('section','search-category');people.append(node('h2','','Pessoas do elenco'));const list=node('div','people-results');people.append(list);searchResults.append(people);
    const showPerson=async(person)=>{const view=smartDialog('Na coleção com '+person.name);try{const movies=await transferAction(person.id?'search-person-library':'search-person-local',person.id?String(person.id):null,person.name);if(!Array.isArray(movies)||!movies.length)view.status.textContent='Nenhum título identificado com esta pessoa na sua coleção.';else{const grid=node('div','grid');grid.append(...movies.map(movie=>{const card=movieCard(movie);card.querySelector('button').onclick=()=>{view.dialog.close();showDetails(movie);};return card;}));view.body.append(grid);}}catch(error){view.status.textContent=error.message;}};
    const addPerson=person=>{const control=button('','secondary',()=>showPerson(person));control.append(node('strong','',person.name),node('small','',person.knownFor||'Identificado na coleção'));list.append(control);};
    for(const name of data.people)addPerson({name});
    const peopleStatus=node('p','meta',data.people.length?'Elenco identificado nas fichas da coleção.':'Nenhuma pessoa identificada nas fichas da coleção.');people.append(peopleStatus);
    const online=button('Pesquisar pessoas no TMDB','secondary',async()=>{online.disabled=true;try{const result=await transferAction('search-people',null,query);if(generation!==searchGeneration)return;if(!result.configured){peopleStatus.textContent='Conecte o TMDB nas configurações para pesquisar pessoas.';return;}list.replaceChildren();for(const person of result.people)addPerson(person);peopleStatus.textContent=result.people.length?'TMDB · selecione para ver títulos identificados na sua coleção.':'Nenhuma pessoa encontrada no TMDB.';}catch(error){peopleStatus.textContent=error.message;}finally{online.disabled=false;}});if(query.length>=2)people.append(online);
    $('#status').textContent='Busca organizada em filmes, séries, episódios e elenco.';
  }catch(error){if(generation===searchGeneration)searchResults.replaceChildren(node('p','meta',error.message));}
  finally{if(generation===searchGeneration){state.busy=false;$('#main').setAttribute('aria-busy','false');}}
};

