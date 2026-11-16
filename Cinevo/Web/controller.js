(function(){
  'use strict';
  var clickVolume=30,muted=localStorage.getItem('cinevo-sounds')==='off',context=null;
  var selector='button,summary,a[href],input,select,textarea,[tabindex="0"]';
  var contentMemory=new Map(),lastSidebar=null;
  var remembered=new Map(),scopeNow=null,scopeKey='',lastFocus=null,editing=null,anchorX=null,repairPending=false;
  var previous=[],padId='',directionHeld=null,axisHeld=null,repeatAt=0,armed=false,keyHeld=null,keyAt=0;
  var help=document.createElement('div');help.className='control-help';help.hidden=true;help.setAttribute('aria-hidden','true');document.body.appendChild(help);
  function helpItem(key,label){var n=document.createElement('span'),b=document.createElement('b');b.textContent=key;n.appendChild(b);n.appendChild(document.createTextNode(label));return n;}
  function updateHelp(){var card=document.activeElement.closest('.card');help.hidden=!active()||scope()!==document||!card;if(help.hidden)return;help.textContent='';help.appendChild(helpItem('A','Abrir ficha'));if(card.querySelector('[data-control-action=add]'))help.appendChild(helpItem('X','Adicionar à biblioteca'));help.appendChild(helpItem('B','Menu lateral'));}
  function quickAction(){var card=document.activeElement.closest('.card'),control=card&&card.querySelector('[data-control-action=add]');if(control&&visible(control)&&scope()===document){unlock();input();control.click();}}
  function active(){return document.documentElement.classList.contains('tv-mode');}
  function input(){document.dispatchEvent(new Event('cinevo-input'));}
  function sound(click){if(muted||clickVolume===0||!context||context.state!=='running')return;try{var osc=context.createOscillator(),gain=context.createGain(),now=context.currentTime;osc.type='sine';osc.frequency.setValueAtTime(click?520:370,now);osc.frequency.exponentialRampToValueAtTime(click?680:440,now+.035);gain.gain.setValueAtTime(.0001,now);gain.gain.exponentialRampToValueAtTime(.025*clickVolume/30,now+.008);gain.gain.exponentialRampToValueAtTime(.0001,now+.075);osc.connect(gain);gain.connect(context.destination);osc.start(now);osc.stop(now+.08);}catch(e){}}
  function unlock(force){if((!active()&&!force)||muted)return;try{var Audio=window.AudioContext||window.webkitAudioContext;if(Audio&&!context)context=new Audio();if(context&&context.resume)context.resume();}catch(e){}}
  function visible(el){if(!el||!el.isConnected||el.classList.contains('skip-link')||el.disabled||el.getAttribute('aria-disabled')==='true'||el.tabIndex<0||el.closest('[hidden],.hidden,[inert],[aria-hidden="true"]'))return false;var r=el.getBoundingClientRect();if(!r.width||!r.height)return false;for(var p=el;p&&p!==document.body;p=p.parentElement){var s=getComputedStyle(p);if(s.visibility==='hidden'||s.display==='none'||s.opacity==='0')return false;}return true;}
  function shown(el){return !el.closest('[hidden],.hidden')&&el.getBoundingClientRect().width>0&&getComputedStyle(el).visibility!=='hidden';}
  function scope(){var dialogs=Array.prototype.filter.call(document.querySelectorAll('dialog[open]'),shown),root=dialogs.length?dialogs[dialogs.length-1]:document;
    if(root===document){var overlays=Array.prototype.filter.call(document.querySelectorAll('.tv-overlay,#player-panel,#pairing'),shown);overlays.sort(function(a,b){return (parseInt(getComputedStyle(a).zIndex)||0)-(parseInt(getComputedStyle(b).zIndex)||0);});if(overlays.length)root=overlays[overlays.length-1];}
    var menus=Array.prototype.filter.call(root.querySelectorAll('details.menu[open]'),shown);return menus.length?menus[menus.length-1]:root;
  }
  function route(root){if(root!==document)return root;var label=document.getElementById('current-section');return 'page:'+(label?label.textContent:(document.getElementById('tv-imports')||{}).textContent||'library');}
  function controls(root){return Array.prototype.filter.call((root||scope()).querySelectorAll(selector),visible);}
  function signature(el,root){if(el.id)return {id:el.id};var movie=el.closest('[data-movie]');if(movie)return {movie:movie.getAttribute('data-movie'),index:Array.prototype.indexOf.call(movie.querySelectorAll(selector),el)};return {text:el.textContent,tag:el.tagName,index:controls(root).indexOf(el)};}
  function remember(el,root,key){if(!el||!visible(el))return;remembered.set(key,{el:el,signature:signature(el,root),rect:el.getBoundingClientRect()});if(remembered.size>24)remembered.delete(remembered.keys().next().value);}
  function recalled(root,key){var saved=remembered.get(key),list=controls(root);if(!saved)return null;if(list.indexOf(saved.el)>=0)return saved.el;var s=saved.signature,target=null;if(s.id)target=document.getElementById(s.id);if(s.movie){var card=Array.prototype.filter.call(root.querySelectorAll('[data-movie]'),function(n){return n.getAttribute('data-movie')===s.movie;})[0];target=card&&card.querySelectorAll(selector)[s.index];}if(list.indexOf(target)>=0)return target;return list.filter(function(n){return n.tagName===s.tag&&n.textContent===s.text;})[0]||list[Math.min(Math.max(s.index||0,0),list.length-1)];}
  function focus(el,scroll){if(!el||!visible(el))return;try{el.focus({preventScroll:true});}catch(e){el.focus();}if(scroll!==false){var target=el.closest('.card')||el;try{target.scrollIntoView({block:'nearest',inline:'nearest',behavior:'instant'});}catch(e){target.scrollIntoView(false);}}}
  function sync(){var root=scope(),key=route(root),changed=key!==scopeKey;if(changed){var closing=scopeNow&&scopeNow!==document&&(!scopeNow.isConnected||!shown(scopeNow)||scopeNow.tagName==='DIALOG'&&!scopeNow.open||scopeNow.tagName==='DETAILS'&&!scopeNow.open);scopeNow=root;scopeKey=key;anchorX=null;editing=null;repeatAt=Date.now()+340;var target=recalled(root,key);if(closing&&target)focus(target);else if(controls(root).indexOf(document.activeElement)<0)focus(target||controls(root)[0]);}
    else if(controls(root).indexOf(document.activeElement)<0&&document.activeElement!==editing)focus(recalled(root,key)||controls(root)[0],false);
    return root;
  }
  function reveal(){return !!(window.cinevoRevealControls&&window.cinevoRevealControls());}
  function change(el,value){el.value=value;el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));input();}
  function fieldDirection(el,direction){if(el.tagName==='INPUT'&&el.type==='range'&&(direction==='left'||direction==='right')){var step=Number(el.step)||1,min=el.min===''?0:Number(el.min),max=el.max===''?100:Number(el.max);change(el,String(Math.max(min,Math.min(max,Number(el.value)+(direction==='right'?step:-step)))));return true;}
    if(editing!==el)return false;if(el.tagName==='SELECT'&&(direction==='up'||direction==='down')){var index=el.selectedIndex,step=direction==='down'?1:-1;do{index+=step;}while(index>=0&&index<el.options.length&&el.options[index].disabled);if(index>=0&&index<el.options.length){el.selectedIndex=index;el.dispatchEvent(new Event('change',{bubbles:true}));}return true;}return false;
  }
  function cardMove(current,direction){
    var card=current.closest('.card'),group=card&&card.closest('.grid,.rail');if(!card||!group)return false;
    var rect=card.getBoundingClientRect(),horizontal=direction==='left'||direction==='right',x=rect.left+rect.width/2,y=rect.top+rect.height/2;
    if(horizontal)anchorX=null;else if(anchorX===null)anchorX=x;
    var entries=Array.prototype.map.call(group.querySelectorAll('.card'),function(c){var b=c.querySelector('.poster-button');return b&&visible(b)?{el:b,r:c.getBoundingClientRect()}:null;}).filter(Boolean);
    var ranked=entries.filter(function(c){if(c.el.closest('.card')===card)return false;var dx=c.r.left+c.r.width/2-x,dy=c.r.top+c.r.height/2-y;if(horizontal)return Math.abs(dy)<Math.min(rect.height,c.r.height)*.35&&(direction==='left'?dx<-4:dx>4);return direction==='up'?dy<-Math.min(rect.height,c.r.height)*.45:dy>Math.min(rect.height,c.r.height)*.45;});
    ranked.sort(function(a,b){if(horizontal)return Math.abs(a.r.left-rect.left)-Math.abs(b.r.left-rect.left);var rowA=Math.abs(a.r.top-rect.top),rowB=Math.abs(b.r.top-rect.top);return Math.abs(rowA-rowB)>20?rowA-rowB:Math.abs(a.r.left+a.r.width/2-anchorX)-Math.abs(b.r.left+b.r.width/2-anchorX);});
    if(ranked.length){input();focus(ranked[0].el);return true;}
    if(horizontal){if(direction==='left'&&scope()===document){var nav=lastSidebar&&visible(lastSidebar)?lastSidebar:document.querySelector('.sidebar nav [aria-current]');if(visible(nav)){input();focus(nav);}}return true;}return false;
  }
  function move(direction){if(!active())return;var root=sync();if(reveal()){input();return;}var list=controls(root),current=document.activeElement;if(list.indexOf(current)<0){focus(list[0]);return;}if(fieldDirection(current,direction))return;if(editing===current&&/INPUT|TEXTAREA/.test(current.tagName))return;
    if(current.closest('.sidebar')&&direction==='right'){var saved=contentMemory.get(route(document)),primary=saved&&visible(saved)?saved:Array.prototype.filter.call(document.querySelectorAll('#main .poster-button'),visible)[0];if(primary){input();focus(primary);return;}}
    if(cardMove(current,direction))return;
    var rect=current.getBoundingClientRect(),horizontal=direction==='left'||direction==='right',x=rect.left+rect.width/2,y=rect.top+rect.height/2;if(horizontal)anchorX=null;else if(anchorX===null)anchorX=x;var candidates=[],rail=current.closest('.rail,.tv-home-rail,.season-tabs'),sidebar=current.closest('.sidebar');
    list.forEach(function(el){if(el===current||el.closest('.card')&&!el.classList.contains('poster-button'))return;var r=el.getBoundingClientRect(),dx=r.left+r.width/2-x,dy=r.top+r.height/2-y,main=direction==='left'?-dx:direction==='right'?dx:direction==='up'?-dy:dy;if(main<=4)return;
      if(!horizontal&&!!el.closest('.sidebar')!==!!sidebar)return;
      var cross=horizontal?Math.max(0,Math.max(rect.top,r.top)-Math.min(rect.bottom,r.bottom)):Math.max(0,r.left-anchorX,anchorX-r.right);
      var aligned=horizontal?Math.abs(dy)<Math.max(18,Math.min(rect.height,r.height)*.45):cross===0;
      var gap=horizontal?(direction==='right'?r.left-rect.right:rect.left-r.right):(direction==='down'?r.top-rect.bottom:rect.top-r.bottom);
      candidates.push({el:el,rank:(rail&&horizontal&&rail.contains(el)?-2:0)+(aligned?0:1),score:Math.max(0,gap)+cross*3+(horizontal?Math.abs(dy)*.2:Math.abs(r.left+r.width/2-anchorX)*.12)});
    });
    candidates.sort(function(a,b){return a.rank-b.rank||a.score-b.score;});if(candidates.length){editing=null;input();focus(candidates[0].el);}
  }
  function chooser(field){
    var modal=document.createElement('dialog'),nativeDialog=typeof modal.showModal==='function';if(!nativeDialog)modal=document.createElement('section');modal.className='control-keyboard'+(nativeDialog?'':' tv-overlay');modal.setAttribute('role','dialog');modal.setAttribute('aria-modal','true');modal.setAttribute('data-controller-modal','');modal.style.zIndex=100;
    var heading=document.createElement('h2');heading.textContent=field.getAttribute('aria-label')||field.placeholder||'Editar campo';modal.appendChild(heading);var first=null;
    function close(){if(nativeDialog)modal.close();modal.remove();editing=null;focus(field,false);}
    modal.controlClose=close;modal.addEventListener('cancel',function(e){e.preventDefault();close();});
    function button(label,run){var b=document.createElement('button');b.type='button';b.className='button secondary';b.textContent=label;b.onclick=run;return b;}
    if(field.tagName==='SELECT'){
      var options=document.createElement('div');options.className='control-options';Array.prototype.forEach.call(field.options,function(option,index){if(option.disabled||option.hidden)return;var b=button(option.textContent,function(){close();field.selectedIndex=index;field.dispatchEvent(new Event('change',{bubbles:true}));});b.setAttribute('aria-pressed',field.selectedIndex===index?'true':'false');if(field.selectedIndex===index)first=b;options.appendChild(b);});modal.appendChild(options);
    }else{
      var draft=document.createElement('input');draft.type=field.type==='password'?'password':'text';draft.value=field.value;draft.setAttribute('aria-label',heading.textContent);draft.maxLength=field.maxLength>0?field.maxLength:8192;modal.appendChild(draft);
      var grid=document.createElement('div');grid.className='control-keys';var uppercase=false,letters=field.type==='number'?'1234567890.-':'abcdefghijklmnopqrstuvwxyz1234567890@./:-_?=&%+!#$*()çãéíóú';
      function add(value){var start=draft.selectionStart==null?draft.value.length:draft.selectionStart,end=draft.selectionEnd==null?start:draft.selectionEnd;var next=draft.value.slice(0,start)+value+draft.value.slice(end);if(next.length>draft.maxLength)return;draft.value=next;draft.setSelectionRange(start+value.length,start+value.length);}
      letters.split('').forEach(function(letter){var b=button(letter,function(){add(uppercase?letter.toUpperCase():letter);});if(!first)first=b;grid.appendChild(b);});modal.appendChild(grid);var actions=document.createElement('div');actions.className='keyboard-actions';var shift=button('Maiúsculas',function(){uppercase=!uppercase;shift.setAttribute('aria-pressed',uppercase?'true':'false');Array.prototype.forEach.call(grid.children,function(key,index){key.textContent=uppercase?letters[index].toUpperCase():letters[index];});});shift.setAttribute('aria-pressed','false');actions.appendChild(shift);actions.appendChild(button('Espaço',function(){add(' ');}));actions.appendChild(button('Apagar',function(){var end=draft.selectionEnd==null?draft.value.length:draft.selectionEnd,start=draft.selectionStart==null?end:draft.selectionStart;if(end===start)start=Math.max(0,start-1);draft.value=draft.value.slice(0,start)+draft.value.slice(end);draft.setSelectionRange(start,start);}));actions.appendChild(button('Limpar',function(){draft.value='';}));actions.appendChild(button('Concluir',function(){var value=draft.value;close();if(field.isConnected)change(field,value);}));modal.appendChild(actions);var help=document.createElement('p');help.className='keyboard-help';help.textContent='Direcional: escolher · A: confirmar · B: cancelar. Você também pode digitar no teclado.';modal.appendChild(help);
    }
    modal.appendChild(button('Cancelar',close));document.body.appendChild(modal);if(nativeDialog)modal.showModal();var initial=first||modal.querySelector('button'),attempts=0;function focusKeys(){if(!modal.isConnected||editing)return;if(visible(initial)){focus(initial);return;}if(++attempts<10)setTimeout(focusKeys,40);}focusKeys();input();
  }
  function activate(){var root=sync();if(reveal()){input();return;}var el=document.activeElement;if(controls(root).indexOf(el)<0){focus(controls(root)[0]);return;}input();if(el.tagName==='SELECT'||el.tagName==='TEXTAREA'||el.tagName==='INPUT'&&!/checkbox|radio|button|submit|file|range/.test(el.type)){if(el.closest('[data-controller-modal]')){editing=el;el.focus();return;}chooser(el);return;}if(el.click)el.click();}
  function back(){var root=sync();input();if(root.controlClose){root.controlClose();return;}if(editing){editing.removeAttribute('data-control-editing');editing=null;return;}if(root.tagName==='DETAILS'){root.open=false;return;}if(root.tagName==='DIALOG'){var event=new Event('cancel',{cancelable:true});if(root.dispatchEvent(event))root.close();return;}if(window.cinevoBack){window.cinevoBack();return;}
    var profile=document.getElementById('watch-history');if(profile&&!profile.hidden&&typeof changeSection==='function'){changeSection('library');return;}
    var nav=document.querySelector('.sidebar nav [aria-current="page"]');if(nav&&document.activeElement!==nav){remember(document.activeElement,root,scopeKey);focus(nav);}
  }
  function reset(){previous=[];padId='';directionHeld=null;axisHeld=null;armed=false;keyHeld=null;}
  function tvClient(){return document.body.classList.contains('tv-client');}
  var lastPhysicalInput=0;
  function setMode(value,automatic){var changed=active()!==value;document.documentElement.classList.toggle('tv-mode',value);if(!automatic)reset();else{directionHeld=null;repeatAt=Date.now()+340;}if(!value){help.hidden=true;editing=null;armed=false;}else if(changed){unlock();Array.prototype.forEach.call(document.querySelectorAll('details.menu[open]'),function(menu){menu.open=false;});sync();var current=document.activeElement,content=contentMemory.get(route(document));if(!visible(content))content=document.querySelector('#main .poster-button,#movies .movie');if(scope()===document&&visible(content)&&!current.closest('dialog'))focus(content);}
   if(changed)document.dispatchEvent(new CustomEvent('cinevo-modechange',{detail:{controller:value}}));
  }
  function physicalInput(event){if(tvClient()||!event.isTrusted)return;lastPhysicalInput=Date.now();if(active())setMode(false,true);else armed=false;}
  document.addEventListener('pointermove',function(event){if(event.pointerType==='mouse'&&(Math.abs(event.movementX)+Math.abs(event.movementY)>0))physicalInput(event);},true);
  document.addEventListener('pointerdown',physicalInput,true);
  window.CinevoControls={setMode:setMode,setSound:function(value){muted=!value;localStorage.setItem('cinevo-sounds',value?'on':'off');unlock();sound(true);},soundEnabled:function(){return !muted&&clickVolume>0;},previewSound:function(){unlock(true);sound(true);},setVolume:function(value){clickVolume=Math.max(0,Math.min(100,value));},move:move,back:back,quickAction:quickAction};
  document.addEventListener('focusin',function(e){if(!active())return;var root=scope(),key=route(root);if(root===document||root.contains(e.target)){if(lastFocus)lastFocus.removeAttribute('data-control-focus');e.target.setAttribute('data-control-focus','');lastFocus=e.target;if(e.target.closest('.sidebar'))lastSidebar=e.target;else if(e.target.classList.contains('poster-button')){contentMemory.set(route(document),e.target);if(contentMemory.size>12)contentMemory.delete(contentMemory.keys().next().value);}remember(e.target,root,key);sound(false);updateHelp();}});
  document.addEventListener('pointerdown',function(){editing=null;anchorX=null;help.hidden=true;});document.addEventListener('focusout',function(){setTimeout(updateHelp,0);});
  document.addEventListener('click',function(e){if(!active())return;unlock();if(e.target.closest('button,a'))sound(true);},true);
  document.addEventListener('keydown',function(e){if(!tvClient()&&e.isTrusted){physicalInput(e);return;}if(!active())return;var code=e.keyCode,key=e.key,dir={37:'left',38:'up',39:'right',40:'down'}[code]||{ArrowLeft:'left',ArrowUp:'up',ArrowRight:'right',ArrowDown:'down'}[key],escape=key==='Escape'||code===461||code===10009;
    if(escape){e.preventDefault();e.stopImmediatePropagation();if(!e.repeat)back();return;}if(editing===e.target&&/INPUT|TEXTAREA/.test(e.target.tagName)&&key!=='Enter')return;
    if(dir){e.preventDefault();e.stopImmediatePropagation();var now=Date.now();if(!e.repeat||keyHeld!==dir){keyHeld=dir;keyAt=now+320;move(dir);}else if(now>=keyAt){keyAt=now+140;move(dir);}return;}
    if(key==='Enter'||code===13){if(e.target.tagName==='TEXTAREA'&&editing===e.target)return;e.preventDefault();e.stopImmediatePropagation();if(!e.repeat){unlock();activate();}return;}
    if(/INPUT|TEXTAREA/.test(e.target.tagName)&&key&&key.length===1)editing=e.target;
  },true);
  document.addEventListener('keyup',function(){keyHeld=null;});window.addEventListener('blur',reset);document.addEventListener('visibilitychange',reset);
  new MutationObserver(function(records){if(!active()||repairPending||!records.some(function(r){return r.type==='childList'||['open','hidden','class','disabled','aria-hidden'].indexOf(r.attributeName)>=0;}))return;repairPending=true;requestAnimationFrame(function(){repairPending=false;if(active()&&document.hasFocus())sync();});}).observe(document.body,{subtree:true,childList:true,attributes:true,attributeFilter:['open','hidden','class','disabled','aria-hidden']});
  function padDirection(pad,buttons){if(buttons[14])return 'left';if(buttons[15])return 'right';if(buttons[12])return 'up';if(buttons[13])return 'down';var x=pad.axes[0]||0,y=pad.axes[1]||0,threshold=axisHeld ? .42 : .65;if(Math.max(Math.abs(x),Math.abs(y))<threshold){axisHeld=null;return null;}var horizontal=axisHeld==='left'||axisHeld==='right';if(axisHeld&&((horizontal&&Math.abs(x)>=.42&&Math.abs(y)<Math.abs(x)+.18)||(!horizontal&&Math.abs(y)>=.42&&Math.abs(x)<Math.abs(y)+.18))){axisHeld=horizontal?(x<0?'left':'right'):(y<0?'up':'down');return axisHeld;}axisHeld=Math.abs(x)>Math.abs(y)?x<0?'left':'right':y<0?'up':'down';return axisHeld;}
  function frame(){var now=Date.now();if(!document.hidden&&document.hasFocus()&&navigator.getGamepads){var pads=navigator.getGamepads(),pad=null;for(var i=0;i<pads.length;i++)if(pads[i]&&pads[i].connected!==false&&pads[i].mapping==='standard'){if(!pad)pad=pads[i];if(pads[i].buttons.some(function(b){return b.pressed;})||pads[i].axes.some(function(a){return Math.abs(a)>.65;})){pad=pads[i];break;}}
   if(pad){if(padId!==pad.id+pad.index){reset();padId=pad.id+pad.index;}var buttons=pad.buttons.map(function(b){return b.pressed;}),direction=padDirection(pad,buttons),intent=buttons.some(Boolean)||pad.axes.some(function(a){return Math.abs(a)>.65;});
    if(!tvClient()&&now-lastPhysicalInput<250){armed=false;previous=buttons;directionHeld=null;}
    else if(!armed){if(!intent){armed=true;axisHeld=null;}previous=buttons;}
    else if(!active()&&intent){setMode(true,true);directionHeld=direction;repeatAt=now+340;previous=buttons;}
    else if(active()){if(direction!==directionHeld){directionHeld=direction;repeatAt=now+340;if(direction)move(direction);}else if(direction&&now>=repeatAt){repeatAt=now+145;move(direction);}if(buttons[0]&&!previous[0]){unlock();activate();}if(buttons[1]&&!previous[1])back();if(buttons[2]&&!previous[2])quickAction();if(buttons[9]&&!previous[9]&&window.cinevoTogglePlayback&&document.getElementById('player-panel')&&shown(document.getElementById('player-panel'))){input();window.cinevoTogglePlayback();}previous=buttons;}else previous=buttons;
   }else reset();
  }else{reset();help.hidden=true;}if(active()&&!document.hidden&&document.hasFocus())requestAnimationFrame(frame);else setTimeout(frame,document.hidden?500:120);}
  if(window.requestAnimationFrame)requestAnimationFrame(frame);
})();
