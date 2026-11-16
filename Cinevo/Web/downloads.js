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
  const card = node('article', 'transfer-card');
  const title = node('h2');
  const badge = node('span', 'transfer-badge');
  const header = node('div', 'transfer-card-header');
  header.append(title, badge);
  const percent = node('strong');
  const bytes = node('span');
  const metrics = node('div', 'transfer-metrics');
  metrics.append(bytes, percent);
  const track = node('div', 'transfer-track');
  track.setAttribute('role', 'progressbar');
  track.setAttribute('aria-label', 'Progresso do download');
  const fill = node('span');
  track.append(fill);
  const foot = node('div', 'transfer-card-foot');
  const rates = node('span');
  let mode;
  const control = button('', 'secondary', event => actOnTransfer(
    control.dataset.action, hash, event.currentTarget));
  const controls = node('div', 'transfer-actions');
  const remove = button('Remover', 'secondary', () => confirmRemoval('Remover transferência?',
    title.textContent + ' sai da fila do qBittorrent. Os arquivos ficam no PC.', async () => {
      await transferAction('torrent-remove', hash);
      refreshTransfers(true);
    }));
  const reannounce = button('Buscar pares novamente', 'secondary', event =>
    actOnTransfer('torrent-reannounce', hash, event.currentTarget));
  controls.append(button('Organizar / episódios', 'secondary', () => organizeTorrent(hash)), control, reannounce, button('Diagnóstico', 'secondary', () => diagnoseTransfer(hash)), remove);
  foot.append(rates, controls);
  card.append(header, metrics, track, foot);
  return { card, update(item) {
    const [label, color] = transferState(item);
    const progress = Math.max(0, Math.min(100, (item.progress || 0) * 100));
    const value = Math.floor(progress * 10) / 10;
    setText(title, item.name || hash);
    setText(badge, label + (item.priority>0?' · Fila '+item.priority:''));
    badge.dataset.status = color;
    setText(percent, value.toLocaleString('pt-BR', { maximumFractionDigits: 1 }) + '%');
    setText(bytes, storageSize(item.completed) + (item.size > 0 ? ' de ' + storageSize(item.size) : ''));
    fill.style.width = value + '%';
    track.setAttribute('aria-valuenow', value);
    const waitingForPeers = /meta|stalledDL/i.test(item.state || '') && color === 'wait';
    const peers = Number.isInteger(item.seeds) && item.seeds >= 0 &&
      Number.isInteger(item.leechers) && item.leechers >= 0 ? item.seeds + item.leechers : null;
    const peerStatus = peers === 0 ? 'Nenhum par conectado' : peers === null ? '' :
      peers.toLocaleString('pt-BR') + (peers === 1 ? ' par conectado' : ' pares conectados');
    reannounce.hidden = !waitingForPeers;
    setText(rates, (item.scheduled ? 'Início: '+new Date(item.scheduled).toLocaleString('pt-BR') : '') || item.fallback || (waitingForPeers ? peerStatus : color === 'done' ? 'Disponível no PC' :
      (item.speed > 0 ? storageSize(item.speed) + '/s · ' + timeLeft(item.eta) : '')));
    const nextMode = color === 'done' ? 'torrent-files' : color === 'paused' ? 'torrent-resume' : 'torrent-pause';
    card.dataset.status = color;
    if (mode !== nextMode) {
      mode = nextMode;
      control.dataset.action = mode;
      setText(control, mode === 'torrent-files' ? 'Assistir' : mode === 'torrent-resume' ? 'Retomar' : 'Pausar');
    }
    control.hidden = color === 'error' || /checking|moving|allocating/i.test(item.state || '');
  } };
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
  const counters = [
    [list.length, list.length === 1 ? 'transferência' : 'transferências'],
    [list.filter(item => item.progress >= 1).length, 'concluídos'],
    [storageSize(speed) + '/s', 'velocidade total']
  ];
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
  applyTransferFilter();
  rows.classList.remove('stale-transfers');
}

function applyTransferFilter() {
  let visible = 0;
  for (const { card } of transferCards.values()) {
    card.hidden = transferFilter !== 'all' && card.dataset.status !== transferFilter;
    if (!card.hidden) visible++;
  }
  for (const filter of document.querySelectorAll('[data-transfer-filter]'))
    filter.setAttribute('aria-pressed', filter.dataset.transferFilter === transferFilter);
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
setInterval(() => refreshTransfers(), 1500);

async function showSourceDownloads(movie, trigger) {
  const panel = $('#detail-content .detail-intro');
  const existing = panel.querySelector('.source-downloads');
  if (existing) {
    existing.scrollIntoView({ block: 'nearest' });
    return;
  }
  const choices = node('div', 'source-downloads');
  panel.append(choices);
  const message = node('p', '', 'Consultando opções da origem…');
  choices.append(node('h3', '', 'Fontes deste filme'), message);
  message.setAttribute('role', 'status');
  const sourceFolder = folderPicker();
  choices.append(sourceFolder.element);
  const selectEpisodes=smartField(choices,'Escolher arquivos antes de baixar (qBittorrent 5+)',null,'checkbox');
  choices.scrollIntoView({ block: 'nearest' });
  trigger.disabled = true;
  try {
    const sources = await transferAction('movie-sources', movie.id);
    let found = 0;
    for (const source of sources) {
    const group = node('section', 'source-group');
    group.append(node('h4', '', source.site || 'Origem'));
    choices.append(group);
    let options;
    try { options = await transferAction('download-options', source.id); }
    catch (error) { group.append(node('p', '', error.message)); continue; }
    found += options.length;
    if (!options.length) group.append(node('p', '', 'Nenhuma opção disponível nesta origem.'));
    for (const option of options) {
      const label = option.label + (option.kind === 'torrent' ? ' · qBittorrent' : option.kind === 'link' ? ' ↗' : '');
      group.append(button(label, 'secondary', async event => {
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
          if (choices.isConnected && $('#details').open) {
            $('#details').close();
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
      }));
    }
    }
    setText(message, found ? '' : 'Nenhuma fonte retornou opções. Confira as mensagens de cada site.');
    if (!found) choices.append(button('Consultar fontes novamente', 'secondary', () => {
      choices.remove();
      showSourceDownloads(movie, trigger);
    }));
  } catch (error) {
    setText(message, error.message || 'A consulta falhou. Tente novamente.');
    choices.append(button('Tentar novamente', 'secondary', () => {
      choices.remove();
      return showSourceDownloads(movie, trigger);
    }));
  } finally { trigger.disabled = false; }
}
