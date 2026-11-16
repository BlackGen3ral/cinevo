(function(){
 var ambient=document.createElement('div');ambient.className='ambient';ambient.setAttribute('aria-hidden','true');document.body.insertBefore(ambient,document.body.firstChild);
 var player=document.getElementById('player-panel');
 function update(){var playing=player&&!player.classList.contains('hidden');document.body.classList.toggle('ambient-still',document.hidden||!document.hasFocus()||!!playing);}
 document.addEventListener('visibilitychange',update);window.addEventListener('focus',update);window.addEventListener('blur',update);
 if(player&&window.MutationObserver)new MutationObserver(update).observe(player,{attributes:true,attributeFilter:['class']});
 update();
})();
