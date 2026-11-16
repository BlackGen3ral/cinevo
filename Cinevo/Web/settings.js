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
      item.append(node('p', '', notes || 'Esta versão não tem descrição das mudanças publicada.'));
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
