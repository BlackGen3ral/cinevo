const transferCards = new Map();
let transferRequest;
let transferRefreshQueued = false;

async function transferAction(name, id, address) {
  const response = await request('/api/action', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, id, address })
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
  controls.append(control, remove);
  foot.append(rates, controls);
  card.append(header, metrics, track, foot);
  return { card, update(item) {
    const [label, color] = transferState(item);
    const progress = Math.max(0, Math.min(100, (item.progress || 0) * 100));
    const value = Math.floor(progress * 10) / 10;
    setText(title, item.name || hash);
    setText(badge, label);
    badge.dataset.status = color;
    setText(percent, value.toLocaleString('pt-BR', { maximumFractionDigits: 1 }) + '%');
    setText(bytes, storageSize(item.completed) + (item.size > 0 ? ' de ' + storageSize(item.size) : ''));
    fill.style.width = value + '%';
    track.setAttribute('aria-valuenow', value);
    setText(rates, color === 'done' ? 'Disponível no PC' :
      (item.speed > 0 ? storageSize(item.speed) + '/s · ' + timeLeft(item.eta) : ''));
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
  rows.classList.remove('stale-transfers');
}

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
    const result = await transferAction(name, hash, address);
    if (name === 'torrent-files') {
      if (!result.length) throw new Error('Nenhum vídeo compatível foi encontrado na pasta do torrent.');
      const picker = $('#torrent-file-list');
      picker.replaceChildren(...result.map(file => button(file.name + ' · ' + storageSize(file.size), 'secondary',
        event => actOnTransfer('torrent-play', hash, event.currentTarget, String(file.index)))));
      $('#torrent-files').showModal();
    } else if (name === 'torrent-add') {
      setText($('#torrent-message'), 'Link enviado ao qBittorrent.');
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
$('#torrent-configure').onclick = event => actOnTransfer('torrent-configure', null, event.currentTarget);
$('#close-torrent-files').onclick = () => $('#torrent-files').close();
$('#torrent-form').onsubmit = async event => {
  event.preventDefault();
  const input = $('#torrent-magnet');
  if (await actOnTransfer('torrent-add', null, event.submitter, input.value.trim())) input.value = '';
};
setInterval(() => refreshTransfers(), 1500);

async function showSourceDownloads(movie, trigger) {
  const panel = $('#detail-content');
  let choices = panel.querySelector('.source-downloads');
  if (choices) return;
  choices = node('div', 'source-downloads');
  panel.append(choices);
  const message = node('p', '', 'Consultando opções da origem…');
  choices.append(node('h3', '', 'Baixar este conteúdo'), message);
  trigger.disabled = true;
  try {
    const options = await transferAction('download-options', movie.id);
    setText(message, options.length ? '' : 'Nenhuma opção publicada pela origem.');
    for (const option of options) {
      const label = option.label + (option.kind === 'torrent' ? ' · qBittorrent' : option.kind === 'link' ? ' ↗' : '');
      choices.append(button(label, 'secondary', async event => {
        const selected = event.currentTarget;
        if (option.kind === 'torrent') {
          $('#details').close();
          changeSection('downloads');
          $('#torrent-magnet').value = option.url;
          if (await actOnTransfer('torrent-add', null, selected, option.url)) $('#torrent-magnet').value = '';
          return;
        }
        selected.disabled = true;
        try { await transferAction('download-source', movie.id, option.url); }
        catch (error) { setText(message, error.message); }
        finally { selected.disabled = false; }
      }));
    }
  } catch (error) { setText(message, error.message); }
  finally { trigger.disabled = false; }
}
