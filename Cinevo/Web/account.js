async function openAccountTv() {
  const view=smartDialog('Conta e TV');
  let data;
  try{data=await transferAction('account-read');}catch(error){view.status.textContent=error.message;return;}
  const accountSection=node('section','settings-card');accountSection.append(node('h3','',data.username?'Conectado como '+data.username:'Sua conta Cinevo'),node('p','',data.status));view.body.append(accountSection);
  accountSection.append(node('p','meta','Ao conectar, títulos da biblioteca, favoritos e progresso são sincronizados na sua conta. Arquivos de vídeo, caminhos de pastas, credenciais de outros serviços e library.json permanecem no PC. O catálogo de Filmes importados fica local até você adicionar títulos à biblioteca.'));
  if(data.username){
    accountSection.append(button('Sincronizar agora','secondary',async()=>{try{const result=await transferAction('account-sync');view.status.textContent=result.status;await refresh(false,true);}catch(error){view.status.textContent=error.message;}}),button('Sair da conta','secondary',async()=>{try{await transferAction('account-logout');view.dialog.close();openAccountTv();}catch(error){view.status.textContent=error.message;}}));
  }else{
    const form=node('form','smart-grid');accountSection.append(form);
    const username=smartField(form,'Nome de usuário','','text');username.autocomplete='username';username.required=true;username.pattern='[a-zA-Z0-9_.-]{3,40}';
    const password=smartField(form,'Senha (12 caracteres ou mais)','','password');password.autocomplete='current-password';password.minLength=12;password.maxLength=128;password.required=true;
    const signup=smartField(form,'Criar uma nova conta',null,'checkbox');
    accountSection.append(node('p','meta','Nesta Beta, a conta usa nome de usuário e senha, sem recuperação por e-mail. Guarde sua senha. Conectar é opcional; sua biblioteca continua disponível offline.'));
    smartSubmit(form,'Conectar conta',async()=>{await transferAction(signup.checked?'account-register':'account-login',null,JSON.stringify({username:username.value,password:password.value}));password.value='';view.dialog.close();openAccountTv();},view.status);
  }
  const central=node('section','settings-card');central.append(node('h3','','Cinema na TV e no controle'),node('p','','Ative a central, abra o endereço no navegador da TV e digite o código. PC e TV precisam estar na mesma rede privada. O PC e o disco dos vídeos devem permanecer ligados. Se o Windows pedir acesso à rede, permita a rede privada.'));
  central.append(node('p','meta','No PC, abra o modo TV pelo endereço local para usar o controle de videogame. A navegação também aceita setas e Enter. A reprodução depende dos formatos aceitos pelo navegador; esta versão não converte os vídeos.'));
  view.body.append(central);const connection=node('div','tv-connection');central.append(connection);
  async function tvInfo(command){try{const info=await transferAction(command);connection.replaceChildren();if(info.enabled){connection.append(node('p','','Código de pareamento (5 minutos):'),node('strong','tv-code',info.pin));
    for(const address of info.addresses)connection.append(node('p','tv-address',address));if(!info.addresses.length)connection.append(node('p','','Conecte o PC a uma rede privada para acessar pela TV.'));
    const local=node('a','button primary','Abrir modo TV neste PC');local.href=info.local;local.target='_blank';local.rel='noopener';connection.append(local,button('Novo código','secondary',()=>tvInfo('tv-code')),button('Desativar central e desconectar TVs','secondary',()=>tvInfo('tv-stop')));
  }else connection.append(button('Ativar central TV','primary',()=>tvInfo('tv-start')));}catch(error){view.status.textContent=error.message;}}
  await tvInfo('tv-read');
}
const accountButton=button('Conta e TV','secondary',openAccountTv);$('.sidebar-support').prepend(accountButton);
const tvModeButton=button('Modo controle','secondary',()=>{const enabled=!document.documentElement.classList.contains('tv-mode');CinevoControls.setMode(enabled);tvModeButton.textContent=enabled?'Sair do modo controle':'Modo controle';});$('.smart-toolbar').append(tvModeButton);
const soundButton=button('Sons de navegação','secondary',()=>{CinevoControls.setSound(!CinevoControls.soundEnabled());soundButton.textContent=CinevoControls.soundEnabled()?'Sons: ligados':'Sons: desligados';});$('.smart-toolbar').append(soundButton);
