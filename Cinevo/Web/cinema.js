const coverFlights=new WeakMap();let coverTransition=null;
function motionAllowed(){return !matchMedia('(prefers-reduced-motion: reduce)').matches;}
async function openWithCover(movie,source){
  if(!source?.complete||!source.naturalWidth||!document.startViewTransition||!motionAllowed()){showDetails(movie);return;}
  if(coverTransition)coverTransition.skipTransition();
  source.style.viewTransitionName='cinevo-cover';let target;
  const transition=document.startViewTransition(async()=>{
    source.style.viewTransitionName='';await showDetails(movie);
    const dialog=document.querySelector('.series-hub[open]')||$('#details');
    target=dialog.querySelector('.detail-poster,.series-intro>img');
    if(target){target.style.viewTransitionName='cinevo-cover';await loadImage(target);try{await target.decode();}catch{}coverFlights.set(dialog,{source,target});installCoverReturn(dialog);}
  });coverTransition=transition;
  try{await transition.finished;}catch{}finally{source.style.viewTransitionName='';if(target)target.style.viewTransitionName='';if(coverTransition===transition)coverTransition=null;}
}
function installCoverReturn(dialog){if(dialog.dataset.coverReturn)return;dialog.dataset.coverReturn='true';
  async function close(){const flight=coverFlights.get(dialog);if(!flight?.source.isConnected||!flight.target.isConnected||!motionAllowed()||!document.startViewTransition){dialog.close();return;}if(coverTransition)coverTransition.skipTransition();flight.target.style.viewTransitionName='cinevo-cover';const transition=document.startViewTransition(()=>{flight.target.style.viewTransitionName='';dialog.close();flight.source.style.viewTransitionName='cinevo-cover';});coverTransition=transition;try{await transition.finished;}catch{}finally{flight.source.style.viewTransitionName='';flight.target.style.viewTransitionName='';if(coverTransition===transition)coverTransition=null;}}
  dialog.addEventListener('click',event=>{const button=event.target.closest('button');if(button&&(button.classList.contains('dialog-close')||button.textContent.trim()==='Fechar')){event.preventDefault();event.stopImmediatePropagation();close();}},true);
  dialog.addEventListener('cancel',event=>{event.preventDefault();close();});
}
async function addCinematicArt(movie,header,panoramic){
  const revision=(header.cinemaRevision||0)+1;header.cinemaRevision=revision;
  try{const response=await request((panoramic?'/api/backdrop/':'/api/cover/')+movie.id);const blob=await response.blob();if(!header.isConnected||header.cinemaRevision!==revision)return;const art=node('img','detail-backdrop');art.alt='';art.setAttribute('aria-hidden','true');art.classList.toggle('poster-fallback',!panoramic);const url=URL.createObjectURL(blob);art.onload=art.onerror=()=>URL.revokeObjectURL(url);art.src=url;header.closest('dialog')?.addEventListener('close',()=>URL.revokeObjectURL(url),{once:true});header.querySelector('.detail-backdrop')?.remove();header.prepend(art);}catch{}
}
const collapseButton=button('Recolher menu','secondary',()=>setSidebar(!document.documentElement.classList.contains('sidebar-collapsed')));collapseButton.id='sidebar-toggle';collapseButton.setAttribute('aria-controls','cinevo-sidebar');$('.sidebar').id='cinevo-sidebar';$('.sidebar').insertBefore(collapseButton,$('.sidebar').firstChild);
function setSidebar(value,persist=true){document.documentElement.classList.toggle('sidebar-collapsed',value);if(!collapseButton.dataset.sidebarLabel)collapseButton.textContent=value?'☰':'Recolher menu';collapseButton.setAttribute('aria-expanded',!value);collapseButton.setAttribute('aria-label',value?'Expandir menu lateral':'Recolher menu lateral');collapseButton.title=value?'Expandir menu lateral':'Recolher menu lateral';localStorage.setItem('cinevo-sidebar',value?'collapsed':'expanded');if(persist)transferAction('settings-sidebar',null,JSON.stringify({collapsed:value})).catch(error=>$('#status').textContent=error.message);}
for(const button of document.querySelectorAll('.sidebar nav button')){button.title=button.textContent.trim();button.setAttribute('aria-label',button.title);}
for(const [selector,label] of [['#viewer-button','Perfis'],['#open-news','Novidades'],['.sidebar-support>button[data-command="settings"]','Configurações']]){const element=$(selector);if(element)element.title=label;}
setSidebar(localStorage.getItem('cinevo-sidebar')==='collapsed',false);transferAction('settings-read').then(settings=>setSidebar(!!settings.sidebarCollapsed,false)).catch(()=>{});

for(const button of document.querySelectorAll('.sidebar-support>button')){button.setAttribute('aria-label',button.textContent.trim());button.title=button.textContent.trim();button.dataset.sidebarKind=button.textContent.includes('Conta')?'account':button.id==='open-news'?'news':button.dataset.command==='settings'?'settings':'media';}
