(function(){
 const entries=new Set();let apiPromise,sequence=0;
 function api(){if(window.YT?.Player)return Promise.resolve(window.YT);if(apiPromise)return apiPromise;
  apiPromise=new Promise((resolve,reject)=>{let complete=false;const script=document.createElement('script'),previous=window.onYouTubeIframeAPIReady;function finish(error){if(complete)return;complete=true;clearTimeout(timer);if(error){apiPromise=null;script.remove();reject(error);}else resolve(window.YT);}
   window.onYouTubeIframeAPIReady=()=>{try{previous?.();}finally{finish();}};script.src='https://www.youtube.com/iframe_api';script.referrerPolicy='strict-origin-when-cross-origin';script.onerror=()=>finish(new Error('O YouTube não respondeu. Confira a conexão e tente novamente.'));const timer=setTimeout(()=>finish(new Error('O YouTube demorou para responder. Tente novamente.')),15000);document.head.append(script);
  });return apiPromise;
 }
 function dispose(entry){clearTimeout(entry.timer);try{entry.player?.destroy();}catch{}entry.frame?.remove();entries.delete(entry);}
 function render(section,actions,movie,video){
  const id=typeof video==='string'&&/^[A-Za-z0-9_-]{11}$/.test(video)?video:'';actions.querySelector('[data-trailer-action]')?.remove();
  const play=button(id?'Ver trailer':'Buscar trailer','secondary',()=>{section.scrollIntoView({block:'nearest'});const entry=[...entries].find(e=>e.section===section);if(entry){if(entry.player&&entry.ready){entry.player.playVideo();entry.frame?.focus({preventScroll:true});}else entry.wantPlay=true;}else section.querySelector('a')?.focus();});play.dataset.trailerAction='true';actions.append(play);
  if(section.dataset.trailer===id&&section.childElementCount)return;for(const entry of entries)if(entry.section===section)dispose(entry);
  section.dataset.trailer=id;section.replaceChildren(node('h3','','Trailer'));
  const external=node('a','detail-source',id?'Abrir no YouTube ↗':'Pesquisar trailer no YouTube ↗');external.href=id?'https://www.youtube.com/watch?v='+id:'https://www.youtube.com/results?search_query='+encodeURIComponent([movie.title,movie.year,'trailer oficial'].filter(Boolean).join(' '));external.target='_blank';external.rel='noopener noreferrer';
  if(!id){section.append(node('p','meta','Nenhum trailer vinculado a este título. Você pode adicionar o link na identificação do filme.'),external);return;}
  const stage=node('div','trailer-stage'),status=node('p','trailer-state','Carregando o player do YouTube…');status.setAttribute('role','status');section.append(stage,status);
  const entry={section,player:null,frame:null,timer:null,wantPlay:false,ready:false};entries.add(entry);
  async function load(){if(!section.isConnected)return;entry.ready=false;entry.wantPlay=false;try{entry.player?.destroy();}catch{}stage.replaceChildren();const frame=node('iframe');entry.frame=frame;frame.id='cinevo-trailer-'+(++sequence);frame.title='Trailer de '+movie.title;frame.allow='autoplay; encrypted-media; fullscreen; picture-in-picture';frame.allowFullscreen=true;frame.referrerPolicy='strict-origin-when-cross-origin';frame.tabIndex=0;
   const url=new URL('https://www.youtube-nocookie.com/embed/'+id);url.search=new URLSearchParams({enablejsapi:'1',origin:location.origin,playsinline:'1',rel:'0',autoplay:'0'});frame.src=url.href;stage.append(frame);status.textContent='Carregando o player do YouTube…';
   try{const YT=await api();if(!section.isConnected||!entries.has(entry)||entry.frame!==frame)return;
    entry.player=new YT.Player(frame,{events:{onReady:event=>{if(!entries.has(entry))return;entry.ready=true;status.textContent='Trailer pronto. Use o play para assistir aqui no Cinevo.';if(entry.wantPlay){entry.wantPlay=false;event.target.playVideo();}},onError:event=>{if(!entries.has(entry))return;status.textContent=event.data===101||event.data===150?'Este vídeo não permite reprodução em outros aplicativos. Use Abrir no YouTube.':event.data===100?'O trailer foi removido ou está privado no YouTube.':event.data===153?'O YouTube não reconheceu esta reprodução. Tente recarregar o player ou abra o vídeo no YouTube.':'O YouTube não conseguiu reproduzir o trailer. Tente recarregar ou use o link abaixo.';},onAutoplayBlocked:()=>{if(entries.has(entry))status.textContent='Clique no play dentro do vídeo para iniciar o trailer.';},onStateChange:event=>{if(!entries.has(entry))return;if(event.data===1)status.textContent='Reproduzindo no Cinevo';else if(event.data===2)status.textContent='Trailer pausado';}}});
   }catch(error){if(section.isConnected&&entries.has(entry))status.textContent=error.message;}
  }
  const tools=node('div','trailer-tools');tools.append(button('Recarregar player','secondary',load),external);section.append(tools);load();
 }
 new MutationObserver(()=>{for(const entry of entries)if(!entry.section.isConnected)dispose(entry);}).observe(document.body,{subtree:true,childList:true});
 document.addEventListener('visibilitychange',()=>{if(document.hidden)for(const entry of entries)try{entry.player?.pauseVideo();}catch{}});
 window.CinevoTrailers={render};
})();
