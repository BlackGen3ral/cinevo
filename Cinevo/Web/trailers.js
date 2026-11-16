(function(){
 const entries=new Set();let apiPromise,sequence=0;
 function api(){
  if(window.YT?.Player)return Promise.resolve(window.YT);if(apiPromise)return apiPromise;
  apiPromise=new Promise((resolve,reject)=>{
   let complete=false;const script=document.createElement('script'),previous=window.onYouTubeIframeAPIReady;
   function finish(error){if(complete)return;complete=true;clearTimeout(timer);if(error){apiPromise=null;script.remove();reject(error);}else resolve(window.YT);}
   window.onYouTubeIframeAPIReady=()=>{try{previous?.();}finally{finish();}};
   script.src='https://www.youtube.com/iframe_api';script.referrerPolicy='strict-origin-when-cross-origin';
   script.onerror=()=>finish(new Error('O YouTube não respondeu. Confira a conexão e tente novamente.'));
   const timer=setTimeout(()=>finish(new Error('O YouTube demorou para responder. Tente novamente.')),15000);document.head.append(script);
  });return apiPromise;
 }
 function dispose(entry){clearTimeout(entry.timer);try{entry.player?.destroy();}catch{}entry.frame?.remove();entries.delete(entry);}
 function render(section,actions,movie,video,alternatives){
  const ids=[...new Set([video,...(Array.isArray(alternatives)?alternatives:[])].filter(id=>typeof id==='string'&&/^[A-Za-z0-9_-]{11}$/.test(id)))].slice(0,6),signature=JSON.stringify(ids);
  actions.querySelector('[data-trailer-action]')?.remove();
  async function refresh(control){control.disabled=true;try{await window.refreshMovieTrailer(movie,section,actions);}catch(error){if(section.isConnected){let message=section.querySelector('.trailer-state');if(!message){message=node('p','trailer-state');section.append(message);}message.textContent=error.message;}}finally{control.disabled=false;}}
  const play=button(ids.length?'Ver trailer':'Buscar trailer','secondary',()=>{
   if(!ids.length){refresh(play);return;}
   section.scrollIntoView({block:'nearest',behavior:motionAllowed()?'smooth':'instant'});
   const entry=[...entries].find(e=>e.section===section);
   if(entry){if(entry.player&&entry.ready){entry.player.playVideo();entry.frame?.focus({preventScroll:true});}else entry.wantPlay=true;}
  });play.dataset.trailerAction='true';actions.append(play);
  if(section.dataset.trailer===signature&&section.childElementCount)return;
  for(const entry of entries)if(entry.section===section)dispose(entry);
  section.dataset.trailer=signature;section.replaceChildren(node('h3','','Trailer'));
  const external=node('a','detail-source',ids.length?'Abrir no YouTube ↗':'Pesquisar trailer no YouTube ↗');
  external.href=ids.length?'https://www.youtube.com/watch?v='+ids[0]:'https://www.youtube.com/results?search_query='+encodeURIComponent([movie.title,movie.year,'trailer oficial'].filter(Boolean).join(' '));external.target='_blank';external.rel='noopener noreferrer';
  if(!ids.length){
   section.append(node('p','trailer-state',movie.tmdbConfigured===false?'Nenhum trailer vinculado. Conecte o TMDB em Configurações para buscar as prévias deste título.':'Nenhum trailer encontrado para esta identificação. Use Buscar trailer para consultar novamente ou corrija a ficha do título.'),external);return;
  }
  const stage=node('div','trailer-stage'),status=node('p','trailer-state','Carregando o player do YouTube…');status.setAttribute('role','status');section.append(stage,status);
  const entry={section,player:null,frame:null,timer:null,wantPlay:false,ready:false,index:0,tried:new Set()};entries.add(entry);
  const select=node('select');select.setAttribute('aria-label','Escolher prévia do título');select.hidden=ids.length<2;
  ids.forEach((id,index)=>{const option=node('option','', 'Prévia '+(index+1)+' de '+ids.length);option.value=index;select.append(option);});select.onchange=()=>load(Number(select.value));
  async function load(index=entry.index){
   if(!section.isConnected||!entries.has(entry))return;
   clearTimeout(entry.timer);entry.index=index;entry.tried.add(index);entry.ready=false;entry.wantPlay=false;select.value=String(index);
   try{entry.player?.destroy();}catch{}stage.replaceChildren();
   const frame=node('iframe');entry.frame=frame;frame.id='cinevo-trailer-'+(++sequence);frame.title='Trailer de '+movie.title;frame.allow='autoplay; encrypted-media; fullscreen; picture-in-picture';frame.allowFullscreen=true;frame.referrerPolicy='strict-origin-when-cross-origin';frame.tabIndex=0;
   const url=new URL('https://www.youtube-nocookie.com/embed/'+ids[index]);url.search=new URLSearchParams({enablejsapi:'1',origin:location.origin,playsinline:'1',rel:'0',autoplay:'0'});frame.src=url.href;stage.append(frame);external.href='https://www.youtube.com/watch?v='+ids[index];status.textContent='Carregando o player do YouTube…';
   const current=()=>entries.has(entry)&&entry.frame===frame&&section.isConnected;
   entry.timer=setTimeout(()=>{if(current()&&!entry.ready)status.textContent='O player demorou para carregar. Confira a conexão ou use Recarregar player.';},20000);
   try{
    const YT=await api();if(!current())return;
    entry.player=new YT.Player(frame,{events:{
     onReady:event=>{if(!current())return;clearTimeout(entry.timer);entry.ready=true;status.textContent='Trailer pronto. Use o play para assistir aqui no Cinevo.';if(entry.wantPlay){entry.wantPlay=false;event.target.playVideo();}},
     onError:event=>{if(!current())return;clearTimeout(entry.timer);entry.ready=false;
      const next=ids.findIndex((id,i)=>!entry.tried.has(i));
      if([100,101,150].includes(event.data)&&next>=0){status.textContent='Esta prévia está indisponível. Carregando outra do mesmo título…';load(next);return;}
      status.textContent=event.data===101||event.data===150?'Este vídeo não permite reprodução em outros aplicativos. Use Abrir no YouTube.':event.data===100?'O trailer foi removido ou está privado no YouTube.':event.data===153?'O YouTube não reconheceu esta reprodução. Tente recarregar o player ou abra o vídeo no YouTube.':'O YouTube não conseguiu reproduzir o trailer. Tente recarregar ou use o link abaixo.';
     },
     onAutoplayBlocked:()=>{if(current())status.textContent='Clique no play dentro do vídeo para iniciar o trailer.';},
     onStateChange:event=>{if(!current())return;if(event.data===1)status.textContent='Reproduzindo no Cinevo';else if(event.data===2)status.textContent='Trailer pausado';}
    }});
   }catch(error){clearTimeout(entry.timer);if(current())status.textContent=error.message;}
  }
  const tools=node('div','trailer-tools');tools.append(select,button('Recarregar player','secondary',()=>load()),external);section.append(tools);load(0);
 }
 new MutationObserver(()=>{for(const entry of entries)if(!entry.section.isConnected)dispose(entry);}).observe(document.body,{subtree:true,childList:true});
 document.addEventListener('visibilitychange',()=>{if(document.hidden)for(const entry of entries)try{entry.player?.pauseVideo();}catch{}});
 window.CinevoTrailers={render};
})();
