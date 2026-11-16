let settingsLoading = false;
let settingsSubmitting = false;

function settingsTab(tab) {
  for (const item of document.querySelectorAll('[data-settings-tab]')) {
    const selected = item.dataset.settingsTab === tab;
    item.setAttribute('aria-selected', selected);
    item.tabIndex = selected ? 0 : -1;
    $('#settings-' + item.dataset.settingsTab).hidden = !selected;
  }
}

async function openSettings(tab = 'general') {
  if (settingsSubmitting) return;
  const dialog = $('#settings-dialog');
  if (!dialog.open) dialog.showModal();
  settingsTab(tab);
  if (settingsLoading) return;
  settingsLoading = true;
  $('#settings-status').textContent = 'Carregando configurações…';
  try {
    const data = await transferAction('settings-read');
    $('#setting-auto-update').checked = data.autoUpdate;
    $('#setting-auto-covers').checked = data.autoCovers;
    $('#settings-version').textContent = 'Cinevo ' + data.version + ' · Beta';
    $('#setting-port').value = data.port;
    $('#setting-user').value = data.username;
    $('#setting-no-password').checked = data.noPassword;
    $('#setting-credentials').hidden = data.noPassword;
    $('#setting-password').value = $('#setting-token').value = '';
    $('#setting-tmdb-status').textContent = data.tmdb ? 'Conectado ao TMDB.' : 'Conecte sua conta para enriquecer as fichas dos filmes.';
    $('#settings-tmdb-remove').disabled = !data.tmdb;
    $('#settings-dialog [data-command="update"]').disabled = !data.canUpdate;
    $('#settings-status').textContent = '';
  } catch (error) { $('#settings-status').textContent = error.message; }
  finally { settingsLoading = false; }
}

async function saveSettings(form, name, address, success) {
  if (settingsSubmitting) return;
  settingsSubmitting = true;
  const controls = [...form.querySelectorAll('button,input')];
  const disabled = controls.map(item => item.disabled);
  controls.forEach(item => item.disabled = true);
  $('#settings-status').textContent = 'Salvando…';
  try {
    await transferAction(name, null, address);
    $('#settings-status').textContent = success;
    if (name === 'settings-tmdb') {
      $('#setting-token').value = '';
      $('#setting-tmdb-status').textContent = 'Conectado ao TMDB.';
      $('#settings-tmdb-remove').disabled = false;
    }
    if (name === 'settings-torrent') {
      $('#setting-password').value = '';
      refreshTransfers(true);
    }
  } catch (error) { $('#settings-status').textContent = error.message; }
  finally {
    controls.forEach((item, index) => item.disabled = disabled[index]);
    settingsSubmitting = false;
  }
}

for (const tab of document.querySelectorAll('[data-settings-tab]')) {
  tab.onclick = () => settingsTab(tab.dataset.settingsTab);
  tab.onkeydown = event => {
    const tabs = [...document.querySelectorAll('[data-settings-tab]')];
    if (!['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
    event.preventDefault();
    const index = (tabs.indexOf(tab) + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
    tabs[index].click();
    tabs[index].focus();
  };
}
$('#settings-close').onclick = () => { if (!settingsSubmitting) $('#settings-dialog').close(); };
$('#settings-dialog').addEventListener('cancel', event => { if (settingsSubmitting) event.preventDefault(); });
$('#settings-dialog').addEventListener('close', () => {
  $('#setting-password').value = $('#setting-token').value = '';
});
$('#setting-no-password').onchange = () => $('#setting-credentials').hidden = $('#setting-no-password').checked;
$('#settings-preferences').onsubmit = event => {
  event.preventDefault();
  saveSettings(event.currentTarget, 'settings-save', JSON.stringify({
    autoUpdate: $('#setting-auto-update').checked, autoCovers: $('#setting-auto-covers').checked
  }), 'Preferências salvas.');
};
$('#settings-torrent-form').onsubmit = event => {
  event.preventDefault();
  saveSettings(event.currentTarget, 'settings-torrent', JSON.stringify({
    port: Number($('#setting-port').value), username: $('#setting-user').value.trim(),
    password: $('#setting-password').value, localWithoutPassword: $('#setting-no-password').checked
  }), 'Conexão verificada e salva.');
};
$('#settings-tmdb-form').onsubmit = event => {
  event.preventDefault();
  saveSettings(event.currentTarget, 'settings-tmdb', $('#setting-token').value.trim(), 'TMDB conectado.');
};
$('#settings-tmdb-remove').onclick = async () => {
  await saveSettings($('#settings-tmdb-form'), 'settings-tmdb-remove', null, 'TMDB desconectado.');
  const data = await transferAction('settings-read').catch(() => null);
  if (data && !data.tmdb) {
    $('#setting-tmdb-status').textContent = 'TMDB desconectado.';
    $('#settings-tmdb-remove').disabled = true;
  }
};
for (const [id, action] of [['settings-backup', 'settings-backup'], ['settings-data', 'settings-data'], ['settings-open-torrent', 'torrent-open']]) {
  $('#' + id).onclick = async event => {
    event.currentTarget.disabled = true;
    try { await transferAction(action); }
    catch (error) { $('#settings-status').textContent = error.message; }
    finally { $('#' + id).disabled = false; }
  };
}
$('#settings-clear').onclick = () => {
  $('#settings-dialog').close();
  $('#clear-library').click();
};

function folderPicker() {
  const picker = { value: null, element: node('div', 'download-folder') };
  const label = node('span', '', 'Pasta padrão do qBittorrent');
  const select = button('Escolher pasta', 'secondary', async () => {
    select.disabled = true;
    try {
      const result = await transferAction('settings-folder');
      if (result.folder) {
        picker.value = result.folder;
        label.textContent = result.folder;
      }
    } catch (error) { label.textContent = error.message; }
    finally { select.disabled = false; }
  });
  picker.element.append(label, select);
  return picker;
}

let historyPage = 1;
let historyLoading = false;
function releaseIcon(text) {
  const paths=/controle|analógico|direcional|teclado/i.test(text)?['M7 7h10a5 5 0 0 1 4.7 6.7l-1.1 3a2 2 0 0 1-3.3.8L15 15H9l-2.3 2.5a2 2 0 0 1-3.3-.8l-1.1-3A5 5 0 0 1 7 7','M7 9v5','M4.5 11.5h5','M16 10h.01','M19 13h.01']:
    /perfil|pessoa|foto/i.test(text)?['M8 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6','M2 20v-2a6 6 0 0 1 12 0v2','M16 5a3 3 0 0 1 0 6','M17 15a5 5 0 0 1 5 5']:
    /série|temporada|episódio/i.test(text)?['M4 4h16v16H4z','M4 9h16','M9 9v11','M13 13l4 2-4 2z']:
    /download|baixad|mídia|computador/i.test(text)?['M4 3h16v12H4z','M8 21h8','M12 15v6','M12 6v6','M9 9l3 3 3-3']:
    /novidades|histórico/i.test(text)?['M4 4h16v16H4z','M8 8h8','M8 12h8','M8 16h5']:
    /anima|brilho|visual|interface|foco/i.test(text)?['M12 3l2.7 6.3L21 12l-6.3 2.7L12 21l-2.7-6.3L3 12l6.3-2.7z']:
    ['M5 12l4 4L19 6','M4 3h16v18H4'];
  const holder=node('span','news-icon');holder.setAttribute('aria-hidden','true');
  const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');svg.setAttribute('viewBox','0 0 24 24');svg.setAttribute('fill','none');svg.setAttribute('stroke','currentColor');svg.setAttribute('stroke-width','1.5');svg.setAttribute('stroke-linecap','round');svg.setAttribute('stroke-linejoin','round');
  for(const d of paths){const path=document.createElementNS(svg.namespaceURI,'path');path.setAttribute('d',d);svg.append(path);}holder.append(svg);return holder;
}
for(const item of document.querySelectorAll('#news-current li')){const text=item.textContent;item.replaceChildren(releaseIcon(text),node('span','',text));}
async function loadHistory() {
  if (historyLoading) return;
  historyLoading = true;
  $('#news-more').disabled = true;
  $('#news-status').textContent = 'Carregando versões publicadas…';
  try {
    const releases = await transferAction('settings-history', null, String(historyPage));
    for (const release of releases) {
      if (!release.url?.startsWith('https://github.com/BlackGen3ral/cinevo/releases/')) continue;
      const item = node('article', 'settings-card release-note');
      const link = node('a', '', release.title || release.tag);
      link.href = release.url;
      link.target = '_blank';
      link.rel = 'noopener noreferrer';
      const title = node('h3');
      title.append(link);
      item.append(title);
      if (release.date) item.append(node('time', '', new Date(release.date).toLocaleDateString('pt-BR')));
      const notes = (release.notes || '').split('\n').filter(line => !/^\s*(?:##|\*\*Full Changelog|Full Changelog)/i.test(line))
        .map(line => line.replace(/\s+by @\S+.*$/, '').replace(/\s+in https:\/\/github\.com\/\S+$/, '').replace(/^\s*[-*]\s*/, ''))
        .filter(line => line.trim().length > 0).join('\n');
      const changes=node('ul','news-changes');for(const line of (notes||'Esta versão não tem descrição das mudanças publicada.').split('\n')){const row=node('li');row.append(releaseIcon(line),node('span','',line));changes.append(row);}item.append(changes);
      $('#news-history').append(item);
    }
    historyPage++;
    $('#news-more').hidden = releases.length < 30;
    $('#news-more').textContent = 'Versões anteriores';
    $('#news-status').textContent = '';
  } catch (error) { $('#news-status').textContent = 'Histórico indisponível agora. Tente novamente.'; }
  finally { historyLoading = false; $('#news-more').disabled = false; }
}
async function openNews() {
  if (!$('#news-dialog').open) $('#news-dialog').showModal();
  transferAction('settings-news-seen').catch(() => {});
  selectNewsTab(false);
}
function selectNewsTab(history) {
  $('#news-current').hidden = history;
  $('#news-archive').hidden = !history;
  $('#news-current-tab').setAttribute('aria-selected', !history);
  $('#news-history-tab').setAttribute('aria-selected', history);
  if (history && historyPage === 1) loadHistory();
}
$('#news-current-tab').onclick = () => selectNewsTab(false);
$('#news-history-tab').onclick = () => selectNewsTab(true);
$('#open-news').onclick = openNews;
$('#settings-news').onclick = () => { $('#settings-dialog').close(); openNews(); };
$('#news-close').onclick = () => $('#news-dialog').close();
$('#news-more').onclick = loadHistory;
setTimeout(async () => {
  try {
    const data = await transferAction('settings-read');
    if (data.showNews && !document.querySelector('dialog[open]')) openNews();
  } catch { }
}, 1500);
